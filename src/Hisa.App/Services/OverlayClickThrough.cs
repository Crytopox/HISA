using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Hisa.App.Services;

internal static class OverlayClickThrough
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x00000020;
    private const nint WsExLayered = 0x00080000;

    public static void Set(Window window, bool clickThrough)
    {
        window.IsHitTestVisible = !clickThrough;
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return;
        }

        var exStyle = GetExtendedStyle(handle);
        var newStyle = clickThrough
            ? exStyle | WsExLayered | WsExTransparent
            : exStyle & ~WsExTransparent;
        if (newStyle != exStyle)
        {
            SetExtendedStyle(handle, newStyle);
        }
    }

    private static nint GetExtendedStyle(nint hwnd) => nint.Size == 8
        ? GetWindowLongPtr64(hwnd, GwlExStyle)
        : GetWindowLong32(hwnd, GwlExStyle);

    private static void SetExtendedStyle(nint hwnd, nint style)
    {
        if (nint.Size == 8)
        {
            SetWindowLongPtr64(hwnd, GwlExStyle, style);
            return;
        }

        SetWindowLong32(hwnd, GwlExStyle, (int)style);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);
}
