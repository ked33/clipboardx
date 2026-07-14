using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClipboardManager;

/// <summary>
/// FileJumpOnly 的轻量级消息宿主。仅创建隐藏的 Win32 消息窗口，
/// 不创建剪贴板 PopupWindow，也不加载其 XAML/历史库。
/// </summary>
internal sealed class FileJumpHost : IDisposable
{
    private static FileJumpHost? s_foregroundOwner;
    private static readonly Win32.WinEventDelegate s_foregroundThunk = ForegroundChanged;
    private const int HotkeyId = 9002;
    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _source;
    private AppSettings? _settings;
    private uint _hotkeyModifiers;
    private uint _hotkeyKey;
    private int _collectGeneration;
    private FileDialogJumpPickerWindow? _activePicker;
    private IntPtr _foregroundHook;
    private IntPtr _lastAutoDialogRoot;

    public FileJumpHost(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        var parameters = new HwndSourceParameters("ClipboardX.FileJumpHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        _hotkeyModifiers = settings.FileJumpHotkeyModifiers;
        _hotkeyKey = settings.FileJumpHotkeyKey;
        if (!RegisterHotkey(_hotkeyModifiers, _hotkeyKey))
        {
            System.Windows.MessageBox.Show(
                $"快捷键 {settings.FileJumpHotkeyDisplayName}（文件对话框跳转）注册失败，可能与其他软件冲突",
                "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        InstallForegroundHook();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (settings.FileJumpHotkeyModifiers == _hotkeyModifiers
            && settings.FileJumpHotkeyKey == _hotkeyKey)
            return;

        var oldModifiers = _hotkeyModifiers;
        var oldKey = _hotkeyKey;
        Win32.UnregisterHotKey(_source.Handle, HotkeyId);
        if (RegisterHotkey(settings.FileJumpHotkeyModifiers, settings.FileJumpHotkeyKey))
        {
            _hotkeyModifiers = settings.FileJumpHotkeyModifiers;
            _hotkeyKey = settings.FileJumpHotkeyKey;
            return;
        }

        RegisterHotkey(oldModifiers, oldKey);
        settings.FileJumpHotkeyModifiers = oldModifiers;
        settings.FileJumpHotkeyKey = oldKey;
        System.Windows.MessageBox.Show(
            $"文件对话框跳转快捷键 {settings.FileJumpHotkeyDisplayName} 注册失败，已恢复原快捷键",
            "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    private bool RegisterHotkey(uint modifiers, uint key) =>
        Win32.RegisterHotKey(_source.Handle, HotkeyId, modifiers | Win32.MOD_NOREPEAT, key);

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
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint threadId, uint eventTime)
    {
        var owner = s_foregroundOwner;
        if (owner == null || hwnd == IntPtr.Zero) return;
        owner._dispatcher.BeginInvoke(() => owner.OnForegroundChanged(hwnd), DispatcherPriority.Background);
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        var settings = _settings;
        if (settings == null) return;
        if (!settings.FileJumpPickerOpenWhenDialogForeground
            && !settings.FileJumpAutoOnFirstClick
            && !settings.FileJumpAutoSyncOnReturn)
            return;

        var dialog = FileDialogJumpHelper.ResolveFileDialogHwndFromWindowOrAncestor(hwnd);
        if (dialog == IntPtr.Zero) return;
        var root = Win32.GetAncestor(dialog, Win32.GA_ROOT);
        var firstVisit = root != IntPtr.Zero && root != _lastAutoDialogRoot;
        if (firstVisit)
            _lastAutoDialogRoot = root;

        if (!firstVisit && !settings.FileJumpAutoSyncOnReturn)
            return;
        if (firstVisit && !settings.FileJumpPickerOpenWhenDialogForeground
            && !settings.FileJumpAutoOnFirstClick)
            return;

        var delay = Math.Clamp(settings.FileJumpPickerShowDelayMs, 0, 10000);
        var generation = Interlocked.Increment(ref _collectGeneration);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(16, delay)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (generation != Volatile.Read(ref _collectGeneration)) return;
            CollectAndApplyAutomaticAction(dialog, firstVisit, settings, generation);
        };
        timer.Start();
    }

    private void CollectAndApplyAutomaticAction(
        IntPtr dialog, bool firstVisit, AppSettings settings, int generation)
    {
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
                ShellNavigateLog.Write("filejump", "FileJumpHost automatic collect: " + ex);
                return;
            }

            _dispatcher.BeginInvoke(() =>
            {
                if (generation != Volatile.Read(ref _collectGeneration) || candidates.Count == 0) return;
                if ((firstVisit && settings.FileJumpAutoOnFirstClick)
                    || (!firstVisit && settings.FileJumpAutoSyncOnReturn))
                {
                    var path = candidates[0].Path;
                    var allowInject = settings.EnableShellNavigateInject;
                    var thread = new Thread(() =>
                        FileDialogJumpHelper.TryNavigateToFolder(dialog, path, allowInject))
                    {
                        IsBackground = true,
                        Name = "ClipboardX-FileJumpHost-Navigate",
                    };
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                }

                if (firstVisit && settings.FileJumpPickerOpenWhenDialogForeground && _activePicker == null)
                    ShowPicker(candidates, dialog, settings);
            }, DispatcherPriority.Normal);
        }

        var collector = new Thread(Collect)
        {
            IsBackground = true,
            Name = "ClipboardX-FileJumpHost-AutoCollect",
        };
        collector.SetApartmentState(ApartmentState.STA);
        collector.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            if (IsForegroundAppExcluded()) return IntPtr.Zero;
            OpenPickerForForegroundWindow();
        }
        return IntPtr.Zero;
    }

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

    private void OpenPickerForForegroundWindow()
    {
        var settings = _settings;
        if (settings == null) return;

        if (_activePicker != null)
        {
            try { _activePicker.Activate(); } catch { }
            return;
        }

        var foreground = Win32.GetForegroundWindow();
        var dialog = FileDialogJumpHelper.ResolveFileDialogHwndFromWindowOrAncestor(foreground);
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

            if (dialog == IntPtr.Zero)
                AddSavedFolders(candidates, settings);

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

    private static void AddSavedFolders(List<FileJumpCandidate> candidates, AppSettings settings)
    {
        var seen = new HashSet<string>(candidates.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var favorite in settings.FolderFavorites)
        {
            var path = favorite.Path?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && seen.Add(path))
                candidates.Add(new FileJumpCandidate("收藏", path));
        }
        foreach (var path in settings.RecentFileDialogFolders)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && seen.Add(path))
                candidates.Add(new FileJumpCandidate("常用", path));
        }
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
        if (_foregroundHook != IntPtr.Zero)
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (ReferenceEquals(s_foregroundOwner, this))
            s_foregroundOwner = null;
        Win32.UnregisterHotKey(_source.Handle, HotkeyId);
        try { _activePicker?.Close(); } catch { }
        _activePicker = null;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
