using System.Text.RegularExpressions;

namespace ClipboardManager;

/// <summary>
/// 识别用户在系统「打开/保存」对话框内点击主确认按钮（确定/打开/保存等），用于在关闭前抓取当前文件夹路径。
/// 依赖 Win32 <c>Button</c> + 控件 ID / 标题启发式；自绘/DirectUI 命中非 Button 时可能识别不到。
/// </summary>
internal static class FileDialogConfirmClick
{
    /// <summary>
    /// 若点击落在某公共文件对话框内的主确认按钮上，返回该对话框 HWND。
    /// </summary>
    public static bool TryResolveDialogOnPrimaryConfirmClick(Win32.POINT pt, out IntPtr dialogHwnd)
    {
        dialogHwnd = IntPtr.Zero;
        var hit = Win32.WindowFromPoint(pt);
        if (hit == IntPtr.Zero || !Win32.IsWindow(hit)) return false;

        var dlg = FileDialogJumpHelper.ResolveFileDialogHwndFromWindowOrAncestor(hit);
        if (dlg == IntPtr.Zero || !Win32.IsWindow(dlg)) return false;

        var dlgRoot = Win32.GetAncestor(dlg, Win32.GA_ROOT);
        if (dlgRoot == IntPtr.Zero) return false;

        for (var h = hit; h != IntPtr.Zero; h = Win32.GetParent(h))
        {
            var root = Win32.GetAncestor(h, Win32.GA_ROOT);
            if (root != dlgRoot) break;
            if (h == dlg)
                break;

            var cls = Win32.GetWindowClassName(h);
            if (!cls.Equals("Button", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsConfirmActionButton(h))
                continue;

            dialogHwnd = dlg;
            return true;
        }

        return false;
    }

    private static bool IsConfirmActionButton(IntPtr buttonHwnd)
    {
        var id = Win32.GetDlgCtrlID(buttonHwnd);
        if (id == 2) return false;

        // 多数 IFileDialog / 经典公用对话框主按钮为 IDOK=1。
        if (id == 1)
            return true;

        var text = NormalizeAccessKey(Win32.GetWindowText(buttonHwnd));
        if (string.IsNullOrEmpty(text))
            return false;

        if (LooksLikeCancel(text))
            return false;

        return LooksLikeConfirm(text);
    }

    private static string NormalizeAccessKey(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return Regex.Replace(s, @"\(&.\)|\([^)]*\)", "").Trim();
    }

    private static bool LooksLikeCancel(string t)
    {
        var x = t.Trim();
        if (x.Length == 0) return false;
        if (x.Equals("Cancel", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.Equals("取消", StringComparison.Ordinal)) return true;
        if (x.StartsWith("不保存", StringComparison.Ordinal)) return true;
        if (x.Contains("Don't Save", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool LooksLikeConfirm(string t)
    {
        var x = t.Trim();
        if (x.Length == 0) return false;

        // 英文 / 西欧常见
        if (x.Equals("OK", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.Equals("Save", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.Equals("Open", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.StartsWith("Save ", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.StartsWith("Open ", StringComparison.OrdinalIgnoreCase)) return true;
        if (x.Contains("Save As", StringComparison.OrdinalIgnoreCase)) return true;

        // 简体中文
        if (x.Equals("确定", StringComparison.Ordinal)) return true;
        if (x.Equals("打开", StringComparison.Ordinal)) return true;
        if (x.Equals("保存", StringComparison.Ordinal)) return true;
        if (x.StartsWith("保存", StringComparison.Ordinal)) return true;
        if (x.StartsWith("打开", StringComparison.Ordinal)) return true;
        if (x.Equals("另存为", StringComparison.Ordinal)) return true;
        if (x.StartsWith("另存", StringComparison.Ordinal)) return true;
        if (x.Equals("选择", StringComparison.Ordinal)) return true;
        if (x.StartsWith("选择", StringComparison.Ordinal)) return true;
        if (x.Equals("完成", StringComparison.Ordinal)) return true;
        if (x.Equals("应用", StringComparison.Ordinal)) return true;

        return false;
    }
}
