using System.Runtime.InteropServices;

namespace Tejun.App.Platform.Windows;

/// <summary>撮った画面の生の画素（BGRA、上から下）。</summary>
internal sealed record RawImage(byte[] Pixels, int Width, int Height, RECT Bounds);

internal static class ScreenCapture
{
    public static RECT VirtualScreen()
    {
        var x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        return new RECT
        {
            Left = x, Top = y,
            Right = x + Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
            Bottom = y + Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN),
        };
    }

    public static RECT MonitorAt(POINT pt, out double scale)
    {
        var monitor = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        scale = 1.0;
        try
        {
            if (Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        }
        catch (Exception) { /* 古い Windows では取れない */ }
        return Native.GetMonitorInfo(monitor, ref info) ? info.rcMonitor : VirtualScreen();
    }

    /// <summary>
    /// 画面の指定した範囲を、今映っているとおりに撮る。メニューやツールチップのような重なった表示も含め、
    /// マウスカーソルも描き込む。
    /// </summary>
    public static RawImage? Capture(RECT bounds, bool drawCursor = true)
    {
        int w = bounds.Width, h = bounds.Height;
        if (w <= 0 || h <= 0) return null;

        var screen = Native.GetDC(IntPtr.Zero);
        var memory = Native.CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // 負の値で、上から下へ並ぶ画像にする
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Native.CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
            return null;
        }

        var old = Native.SelectObject(memory, dib);
        try
        {
            if (!Native.BitBlt(memory, 0, 0, w, h, screen, bounds.Left, bounds.Top, Native.SRCCOPY | Native.CAPTUREBLT))
                return null;
            if (drawCursor) DrawCursor(memory, bounds);
            Native.GdiFlush();
            var pixels = new byte[w * h * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new RawImage(pixels, w, h, bounds);
        }
        finally
        {
            Native.SelectObject(memory, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void DrawCursor(IntPtr hdc, RECT bounds)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        const uint CURSOR_SHOWING = 1;
        if (!Native.GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero) return;
        int hotX = 0, hotY = 0;
        if (Native.GetIconInfo(info.hCursor, out var icon))
        {
            hotX = icon.xHotspot;
            hotY = icon.yHotspot;
            if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
        }
        const uint DI_NORMAL = 3;
        Native.DrawIconEx(hdc, info.ptScreenPos.X - hotX - bounds.Left, info.ptScreenPos.Y - hotY - bounds.Top,
            info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
    }
}
