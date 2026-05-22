using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Read-only wrapper around a remote (game) process.
///
/// Owns the OS handle returned by <c>OpenProcess</c>. The handle is opened with
/// <see cref="NativeMethods.READ_ONLY_PROCESS_ACCESS"/> only — no write or thread-creation rights.
/// Disposable; safe to keep alive for the duration of the overlay session.
/// </summary>
public sealed class ProcessAccess : IMemoryReader, IDisposable
{
    private readonly ILogger _log;
    private IntPtr _handle;
    private bool _disposed;

    public int ProcessId { get; }

    /// <summary>Base address of the active game's main module inside the remote process.</summary>
    public ulong ModuleBase { get; }

    /// <summary>Size of the matched module image in bytes.</summary>
    public uint ModuleSize { get; }

    public ulong ModuleEnd => ModuleBase + ModuleSize;

    /// <summary>
    /// True if the target process is 32-bit running under WoW64. False for native x64
    /// processes. Used by <see cref="PointerChainDeathReader"/> to pick the right chain.
    /// </summary>
    public bool IsWow64 { get; }

    private ProcessAccess(int pid, IntPtr handle, ulong baseAddr, uint size, bool isWow64, ILogger log)
    {
        ProcessId = pid;
        _handle = handle;
        ModuleBase = baseAddr;
        ModuleSize = size;
        IsWow64 = isWow64;
        _log = log;
    }

    /// <summary>
    /// Iterate the supplied <paramref name="profiles"/>, find the first one whose process
    /// is currently running, open it read-only, and return both the wrapper and the matched
    /// profile. Returns null when none of the games are running.
    /// </summary>
    public static ProcessAccess? TryOpenAnyKnown(
        IEnumerable<GameProfile> profiles,
        out GameProfile? matched,
        ILogger log)
    {
        matched = null;

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.ProcessName)) continue;

            Process[] candidates = Process.GetProcessesByName(profile.ProcessName);
            try
            {
                foreach (var p in candidates)
                {
                    IntPtr h = NativeMethods.OpenProcess(
                        NativeMethods.READ_ONLY_PROCESS_ACCESS, false, p.Id);

                    if (h == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        log.Log($"OpenProcess pid={p.Id} ({profile.ProcessName}) failed: Win32 {err}");
                        continue;
                    }

                    bool isWow64 = false;
                    NativeMethods.IsWow64Process(h, out isWow64);

                    uint filter = isWow64
                        ? NativeMethods.LIST_MODULES_32BIT
                        : NativeMethods.LIST_MODULES_64BIT;

                    if (TryGetMainModule(h, profile.ModuleName, filter,
                            out ulong baseAddr, out uint size))
                    {
                        log.Log(
                            $"Opened {profile.DisplayName} pid={p.Id} " +
                            $"({(isWow64 ? "32" : "64")}-bit) " +
                            $"base=0x{baseAddr:X} size=0x{size:X}");
                        matched = profile;
                        return new ProcessAccess(p.Id, h, baseAddr, size, isWow64, log);
                    }

                    log.Log($"pid={p.Id}: module '{profile.ModuleName}' not found, closing.");
                    NativeMethods.CloseHandle(h);
                }
            }
            finally
            {
                foreach (var p in candidates) p.Dispose();
            }
        }

        return null;
    }

    private static bool TryGetMainModule(
        IntPtr hProc, string moduleName, uint filterFlag,
        out ulong baseAddr, out uint size)
    {
        baseAddr = 0;
        size = 0;

        if (!NativeMethods.EnumProcessModulesEx(
                hProc, Array.Empty<IntPtr>(), 0, out uint needed, filterFlag)
            || needed == 0)
        {
            return false;
        }

        int count = (int)(needed / (uint)IntPtr.Size);
        var modules = new IntPtr[count];

        if (!NativeMethods.EnumProcessModulesEx(
                hProc, modules, needed, out _, filterFlag))
        {
            return false;
        }

        var sb = new StringBuilder(260);
        foreach (var h in modules)
        {
            sb.Clear();
            uint n = NativeMethods.GetModuleBaseNameW(hProc, h, sb, (uint)sb.Capacity);
            if (n == 0) continue;

            string name = sb.ToString();
            if (!string.Equals(name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!NativeMethods.GetModuleInformation(
                    hProc, h, out NativeMethods.MODULEINFO info, (uint)Marshal.SizeOf<NativeMethods.MODULEINFO>()))
            {
                return false;
            }

            baseAddr = (ulong)info.lpBaseOfDll.ToInt64();
            size = info.SizeOfImage;
            return true;
        }

        return false;
    }

    // --- read primitives ---------------------------------------------------------------------

    /// <summary>Read raw bytes into <paramref name="buffer"/>; returns number of bytes read.</summary>
    public int ReadBytes(ulong remoteAddress, byte[] buffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessAccess));
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));

        bool ok = NativeMethods.ReadProcessMemory(
            _handle,
            new IntPtr(unchecked((long)remoteAddress)),
            buffer,
            new IntPtr(buffer.Length),
            out IntPtr read);
        return ok ? read.ToInt32() : 0;
    }

    public bool TryReadInt32(ulong address, out int value)
    {
        var arr = new byte[4];
        int n = ReadBytes(address, arr);
        if (n != 4)
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToInt32(arr, 0);
        return true;
    }

    public bool TryReadUInt32(ulong address, out uint value)
    {
        var arr = new byte[4];
        int n = ReadBytes(address, arr);
        if (n != 4)
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToUInt32(arr, 0);
        return true;
    }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        var arr = new byte[8];
        int n = ReadBytes(address, arr);
        if (n != 8)
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToUInt64(arr, 0);
        return true;
    }

    /// <summary>
    /// Read a contiguous range of the game's module memory into successive byte buffers.
    /// Yields (remoteStart, buffer) pairs so the caller can search each chunk and translate
    /// any hit back to an absolute remote address.
    ///
    /// Pages that cannot be read (guard / no-access) are skipped silently; the buffer
    /// for that chunk is returned as an empty array.
    /// </summary>
    public IEnumerable<(ulong RemoteStart, byte[] Buffer)> EnumerateModuleChunks(
        int chunkSize = 1 << 20 /* 1 MiB */,
        int overlap = 64)
    {
        if (chunkSize <= overlap)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));

        ulong cursor = ModuleBase;
        while (cursor < ModuleEnd)
        {
            ulong remaining = ModuleEnd - cursor;
            int toRead = (int)Math.Min((ulong)chunkSize, remaining);
            var buf = new byte[toRead];
            int n = ReadBytes(cursor, buf);

            if (n > 0)
            {
                if (n < toRead) Array.Resize(ref buf, n);
                yield return (cursor, buf);
            }
            else
            {
                yield return (cursor, Array.Empty<byte>());
            }

            // Advance with overlap so a pattern straddling a chunk boundary is still found.
            ulong step = (ulong)Math.Max(1, toRead - overlap);
            cursor += step;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
