using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DSDeathOverlay.Memory;
using DSDeathOverlay.Services;

namespace DSDeathOverlay;

/// <summary>
/// View-model bound to the overlay window. Exposes a compact
/// <see cref="DisplayText"/> for the always-on row and a <see cref="BossRows"/>
/// collection for the expandable per-boss list.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private int? _deathCount;
    private PollerStatus _status = PollerStatus.WaitingForGame;
    private GameProfile? _game;
    private string? _activeBossName;
    private int _activeBossCount;
    private bool _isBossListExpanded;

    public int? DeathCount
    {
        get => _deathCount;
        set
        {
            if (_deathCount == value) return;
            _deathCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public PollerStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public GameProfile? Game
    {
        get => _game;
        set
        {
            if (ReferenceEquals(_game, value)) return;
            _game = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>
    /// Display name of the currently-active boss, or null when none. Shown on
    /// the compact line as " | Iudex Gundyr: 3".
    /// </summary>
    public string? ActiveBossName
    {
        get => _activeBossName;
        set
        {
            if (_activeBossName == value) return;
            _activeBossName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public int ActiveBossCount
    {
        get => _activeBossCount;
        set
        {
            if (_activeBossCount == value) return;
            _activeBossCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>Toggled by F10; controls visibility of the boss list panel.</summary>
    public bool IsBossListExpanded
    {
        get => _isBossListExpanded;
        set
        {
            if (_isBossListExpanded == value) return;
            _isBossListExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>One row per boss in the active game's catalog.</summary>
    public ObservableCollection<BossCountRow> BossRows { get; } = new();

    /// <summary>
    /// What the overlay actually shows on the always-visible line. We deliberately
    /// surface a friendly status string when the count is unavailable so the user
    /// knows the tool is alive even before they load a save.
    /// </summary>
    public string DisplayText
    {
        get
        {
            string tag = _game?.ShortTag is { Length: > 0 } t ? $"{t} - " : "";
            string main = _status switch
            {
                PollerStatus.WaitingForGame      => "Deaths: --   (waiting for game)",
                PollerStatus.ResolvingPattern    => $"{tag}Deaths: --   (locating)",
                PollerStatus.WaitingForCharacter => $"{tag}Deaths: --   (load a save)",
                PollerStatus.Reading             => $"{tag}Deaths: {(_deathCount ?? 0):N0}",
                _                                => "Deaths: --",
            };

            return _activeBossName is { Length: > 0 } name && _status == PollerStatus.Reading
                ? $"{main} | {name}: {_activeBossCount:N0}"
                : main;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void ApplyUpdate(DeathCountEventArgs e)
    {
        // Set status + game first so DisplayText reflects the combined state when
        // the count change notification fires.
        Status = e.Status;
        Game = e.Game;
        DeathCount = e.DeathCount;
    }

    /// <summary>
    /// Replace <see cref="BossRows"/> and <see cref="ActiveBossName"/> with the
    /// values from a tracker snapshot. Must be called on the UI thread.
    /// </summary>
    public void ApplyBossSnapshot(BossSnapshot snapshot)
    {
        BossRows.Clear();
        foreach (var row in snapshot.Rows)
            BossRows.Add(row);

        ActiveBossName = snapshot.ActiveBossName;
        ActiveBossCount = snapshot.ActiveBossCount;
    }
}
