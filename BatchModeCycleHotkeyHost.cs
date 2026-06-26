using System;
using System.Windows.Forms;

namespace ClipboardManager;

#if CLIPX_CLIPBOARD
/// <summary>
/// 仅 HWND_MESSAGE，剪贴板面板隐藏后仍可靠收到批量模式切换的 WM_HOTKEY。
/// </summary>
internal sealed class BatchModeCycleHotkeyHost : NativeWindow
{
    public const int HotkeyId = 9003;
    private bool _registered;
    public uint CurrentModifiers { get; private set; }
    public uint CurrentKey { get; private set; }
    public event Action? CycleRequested;
    /// <summary>设置为 true 时，WndProc 在触发前跳过前台排除检查。</summary>
    internal Func<bool>? IsForegroundAppExcluded { get; set; }

    public BatchModeCycleHotkeyHost()
    {
        var cp = new CreateParams
        {
            Caption = "",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        };
        CreateHandle(cp);
    }

    public bool TryRegister(uint modifiers, uint vk)
    {
        if (Handle == IntPtr.Zero) return false;
        Win32.UnregisterHotKey(Handle, HotkeyId);
        if (Win32.RegisterHotKey(Handle, HotkeyId, modifiers | Win32.MOD_NOREPEAT, vk))
        {
            CurrentModifiers = modifiers;
            CurrentKey = vk;
            _registered = true;
            return true;
        }
        if (_registered)
            Win32.RegisterHotKey(Handle, HotkeyId, CurrentModifiers | Win32.MOD_NOREPEAT, CurrentKey);
        return false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            if (IsForegroundAppExcluded?.Invoke() == true) return;
            CycleRequested?.Invoke();
            return;
        }
        base.WndProc(ref m);
    }

    public void DisposeHost()
    {
        if (Handle != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
        _registered = false;
    }
}
#endif
