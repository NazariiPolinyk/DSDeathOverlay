using System;
using System.Runtime.InteropServices;

namespace DSDeathOverlay.Memory;

/// <summary>
/// P/Invoke declarations for the Win32 APIs used by the overlay.
///
/// Strictly limited to:
///   - kernel32 / psapi: read-only access to another process's memory and module list.
///   - user32: cosmetic window styling (transparency, click-through, topmost) and global hotkeys.
///
/// We deliberately do NOT expose VirtualAllocEx, WriteProcessMemory, CreateRemoteThread,
/// or anything else that could be construed as injection.
/// </summary>
internal static class NativeMethods
{
    // --- Process access rights ---------------------------------------------------------------

    /// <summary>Read another process's address space.</summary>
    public const uint PROCESS_VM_READ = 0x0010;

    /// <summary>Required by <c>EnumProcessModulesEx</c> / <c>GetModuleInformation</c>.</summary>
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    /// <summary>
    /// Lighter variant that still allows module enumeration on Win Vista+ and works for
    /// elevated/system processes we don't need (we only want our own user's game).
    /// </summary>
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Combined access mask used when opening the game process. READ ONLY.</summary>
    public const uint READ_ONLY_PROCESS_ACCESS = PROCESS_VM_READ | PROCESS_QUERY_INFORMATION;

    // --- kernel32 / psapi --------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        IntPtr nSize,
        out IntPtr lpNumberOfBytesRead);

    /// <summary>List 64-bit modules only.</summary>
    public const uint LIST_MODULES_64BIT = 0x02;

    /// <summary>List 32-bit modules only (used to enumerate WoW64 processes like 32-bit DS2).</summary>
    public const uint LIST_MODULES_32BIT = 0x01;

    /// <summary>List both 32- and 64-bit modules.</summary>
    public const uint LIST_MODULES_ALL = 0x03;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool Wow64Process);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumProcessModulesEx(
        IntPtr hProcess,
        [Out] IntPtr[] lphModule,
        uint cb,
        out uint lpcbNeeded,
        uint dwFilterFlag);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetModuleBaseNameW(
        IntPtr hProcess,
        IntPtr hModule,
        [Out] System.Text.StringBuilder lpBaseName,
        uint nSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetModuleInformation(
        IntPtr hProcess,
        IntPtr hModule,
        out MODULEINFO lpmodinfo,
        uint cb);

    // --- user32: window styling --------------------------------------------------------------

    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_TRANSPARENT = 0x00000020; // clicks pass through
    public const int WS_EX_TOOLWINDOW  = 0x00000080; // no taskbar / alt-tab entry
    public const int WS_EX_TOPMOST     = 0x00000008;
    public const int WS_EX_NOACTIVATE  = 0x08000000; // never steals focus
    public const int WS_EX_LAYERED     = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy,
        uint uFlags);

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOSIZE     = 0x0001;
    public const uint SWP_NOMOVE     = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // --- user32: global hotkeys --------------------------------------------------------------

    public const uint MOD_NONE  = 0x0000;
    public const uint MOD_ALT   = 0x0001;
    public const uint MOD_CTRL  = 0x0002;
    public const uint MOD_SHIFT = 0x0004;

    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
