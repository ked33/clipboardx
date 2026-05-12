using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClipboardManager;

/// <summary>
/// 无边框窗口边缘拖拽调整尺寸的 WM_NCHITTEST / WM_SIZING 处理。
/// </summary>
internal static class WindowResizeHelper
{
    public const int ResizeBorder = 8;

    /// <summary>
    /// 处理 WM_NCHITTEST：根据鼠标位置返回边缘/角落 hit-test 值。
    /// <paramref name="margin"/> 以内才检测边缘，避免拦截窗口内部控件的交互。
    /// </summary>
    public static IntPtr HandleNcHitTest(IntPtr hwnd, IntPtr lParam, double margin, ref bool handled)
    {
        int screenX = (short)(lParam.ToInt32() & 0xFFFF);
        int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

        if (!Win32.GetWindowRect(hwnd, out var rc))
            return IntPtr.Zero;

        var src = HwndSource.FromHwnd(hwnd);
        double dipX = 1, dipY = 1;
        if (src?.CompositionTarget != null)
        {
            dipX = src.CompositionTarget.TransformFromDevice.M11;
            dipY = src.CompositionTarget.TransformFromDevice.M22;
        }

        double relX = (screenX - rc.Left) * dipX;
        double relY = (screenY - rc.Top) * dipY;
        double winW = (rc.Right - rc.Left) * dipX;
        double winH = (rc.Bottom - rc.Top) * dipY;

        int border = ResizeBorder;
        bool left = relX < border;
        bool right = relX >= winW - border;
        bool top = relY < border;
        bool bottom = relY >= winH - border;

        // 只在窗口边缘 margin 区域内做 resize hit test，内部让 WPF 正常处理
        bool inMarginArea = relX < margin || relX >= winW - margin
                         || relY < margin || relY >= winH - margin;
        if (!inMarginArea)
            return IntPtr.Zero;

        if (left && top) { handled = true; return new IntPtr(Win32.HTTOPLEFT); }
        if (right && top) { handled = true; return new IntPtr(Win32.HTTOPRIGHT); }
        if (left && bottom) { handled = true; return new IntPtr(Win32.HTBOTTOMLEFT); }
        if (right && bottom) { handled = true; return new IntPtr(Win32.HTBOTTOMRIGHT); }
        if (left) { handled = true; return new IntPtr(Win32.HTLEFT); }
        if (right) { handled = true; return new IntPtr(Win32.HTRIGHT); }
        if (top) { handled = true; return new IntPtr(Win32.HTTOP); }
        if (bottom) { handled = true; return new IntPtr(Win32.HTBOTTOM); }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 处理 WM_SIZING：钳制窗口尺寸到 [min, max] 范围，并标记需要禁用 SizeToContent。
    /// 返回值表示是否需要禁用 SizeToContent（首次 resize 时为 true）。
    /// </summary>
    public static bool HandleWmSizing(IntPtr hwnd, IntPtr wParam, IntPtr lParam,
        double minW, double maxW, double minH, double maxH, bool alreadyResized)
    {
        var rc = Marshal.PtrToStructure<Win32.RECT>(lParam);
        int edge = wParam.ToInt32();

        var src = HwndSource.FromHwnd(hwnd);
        double scaleX = 1, scaleY = 1;
        if (src?.CompositionTarget != null)
        {
            scaleX = src.CompositionTarget.TransformFromDevice.M11;
            scaleY = src.CompositionTarget.TransformFromDevice.M22;
        }

        double wpfW = (rc.Right - rc.Left) * scaleX;
        double wpfH = (rc.Bottom - rc.Top) * scaleY;

        bool changed = false;
        if (wpfW < minW) { wpfW = minW; changed = true; }
        if (wpfW > maxW) { wpfW = maxW; changed = true; }
        if (wpfH < minH) { wpfH = minH; changed = true; }
        if (wpfH > maxH) { wpfH = maxH; changed = true; }

        if (changed)
        {
            int physW = (int)(wpfW / scaleX);
            int physH = (int)(wpfH / scaleY);

            switch (edge)
            {
                case Win32.WMSZ_RIGHT:
                case Win32.WMSZ_TOPRIGHT:
                case Win32.WMSZ_BOTTOMRIGHT:
                    rc.Right = rc.Left + physW; break;
                case Win32.WMSZ_LEFT:
                case Win32.WMSZ_TOPLEFT:
                case Win32.WMSZ_BOTTOMLEFT:
                    rc.Left = rc.Right - physW; break;
            }
            switch (edge)
            {
                case Win32.WMSZ_BOTTOM:
                case Win32.WMSZ_BOTTOMLEFT:
                case Win32.WMSZ_BOTTOMRIGHT:
                    rc.Bottom = rc.Top + physH; break;
                case Win32.WMSZ_TOP:
                case Win32.WMSZ_TOPLEFT:
                case Win32.WMSZ_TOPRIGHT:
                    rc.Top = rc.Bottom - physH; break;
            }

            Marshal.StructureToPtr(rc, lParam, false);
        }

        // 返回 true 表示需要禁用 SizeToContent
        return !alreadyResized;
    }
}
