namespace DSDeathOverlay.Memory;

/// <summary>
/// Read-only view onto a remote process's address space, plus a known module range.
///
/// Introduced so the pointer-chain walker can be unit-tested against an in-memory
/// fake without needing a real game process.
/// </summary>
public interface IMemoryReader
{
    /// <summary>Base address of the matched game module inside the remote process.</summary>
    ulong ModuleBase { get; }

    /// <summary>Size of the matched game module image in bytes.</summary>
    uint ModuleSize { get; }

    /// <summary>True if the target process is 32-bit (WoW64), false for native x64.</summary>
    bool IsWow64 { get; }

    /// <summary>
    /// Read <paramref name="buffer"/>.Length bytes at <paramref name="remoteAddress"/>.
    /// Returns the number of bytes actually read (0 on failure).
    /// </summary>
    int ReadBytes(ulong remoteAddress, byte[] buffer);

    bool TryReadInt32(ulong address, out int value);

    bool TryReadUInt32(ulong address, out uint value);

    bool TryReadUInt64(ulong address, out ulong value);
}
