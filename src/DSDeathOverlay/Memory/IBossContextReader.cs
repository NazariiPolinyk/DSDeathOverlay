namespace DSDeathOverlay.Memory;

/// <summary>
/// Abstraction for "which boss, if any, is the player currently fighting".
///
/// Two implementations are shipped:
///
///   * <see cref="ManualBossContextReader"/> — the active boss is whatever the
///     user last picked via hotkey or click. Always works.
///   * <see cref="PointerChainBossContextReader"/> — walks a configured pointer
///     chain and maps the resulting integer to a boss id via the
///     <see cref="BossDetection.FlagToBossId"/> table. Patch-fragile but fully
///     automatic when offsets are available.
///
/// Implementations must be safe to call from the polling thread.
/// </summary>
public interface IBossContextReader
{
    /// <summary>
    /// Stable id of the currently-active boss (must match a
    /// <see cref="BossEntry.Id"/> in the loaded catalog), or null when no boss
    /// is active.
    /// </summary>
    string? ActiveBossId { get; }

    /// <summary>Re-read the active boss from the underlying source.</summary>
    void Refresh();
}
