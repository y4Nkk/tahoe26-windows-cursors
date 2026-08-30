using System.Runtime.InteropServices;

namespace TahoeCursorStudio.Infrastructure;

internal static class NativeMethods
{
    internal const uint ImageCursor = 2;
    internal const uint LrLoadFromFile = 0x0010;
    internal const uint DiNormal = 0x0003;
    internal const uint SpiSetCursors = 0x0057;
    internal const uint SpifUpdateIniFile = 0x0001;
    internal const uint SpifSendChange = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyCursor(nint cursor);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemCursor(nint cursor, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfoW(uint action, uint parameter, nint value, uint flags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileExW(string existingFile, string? newFile, uint flags);
}
