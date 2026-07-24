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
    private static FileJumpHost? s_foregroundOwner;
    private static readonly Win32.WinEventDelegate s_foregroundThunk = ForegroundChanged;

    private readonly Dispatcher _dispatcher;
    private AppSettings? _settings;
    private uint _hotkeyModifiers;
    private uint _hotkeyKey;
    private uint _listHotkeyModifiers;
    private uint _listHotkeyKey;
    private IntPtr _keyboardHook;
    private IntPtr _foregroundHook;
    private int _collectGeneration;
    private int _externalPathCaptureGeneration;
    private string _latestExternalPath = "";
    private long _externalPathVersion;
    private readonly HashSet<IntPtr> _seenDialogRoots = new();
    private IntPtr _lastAutoSyncDialogRoot;
    private long _lastAutoSyncVersion;
    private long _lastHotkeyTick;
    private FileDialogJumpPickerWindow? _activePicker;

    public FileJumpHost(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Initialize(AppSettings settings)
    {
        ApplySettings(settings);
        InstallKeyboardHook();
        InstallForegroundHook();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _hotkeyModifiers = settings.FileJumpHotkeyModifiers;
        _hotkeyKey = settings.FileJumpHotkeyKey;
        _listHotkeyModifiers = settings.FileJumpListHotkeyModifiers;
        _listHotkeyKey = settings.FileJumpListHotkeyKey;
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

    private void InstallForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        s_foregroundOwner = this;
        _foregroundHook = Win32.SetWinEventHook(
            Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, s_foregroundThunk, 0, 0,
            Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
        if (_foregroundHook == IntPtr.Zero && ReferenceEquals(s_foregroundOwner, this))
            s_foregroundOwner = null;
    }

    private static void ForegroundChanged(
        IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint threadId, uint eventTime)
    {
        var owner = s_foregroundOwner;
        if (owner == null || hwnd == IntPtr.Zero) return;
        owner._dispatcher.BeginInvoke(() => owner.OnForegroundChanged(hwnd), DispatcherPriority.Background);
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        if (_settings == null || hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) return;

        var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        if (root == IntPtr.Zero) root = hwnd;
        var windowClass = Win32.GetWindowClassName(root);

        // 只跟踪明确支持的 Explorer 与 DOpus；不枚举其它窗口，也不进行通用 UIA 树扫描。
        if (windowClass is "CabinetWClass" or "ExploreWClass" or "dopus.lister")
        {
            CaptureExternalPath(root);
            return;
        }

        // 常规窗口先走快速类名判断；仅自定义规则命中时补充进入解析，避免每次前台切换都做重型探测。
        if (!FileDialogJumpHelper.QuickMayBeUnderFileDialog(hwnd)
            && CustomFileDialogStore.FindMatchingRule(root) == null)
            return;

        var dialog = FileDialogJumpHelper.ResolveFileDialogHwndFromWindowOrAncestor(hwnd);
        if (dialog != IntPtr.Zero)
            TryAutoSyncLatestExternalPath(dialog);
    }

    private void CaptureExternalPath(IntPtr managerHwnd)
    {
        var generation = Interlocked.Increment(ref _externalPathCaptureGeneration);
        void Capture()
        {
            string path;
            try
            {
                path = FileManagerPathCollector.TryGetFolderForWindow(managerHwnd, fresh: true) ?? "";
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (generation != Volatile.Read(ref _externalPathCaptureGeneration)) return;
                if (string.Equals(_latestExternalPath, path, StringComparison.OrdinalIgnoreCase)) return;
                _latestExternalPath = path;
                _externalPathVersion++;
            }, DispatcherPriority.Background);
        }

        var thread = new Thread(Capture)
        {
            IsBackground = true,
            Name = "ClipboardX-FileJump-TrackPath",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void TryAutoSyncLatestExternalPath(IntPtr dialog)
    {
        var dialogRoot = Win32.GetAncestor(dialog, Win32.GA_ROOT);
        if (dialogRoot == IntPtr.Zero) dialogRoot = dialog;
        // 首次打开只登记；必须离开并在 Explorer/DOpus 路径发生变化后再次返回，才执行自动同步。
        if (_seenDialogRoots.Add(dialogRoot)) return;

        var settings = _settings;
        var path = _latestExternalPath;
        var version = _externalPathVersion;
        if (settings == null || string.IsNullOrEmpty(path) || version == 0) return;
        if (_lastAutoSyncDialogRoot == dialogRoot && _lastAutoSyncVersion == version) return;

        _lastAutoSyncDialogRoot = dialogRoot;
        _lastAutoSyncVersion = version;
        var allowInject = settings.EnableShellNavigateInject;
        var thread = new Thread(() =>
            FileDialogJumpHelper.TryNavigateToFolder(dialog, path, allowInject))
        {
            IsBackground = true,
            Name = "ClipboardX-FileJump-AutoSync",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
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
        var directJump = keyboard.vkCode == _hotkeyKey && ModifiersMatch(_hotkeyModifiers);
        var openList = keyboard.vkCode == _listHotkeyKey && ModifiersMatch(_listHotkeyModifiers);
        if (!directJump && !openList)
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

        _dispatcher.BeginInvoke(() => ExecuteForDialog(dialog, openList), DispatcherPriority.Send);
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

    private void ExecuteForDialog(IntPtr dialog, bool openList)
    {
        var settings = _settings;
        if (settings == null || dialog == IntPtr.Zero || !Win32.IsWindow(dialog)) return;

        if (openList && _activePicker != null)
        {
            try { _activePicker.Activate(); } catch { }
            return;
        }

        var generation = Interlocked.Increment(ref _collectGeneration);
        var recentFolders = settings.GetRecentFoldersForJump();

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
                if (openList)
                {
                    ShowPicker(candidates, dialog, settings);
                    return;
                }

                if (candidates.Count == 0) return;
                var path = candidates[0].Path;
                var allowInject = settings.EnableShellNavigateInject;
                var navigateThread = new Thread(() =>
                    FileDialogJumpHelper.TryNavigateToFolder(dialog, path, allowInject))
                {
                    IsBackground = true,
                    Name = "ClipboardX-FileJumpHost-DirectNavigate",
                };
                navigateThread.SetApartmentState(ApartmentState.STA);
                navigateThread.Start();
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
        if (_foregroundHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (ReferenceEquals(s_foregroundOwner, this))
            s_foregroundOwner = null;
        try { _activePicker?.Close(); } catch { }
        _activePicker = null;
    }
}
