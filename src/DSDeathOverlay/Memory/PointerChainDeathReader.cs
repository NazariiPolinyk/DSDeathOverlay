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
    private bool _loggedFirstRead;
    private long _lastFailLogTicks;

    /// <summary>Min interval between repeated chain-failure log lines.</summary>
    private static readonly TimeSpan FailLogInterval = TimeSpan.FromSeconds(5);

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

        if (!TryWalkDiagnostic(_reader, _offsets, out int value, out ulong endpoint, out string? failure))
        {
            LogThrottled($"[{_profile.ShortTag}] chain read failed: {failure}");
            return null;
        }

        // A real death count is never negative. A negative result means the chain
        // walked through readable memory but landed somewhere whose low 32 bits
        // have the high bit set — almost always a pointer-aligned field, i.e. the
        // chain is stale for this game build. Surface it loudly (throttled) so the
        // user can copy fresh offsets from DSDeaths into games.json next to the
        // .exe; pre-fix this path was silent and looked indistinguishable from
        // "no save loaded".
        if (value < 0)
        {
            LogThrottled(
                $"[{_profile.ShortTag}] chain produced negative value {value} " +
                $"(endpoint 0x{endpoint:X}); rejecting as garbage. " +
                $"Chain may be stale — check DSDeaths master for newer offsets.");
            return null;
        }

        if (!_loggedFirstRead)
        {
            _loggedFirstRead = true;
            _log.Log($"[{_profile.ShortTag}] first read: {value} (chain endpoint = 0x{endpoint:X}).");
        }
        return value;
    }

    /// <summary>
    /// Emit <paramref name="message"/> at most once per <see cref="FailLogInterval"/>.
    /// Shared between the chain-failure and negative-value paths so a busted chain
    /// can't double up its log spam at full poll rate.
    /// </summary>
    private void LogThrottled(string message)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastFailLogTicks > FailLogInterval.Ticks)
        {
            _lastFailLogTicks = now;
            _log.Log(message);
        }
    }

    /// <summary>
    /// Pure walker exposed for unit tests. Walks the chain against any
    /// <see cref="IMemoryReader"/> and produces the final 4-byte value.
    /// </summary>
    public static bool TryWalk(IMemoryReader reader, int[] offsets, out int value)
        => TryWalkDiagnostic(reader, offsets, out value, out _, out _);

    /// <summary>
    /// Same walk as <see cref="TryWalk"/>, but additionally reports the address that
    /// produced the final read and a human-readable failure reason when the walk
    /// aborts. Used by <see cref="TryReadDeathCount"/> to surface useful diagnostics
    /// in <c>deaths.log</c> without changing the public test surface.
    /// </summary>
    public static bool TryWalkDiagnostic(
        IMemoryReader reader,
        int[] offsets,
        out int value,
        out ulong endpoint,
        out string? failure)
    {
        value = 0;
        endpoint = 0;
        failure = null;

        if (offsets is null || offsets.Length == 0)
        {
            failure = "empty offset chain";
            return false;
        }

        ulong addr = reader.ModuleBase;

        for (int i = 0; i < offsets.Length; i++)
        {
            int off = offsets[i];

            if (addr == 0)
            {
                failure = $"hop {i}: previous deref was null (game on title screen or struct not allocated)";
                return false;
            }

            // Two's-complement add — negative offsets are legal in CE-style chains.
            ulong target = unchecked(addr + (ulong)(long)off);

            if (reader.IsWow64)
            {
                if (!reader.TryReadUInt32(target, out uint v32))
                {
                    failure = $"hop {i}: TryReadUInt32(0x{target:X}) failed (offset 0x{off:X})";
                    return false;
                }
                addr = v32;
            }
            else
            {
                if (!reader.TryReadUInt64(target, out ulong v64))
                {
                    failure = $"hop {i}: TryReadUInt64(0x{target:X}) failed (offset 0x{off:X})";
                    return false;
                }
                addr = v64;
            }

            // Remember the address we just read from. After the last iteration this
            // is the memory location holding the 4-byte death count.
            endpoint = target;
        }

        value = unchecked((int)addr);
        return true;
    }
}
