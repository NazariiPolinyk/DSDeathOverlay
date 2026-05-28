using System.Text.Json.Serialization;

namespace DSDeathOverlay.Memory;

/// <summary>
/// One boss entry inside a game's catalog. <see cref="Id"/> is the stable
/// key used for persistence (lower-case, dash-separated). <see cref="Name"/>
/// is the human-readable string shown in the overlay.
/// </summary>
public sealed class BossEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

/// <summary>
/// How (and whether) the overlay can detect which boss the player is currently
/// fighting. The framework supports two strategies today:
///
///   * <c>"manual"</c> — the user picks the active boss via hotkey (F11) or by
///     clicking a row in the expanded list while in edit mode. Works on every
///     game out of the box and never depends on patch-specific offsets.
///   * <c>"pointerChainFlag"</c> — the overlay walks <see cref="ChainOffsets64"/>
///     (or <see cref="ChainOffsets32"/> for WoW64 builds) and reads an integer
///     boss ID at the end. The value is matched against
///     <see cref="BossEntry.Id"/> via the <see cref="FlagToBossId"/> map.
///     None of the shipped games use this today; it is a hook for future
///     community-supplied offsets without touching the C# code.
/// </summary>
public sealed class BossDetection
{
    public string Type { get; init; } = "manual";

    public int[]? ChainOffsets64 { get; init; }

    public int[]? ChainOffsets32 { get; init; }

    /// <summary>
    /// Mapping from the integer value read at the end of the chain to a boss
    /// <see cref="BossEntry.Id"/>. The reserved key <c>"0"</c> means "no active
    /// boss".
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string>? FlagToBossId { get; init; }
}

/// <summary>
/// Per-game boss catalog. <see cref="GameId"/> must match the
/// <see cref="GameProfile.ShortTag"/> of the corresponding entry in
/// <c>games.json</c> (e.g. <c>"DS3"</c>).
/// </summary>
public sealed class BossGameCatalog
{
    public string GameId { get; init; } = "";

    public BossDetection? Detection { get; init; }

    public BossEntry[] Bosses { get; init; } = System.Array.Empty<BossEntry>();
}

/// <summary>Container matching the on-disk <c>bosses.json</c> layout.</summary>
public sealed class BossCatalogSet
{
    public BossGameCatalog[] Games { get; init; } = System.Array.Empty<BossGameCatalog>();

    [JsonIgnore]
    public BossGameCatalog? this[string gameId]
    {
        get
        {
            foreach (var g in Games)
                if (string.Equals(g.GameId, gameId, System.StringComparison.OrdinalIgnoreCase))
                    return g;
            return null;
        }
    }
}
