using System;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Pointer-chain death reader (used for DS2 / DS3 / Sekiro).
///
/// Walks a hardcoded list of module-relative offsets in the exact style of the
/// DSDeaths reference implementation:
///
/// <code>
/// addr = moduleBase;
/// foreach (int off in offsets) {
///     if (addr == 0) return null;     // null pointer encountered (game on title screen)
///     addr += off;
///     addr = deref(addr);             // reads 8 bytes (x64) or 4 bytes (x86)
/// }
/// deaths = (int)addr;                 // lower 32 bits of the final deref ARE the count
/// </code>
///
/// The "final iteration also derefs" wrinkle matches DSDeaths exactly: the death-count
/// value sits at <c>prevAddr + lastOffset</c> as a 4-byte int, so reading 8 bytes there
/// gives us the value in the low dword.
/// </summary>
public sealed class PointerChainDeathReader : IDeathReader
{
    private readonly IMemoryReader _reader;
    private readonly GameProfile _profile;
    private readonly ILogger _log;
    private readonly int[] _offsets;
    private bool _initialized;

    public bool IsReady => _initialized;

    public PointerChainDeathReader(IMemoryReader reader, GameProfile profile, ILogger log)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _log = log ?? NullLogger.Instance;

        // Pick the right chain for the target process's bitness.
        int[]? chain = _reader.IsWow64 ? profile.ChainOffsets32 : profile.ChainOffsets64;

        if (chain is null || chain.Length == 0)
        {
            throw new ArgumentException(
                $"Profile '{profile.DisplayName}' has no pointer chain for " +
                $"{(_reader.IsWow64 ? "32" : "64")}-bit.", nameof(profile));
        }

        _offsets = chain;
    }

    public bool Initialize()
    {
        // A pointer chain has no one-time setup — just mark ready so the poller
        // proceeds to TryReadDeathCount.
        _initialized = true;
        _log.Log(
            $"[{_profile.ShortTag}] pointer chain ready " +
            $"({(_reader.IsWow64 ? "32" : "64")}-bit, {_offsets.Length} hops).");
        return true;
    }

    public int? TryReadDeathCount()
    {
        if (!_initialized) return null;
        if (!TryWalk(_reader, _offsets, out int value)) return null;

        if (value < 0 || value > 1_000_000) return null;
        return value;
    }

    /// <summary>
    /// Pure walker exposed for unit tests. Walks the chain against any
    /// <see cref="IMemoryReader"/> and produces the final 4-byte value.
    /// </summary>
    public static bool TryWalk(IMemoryReader reader, int[] offsets, out int value)
    {
        value = 0;
        if (offsets is null || offsets.Length == 0) return false;

        ulong addr = reader.ModuleBase;

        foreach (int off in offsets)
        {
            if (addr == 0) return false;

            // Two's-complement add — negative offsets are legal in CE-style chains.
            addr = unchecked(addr + (ulong)(long)off);

            if (reader.IsWow64)
            {
                if (!reader.TryReadUInt32(addr, out uint v32)) return false;
                addr = v32;
            }
            else
            {
                if (!reader.TryReadUInt64(addr, out ulong v64)) return false;
                addr = v64;
            }
        }

        value = unchecked((int)addr);
        return true;
    }
}
