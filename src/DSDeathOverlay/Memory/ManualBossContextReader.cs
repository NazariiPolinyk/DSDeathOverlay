namespace DSDeathOverlay.Memory;

/// <summary>
/// "Active boss" source that is set explicitly by the UI (hotkey or edit-mode
/// click), not read from game memory. This is the default for every game so
/// per-boss tracking works without any reverse-engineered offsets.
/// </summary>
public sealed class ManualBossContextReader : IBossContextReader
{
    private string? _activeBossId;

    public string? ActiveBossId => _activeBossId;

    /// <summary>No-op: manual readers never need to poll game memory.</summary>
    public void Refresh() { }

    /// <summary>
    /// Set (or clear) the active boss. Pass null to mark "no boss in progress".
    /// </summary>
    public void SetActiveBossId(string? bossId)
    {
        _activeBossId = string.IsNullOrWhiteSpace(bossId) ? null : bossId;
    }
}
