using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
/// DS2 SotFS can optionally use a SrShadowy-style final hop that reads a 4-byte int at
/// the last offset without a final pointer dereference.
/// </summary>
public sealed class PointerChainDeathReader : IDeathReader
{
    private readonly record struct ChainCandidate(int[] Offsets, bool FinalHopInt32, string Label);

    private readonly IMemoryReader _reader;
    private readonly GameProfile _profile;
    private readonly ILogger _log;
    private readonly ChainCandidate[] _candidates;
    private ChainCandidate? _activeCandidate;
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
        _candidates = BuildCandidates(profile, _reader.IsWow64);
    }

    private static ChainCandidate[] BuildCandidates(GameProfile profile, bool isWow64)
    {
        var list = new List<ChainCandidate>();

        int[]? primary = isWow64 ? profile.ChainOffsets32 : profile.ChainOffsets64;
        if (primary is { Length: > 0 })
        {
            list.Add(new ChainCandidate(primary, profile.ChainFinalHopInt32, "primary"));
        }

        if (!isWow64 && profile.ChainVariants64 is not null)
        {
            foreach (var variant in profile.ChainVariants64)
            {
                if (variant.Offsets is { Length: > 0 })
                {
                    list.Add(new ChainCandidate(
                        variant.Offsets,
                        variant.FinalHopInt32,
                        variant.Label ?? "alternate"));
                }
            }
        }

        if (list.Count == 0)
        {
            throw new ArgumentException(
                $"Profile '{profile.DisplayName}' has no pointer chain for " +
                $"{(isWow64 ? "32" : "64")}-bit.", nameof(profile));
        }

        return list.ToArray();
    }

    public bool Initialize()
    {
        _initialized = true;
        var sb = new StringBuilder();
        for (int i = 0; i < _candidates.Length; i++)
        {
            if (i > 0) sb.Append("; ");
            sb.Append(FormatChain(_candidates[i]));
        }

        _log.Log(
            $"[{_profile.ShortTag}] pointer chain ready " +
            $"({(_reader.IsWow64 ? "32" : "64")}-bit, module=0x{_reader.ModuleBase:X}): {sb}");
        return true;
    }

    public int? TryReadDeathCount()
    {
        if (!_initialized) return null;

        // Once a chain works, stick with it for the session to avoid flip-flopping.
        if (_activeCandidate is { } pinned)
        {
            int? pinnedValue = TryReadWithCandidate(pinned);
            if (pinnedValue is not null) return pinnedValue;
            _activeCandidate = null;
        }

        foreach (var candidate in _candidates)
        {
            int? value = TryReadWithCandidate(candidate);
            if (value is null) continue;

            if (_activeCandidate is null && !string.Equals(candidate.Label, "primary", StringComparison.Ordinal))
            {
                _log.Log($"[{_profile.ShortTag}] using alternate chain ({candidate.Label}).");
            }

            _activeCandidate = candidate;
            return value;
        }

        return null;
    }

    private int? TryReadWithCandidate(ChainCandidate candidate)
    {
        if (!TryWalkDiagnostic(
                _reader,
                candidate.Offsets,
                candidate.FinalHopInt32,
                out int value,
                out ulong endpoint,
                out string? failure))
        {
            LogThrottled(
                $"[{_profile.ShortTag}] chain read failed ({candidate.Label}): {failure}" +
                MaybeStaticSlotHint(_reader, candidate.Offsets, failure));
            return null;
        }

        if (value < 0)
        {
            LogThrottled(
                $"[{_profile.ShortTag}] chain produced negative value {value} " +
                $"({candidate.Label}, endpoint 0x{endpoint:X}); rejecting as garbage. " +
                $"Chain may be stale — check DSDeaths master or SrShadowy DSII-SOTFS-DIE-COUNT.");
            return null;
        }

        if (!_loggedFirstRead)
        {
            _loggedFirstRead = true;
            _log.Log(
                $"[{_profile.ShortTag}] first read: {value} ({candidate.Label}, " +
                $"endpoint = 0x{endpoint:X}).");
        }

        return value;
    }

    /// <summary>
    /// If the walk died on hop 0/1 with a null deref, include the qword at the static
    /// slot so stale-base debugging does not require Cheat Engine.
    /// </summary>
    private static string MaybeStaticSlotHint(IMemoryReader reader, int[] offsets, string? failure)
    {
        if (failure is null || offsets.Length == 0) return "";
        if (!failure.Contains("hop 1", StringComparison.Ordinal) &&
            !failure.Contains("hop 0", StringComparison.Ordinal))
        {
            return "";
        }

        ulong slot = unchecked(reader.ModuleBase + (ulong)(long)offsets[0]);
        if (!reader.TryReadUInt64(slot, out ulong qword))
            return $" Static slot 0x{slot:X}: unreadable.";

        return $" Static slot 0x{slot:X} (= module+0x{offsets[0]:X}) holds 0x{qword:X}.";
    }

    private static string FormatChain(ChainCandidate c)
    {
        string hops = string.Join(", ", c.Offsets.Select(o => $"0x{o:X}"));
        return c.FinalHopInt32
            ? $"{c.Label} [{hops}] (final int32)"
            : $"{c.Label} [{hops}]";
    }

    private void LogThrottled(string message)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastFailLogTicks > FailLogInterval.Ticks)
        {
            _lastFailLogTicks = now;
            _log.Log(message);
        }
    }

    public static bool TryWalk(IMemoryReader reader, int[] offsets, out int value)
        => TryWalkDiagnostic(reader, offsets, finalHopInt32: false, out value, out _, out _);

    public static bool TryWalkDiagnostic(
        IMemoryReader reader,
        int[] offsets,
        out int value,
        out ulong endpoint,
        out string? failure)
        => TryWalkDiagnostic(reader, offsets, finalHopInt32: false, out value, out endpoint, out failure);

    public static bool TryWalkDiagnostic(
        IMemoryReader reader,
        int[] offsets,
        bool finalHopInt32,
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
            bool isLast = i == offsets.Length - 1;

            if (addr == 0)
            {
                failure = $"hop {i}: previous deref was null (game on title screen or struct not allocated)";
                return false;
            }

            ulong target = unchecked(addr + (ulong)(long)off);

            if (isLast && finalHopInt32)
            {
                if (!reader.TryReadInt32(target, out int v32))
                {
                    failure = $"hop {i}: TryReadInt32(0x{target:X}) failed (offset 0x{off:X})";
                    return false;
                }

                endpoint = target;
                value = v32;
                return true;
            }

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

            endpoint = target;
        }

        value = unchecked((int)addr);
        return true;
    }
}
