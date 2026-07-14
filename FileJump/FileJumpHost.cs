using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClipboardManager;

/// <summary>
/// FileJumpOnly 的轻量宿主。快捷键通过低级键盘钩子观察，但仅当前台窗口可解析为
/// 文件对话框时才拦截并执行；不会向系统注册全局热键，也不会占用其它应用中的组合键。
/// </summary>
internal sealed class FileJumpHost : IDisposable
{
    private static FileJumpHost? s_keyboardOwner;
    private static readonly Win32.LowLevelKeyboardProc s_keyboardThunk = KeyboardHookProc;

    private readonly Dispatcher _dispatcher;
    private AppSettings? _settings;
    private uint _hotkeyModifiers;
    private uint _hotkeyKey;
    private IntPtr _keyboardHook;
    private int _collectGeneration;
    private long _lastHotkeyTick;
    private FileDialogJumpPickerWindow? _activePicker;

    public FileJumpHost(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Initialize(AppSettings settings)
    {
        ApplySettings(settings);
        InstallKeyboardHook();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _hotkeyModifiers = settings.FileJumpHotkeyModifiers;
        _hotkeyKey = settings.FileJumpHotkeyKey;
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero) return;
        s_keyboardOwner = this;
        _keyboardHook = Win32.SetWindowsHookEx(
            Win32.WH_KEYBOARD_LL, s_keyboardThunk, Win32.GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero && ReferenceEquals(s_keyboardOwner, this))
            s_keyboardOwner = null;
    }

    private static IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var owner = s_keyboardOwner;
        if (owner == null || owner._keyboardHook == IntPtr.Zero)
            return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        return owner.OnKeyboardHook(nCode, wParam, lParam);
    }

    private IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam != (IntPtr)Win32.WM_KEYDOWN && wParam != (IntPtr)Win32.WM_SYSKEYDOWN))
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var keyboard = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
        if (keyboard.vkCode != _hotkeyKey || !ModifiersMatch(_hotkeyModifiers))
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var foreground = Win32.GetForegroundWindow();
        var dialog = FileDialogJumpHelper.ResolveFileDialogHwndFromWindowOrAncestor(foreground);
        if (dialog == IntPtr.Zero || IsForegroundAppExcluded())
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        // 键盘自动重复不应并发启动多个路径采集线程。
        var now = Environment.TickCount64;
        if (now - _lastHotkeyTick < 250)
            return (IntPtr)1;
        _lastHotkeyTick = now;

        _dispatcher.BeginInvoke(() => OpenPickerForDialog(dialog), DispatcherPriority.Send);
        return (IntPtr)1;
    }

    private static bool ModifiersMatch(uint expected)
    {
        var actual = 0u;
        if (IsKeyDown(0x12)) actual |= Win32.MOD_ALT;
        if (IsKeyDown(0x11)) actual |= Win32.MOD_CONTROL;
        if (IsKeyDown(0x10)) actual |= Win32.MOD_SHIFT;
        if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) actual |= Win32.MOD_WIN;
        return actual == (expected & (Win32.MOD_ALT | Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_WIN));
    }

    private static bool IsKeyDown(int virtualKey) =>
        (Win32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private bool IsForegroundAppExcluded()
    {
        var settings = _settings;
        if (settings == null || settings.ExclusionApps.Count == 0) return false;
        try
        {
            var hwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return settings.ExclusionApps.Any(x =>
                string.Equals(Path.GetFileNameWithoutExtension(x), name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void OpenPickerForDialog(IntPtr dialog)
    {
        var settings = _settings;
        if (settings == null || dialog == IntPtr.Zero || !Win32.IsWindow(dialog)) return;

        if (_activePicker != null)
        {
            try { _activePicker.Activate(); } catch { }
            return;
        }

        var generation = Interlocked.Increment(ref _collectGeneration);
        var recentFolders = settings.RecentFileDialogFolders.ToList();

        void Collect()
        {
            List<FileJumpCandidate> candidates;
            try
            {
                candidates = FileManagerPathCollector.CollectCandidates(
                    dialog, null, shouldAbort: () => generation != Volatile.Read(ref _collectGeneration),
                    recentFolders: recentFolders);
            }
            catch (Exception ex)
            {
                ShellNavigateLog.Write("filejump", "FileJumpHost CollectCandidates: " + ex);
                candidates = new List<FileJumpCandidate>();
            }

            _dispatcher.BeginInvoke(() =>
            {
                if (generation != Volatile.Read(ref _collectGeneration)) return;
                ShowPicker(candidates, dialog, settings);
            }, DispatcherPriority.Normal);
        }

        var thread = new Thread(Collect)
        {
            IsBackground = true,
            Name = "ClipboardX-FileJumpHost-Collect",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ShowPicker(List<FileJumpCandidate> candidates, IntPtr dialog, AppSettings settings)
    {
        Win32.GetCursorPos(out var cursor);
        var picker = new FileDialogJumpPickerWindow(candidates, 0, cursor.X, cursor.Y, settings, dialog);
        _activePicker = picker;
        picker.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activePicker, picker))
                _activePicker = null;
        };
        picker.Show();
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _collectGeneration);
        if (_keyboardHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (ReferenceEquals(s_keyboardOwner, this))
            s_keyboardOwner = null;
        try { _activePicker?.Close(); } catch { }
        _activePicker = null;
    }
}
