using Microsoft.Win32;

namespace ClipboardManager;

/// <summary>
/// 管理系统剪贴板历史功能（Win+V）的启用/禁用。
/// 注册表路径：HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory
/// </summary>
internal static class SystemClipboardHelper
{
    private const string ClipboardKeyPath = @"Software\Microsoft\Clipboard";
    private const string EnableClipboardHistoryValue = "EnableClipboardHistory";

    /// <summary>获取系统剪贴板历史是否启用。</summary>
    public static bool IsSystemClipboardHistoryEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ClipboardKeyPath, false);
            if (key == null) return true; // 默认启用
            var val = key.GetValue(EnableClipboardHistoryValue);
            if (val == null) return true;
            return Convert.ToInt32(val) != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>设置系统剪贴板历史的启用状态。</summary>
    public static bool SetSystemClipboardHistoryEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ClipboardKeyPath, true);
            if (key == null)
            {
                using var newKey = Registry.CurrentUser.CreateSubKey(ClipboardKeyPath);
                if (newKey == null) return false;
                newKey.SetValue(EnableClipboardHistoryValue, enabled ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            key.SetValue(EnableClipboardHistoryValue, enabled ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
