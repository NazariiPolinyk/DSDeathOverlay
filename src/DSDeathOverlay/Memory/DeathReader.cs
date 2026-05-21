using System;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// High-level death-count reader. Internally:
///
///   1. Pattern-scans DarkSoulsRemastered.exe for the static load of <c>ChrClassBase</c>.
///   2. Resolves the RIP-relative displacement to the absolute address of the static pointer.
///   3. Each read: dereferences that static to get the live <c>ChrClassBase</c> instance,
///      then reads the 4-byte int at instance + <see cref="DeathCountOffset"/>.
///
/// Sources for the pattern + offset:
///   * JohrnaJohrna/RemasterCETable -> DarkSoulsRemastered.CT, entries "TrueDeath" / "True Death Num" / "Death Num"
///   * The DSDeaths and DSDC reference projects use the same approach.
/// </summary>
public sealed class DeathReader
{
    /// <summary>
    /// Pattern matching the instruction <c>mov rax, [rip+disp32]</c> immediately followed by
    /// <c>test rax, rax</c> / branch / <c>addss xmm0, [rax+0xAC]</c>, which is a stable
    /// signature for the static load of ChrClassBase across DSR builds.
    /// </summary>
    public const string ChrClassBasePattern =
        "48 8B 05 ? ? ? ? 48 85 C0 ? ? F3 0F 58 80 AC 00 00 00";

    /// <summary>Offset from <c>[ChrClassBase]</c> to the in-game Death Num counter (4 bytes).</summary>
    public const int DeathCountOffset = 0x98;

    /// <summary>Offset to the cumulative "true" death counter (4 bytes, never resets).</summary>
    public const int TrueDeathOffset = 0x90;

    private readonly ProcessAccess _proc;
    private readonly ILogger _log;

    /// <summary>
    /// Address of the static pointer (a slot in DSR's .data/.bss section). Cached for the
    /// lifetime of the game process. Zero means "not yet resolved".
    /// </summary>
    public ulong ChrClassBasePtrAddress { get; private set; }

    public bool IsResolved => ChrClassBasePtrAddress != 0;

    public DeathReader(ProcessAccess proc, ILogger log)
    {
        _proc = proc ?? throw new ArgumentNullException(nameof(proc));
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Pattern-scan the game's module for the ChrClassBase load instruction and resolve
    /// the RIP-relative reference. Result is cached in <see cref="ChrClassBasePtrAddress"/>.
    /// Returns true if resolved; false otherwise (game in unexpected state, AOB changed by patch).
    /// </summary>
    public bool ResolveChrClassBase()
    {
        var (pattern, mask) = PatternScanner.ParsePattern(ChrClassBasePattern);

        foreach (var (chunkStart, buffer) in _proc.EnumerateModuleChunks())
        {
            if (buffer.Length == 0) continue;

            int offset = PatternScanner.Find(buffer, pattern, mask);
            if (offset < 0) continue;

            // Absolute remote address of the matched 'mov rax, [rip+disp32]' instruction.
            ulong hit = chunkStart + (ulong)offset;

            // Read the 4-byte displacement directly from the remote process to be safe
            // (the chunked buffer might end mid-instruction near an overlap boundary).
            if (!_proc.TryReadInt32(hit + 3, out int disp))
            {
                _log.Log($"Pattern found at 0x{hit:X} but disp32 read failed.");
                continue;
            }

            // RIP after this 7-byte instruction = hit + 7. Target = RIP + disp.
            ulong staticAddr = (ulong)((long)hit + disp + 7);
            ChrClassBasePtrAddress = staticAddr;

            _log.Log($"ChrClassBase static pointer at 0x{staticAddr:X} (pattern hit 0x{hit:X}, disp=0x{disp:X8}).");
            return true;
        }

        _log.Log("ChrClassBase pattern NOT found in DarkSoulsRemastered.exe. Game may not have finished loading, or AOB has changed.");
        return false;
    }

    /// <summary>
    /// Read the current in-game death count. Returns null when:
    /// - <see cref="ResolveChrClassBase"/> hasn't been called or failed, OR
    /// - The static pointer is currently null (player has not loaded a character / on title screen), OR
    /// - The read failed (game process closed mid-read).
    /// </summary>
    public int? TryReadDeathCount()
    {
        if (!IsResolved) return null;

        if (!_proc.TryReadUInt64(ChrClassBasePtrAddress, out ulong instance))
            return null;

        if (instance == 0) return null; // not in game yet

        if (!_proc.TryReadInt32(instance + (ulong)DeathCountOffset, out int deaths))
            return null;

        // Sanity check: a negative count almost certainly means we read garbage from a stale pointer.
        if (deaths < 0 || deaths > 1_000_000) return null;

        return deaths;
    }
}
