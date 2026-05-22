namespace DSDeathOverlay.Memory;

/// <summary>
/// Common surface for the two ways we extract a death count from a game's address space:
///
///   * <see cref="AobDeathReader"/> — pattern scan + RIP-relative resolve + final offset
///     (used for DSR; resilient to patches that move the static around).
///   * <see cref="PointerChainDeathReader"/> — fixed module-relative pointer chain walked
///     in the style of the DSDeaths project (used for DS2 / DS3 / Sekiro).
///
/// Both readers are constructed once per game session and cached. They handle the
/// "not yet initialised" / "title screen / pointer is null" cases by returning
/// <c>null</c> from <see cref="TryReadDeathCount"/>.
/// </summary>
public interface IDeathReader
{
    /// <summary>
    /// Whether the reader has performed any one-time setup needed before
    /// <see cref="TryReadDeathCount"/> can succeed (e.g. an AOB scan).
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Perform any one-time setup. Safe to call repeatedly; will short-circuit when
    /// already ready. Returns true on success.
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Read the current death count. Returns null when not ready, when the game is
    /// on the title screen, or when the read failed (process gone, etc.).
    /// </summary>
    int? TryReadDeathCount();
}
