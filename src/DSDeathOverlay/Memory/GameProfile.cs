using System.Text.Json.Serialization;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Describes one supported game: how to find its process, and how to walk memory to
/// reach the death counter. A profile uses EITHER an AOB pattern + offset (resilient
/// to patches, like our DSR support) OR a hardcoded pointer chain (matches the DSDeaths
/// approach for DS2/DS3/Sekiro).
/// </summary>
public sealed class GameProfile
{
    /// <summary>Human-readable name shown in logs (e.g. "Dark Souls III").</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Short tag shown in the overlay UI (e.g. "DS3"). Kept compact so the overlay
    /// stays small.
    /// </summary>
    public string ShortTag { get; init; } = "";

    /// <summary>
    /// Process name as <see cref="System.Diagnostics.Process.GetProcessesByName(string)"/>
    /// expects it. NO ".exe" suffix.
    /// </summary>
    public string ProcessName { get; init; } = "";

    /// <summary>Main module name including extension (e.g. "DarkSoulsIII.exe").</summary>
    public string ModuleName { get; init; } = "";

    /// <summary>
    /// Optional AOB pattern (cheat-engine style, with '?' wildcards). When non-null,
    /// the reader scans the main module for the pattern, resolves it as a RIP-relative
    /// <c>mov rax, [rip+disp32]</c>, then reads a 4-byte int at the resulting pointer +
    /// <see cref="AobValueOffset"/>.
    /// </summary>
    public string? AobPattern { get; init; }

    /// <summary>Offset added to <c>[ChrClassBase]</c> after AOB resolution.</summary>
    public int? AobValueOffset { get; init; }

    /// <summary>
    /// Pointer chain for 32-bit (WoW64) variants of the game. Only DS2 (non-SotFS)
    /// has a 32-bit build in our supported set. Null for 64-bit-only titles.
    /// </summary>
    public int[]? ChainOffsets32 { get; init; }

    /// <summary>
    /// Pointer chain for 64-bit variants of the game. The walker exactly matches the
    /// DSDeaths semantics: <c>addr = moduleBase; foreach off: addr += off; if not last:
    /// addr = deref; result = (int)addr</c>.
    /// </summary>
    public int[]? ChainOffsets64 { get; init; }

    [JsonIgnore]
    public bool UsesAob => AobPattern is not null && AobValueOffset is not null;

    [JsonIgnore]
    public bool UsesPointerChain => ChainOffsets32 is not null || ChainOffsets64 is not null;
}

/// <summary>Container matching the on-disk JSON layout.</summary>
public sealed class GameProfileSet
{
    public GameProfile[] Games { get; init; } = System.Array.Empty<GameProfile>();
}
