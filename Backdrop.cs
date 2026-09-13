using System;

namespace HWWidget;

/// <summary>Applies the window material (accent blur / Mica / Acrylic) to any HWND,
/// shared by the widgets and the control hub.</summary>
static class Backdrop
{
    public static void Apply(IntPtr hwnd, string kind, Palette p)
    {
        if (hwnd == IntPtr.Zero) return;

        int dark = p.IsLight ? 0 : 1, round = 2, noBorder = Native.DWMWA_COLOR_NONE;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref noBorder, 4);

        if (kind == "blur")
        {
            int none = Native.DWMSBT_NONE;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref none, 4);
            uint tint = Native.Abgr(0x2E, p.Panel.R, p.Panel.G, p.Panel.B);
            Native.ApplyAccent(hwnd, Native.ACCENT_ENABLE_ACRYLICBLURBEHIND, tint);
            return;
        }

        Native.ApplyAccent(hwnd, Native.ACCENT_DISABLED, 0, 0);
        int type = kind switch { "mica" => 2, "micaalt" => 4, "acrylic" => 3, _ => 1 };
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
    }
}
