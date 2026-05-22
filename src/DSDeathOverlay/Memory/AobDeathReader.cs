using System;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// AOB-pattern-based death reader (used for Dark Souls: Remastered).
///
///   1. Pattern-scans the game's main module for a <c>mov rax, [rip+disp32]</c>
///      load of the player class static.
///   2. Resolves the RIP-relative displacement to the absolute address of the
///      static pointer slot.
///   3. Each read: dereferences that static, then reads a 4-byte int at
///      <c>[static] + <see cref="GameProfile.AobValueOffset"/></c>.
///
/// More resilient to game patches than a hardcoded offset, because the AOB will
/// keep matching even if the static moves within the binary.
/// </summary>
public sealed class AobDeathReader : IDeathReader
{
    private readonly ProcessAccess _proc;
    private readonly GameProfile _profile;
    private readonly ILogger _log;

    /// <summary>
    /// Absolute remote address of the static pointer slot. Zero until
    /// <see cref="Initialize"/> succeeds.
    /// </summary>
    public ulong ChrClassBasePtrAddress { get; private set; }

    public bool IsReady => ChrClassBasePtrAddress != 0;

    public AobDeathReader(ProcessAccess proc, GameProfile profile, ILogger log)
    {
        _proc = proc ?? throw new ArgumentNullException(nameof(proc));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (!profile.UsesAob)
            throw new ArgumentException(
                "Profile must have AobPattern and AobValueOffset set.", nameof(profile));
        _log = log ?? NullLogger.Instance;
    }

    public bool Initialize()
    {
        if (IsReady) return true;

        var (pattern, mask) = PatternScanner.ParsePattern(_profile.AobPattern!);

        foreach (var (chunkStart, buffer) in _proc.EnumerateModuleChunks())
        {
            if (buffer.Length == 0) continue;

            int offset = PatternScanner.Find(buffer, pattern, mask);
            if (offset < 0) continue;

            ulong hit = chunkStart + (ulong)offset;

            // Read the displacement directly from the remote (the chunked buffer might
            // end mid-instruction at an overlap boundary).
            if (!_proc.TryReadInt32(hit + 3, out int disp))
            {
                _log.Log($"Pattern found at 0x{hit:X} but disp32 read failed.");
                continue;
            }

            // mov rax, [rip+disp32] is 7 bytes. RIP after = hit + 7. Target = RIP + disp.
            ulong staticAddr = (ulong)((long)hit + disp + 7);
            ChrClassBasePtrAddress = staticAddr;

            _log.Log(
                $"[{_profile.ShortTag}] static pointer at 0x{staticAddr:X} " +
                $"(pattern hit 0x{hit:X}, disp=0x{disp:X8}).");
            return true;
        }

        _log.Log(
            $"[{_profile.ShortTag}] AOB pattern NOT found in {_profile.ModuleName}. " +
            $"Game may still be loading, or AOB has changed.");
        return false;
    }

    public int? TryReadDeathCount()
    {
        if (!IsReady) return null;

        if (!_proc.TryReadUInt64(ChrClassBasePtrAddress, out ulong instance))
            return null;

        if (instance == 0) return null; // title screen / no character

        if (!_proc.TryReadInt32(instance + (ulong)_profile.AobValueOffset!.Value, out int deaths))
            return null;

        if (deaths < 0 || deaths > 1_000_000) return null;

        return deaths;
    }
}
