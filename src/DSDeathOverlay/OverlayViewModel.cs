using System.ComponentModel;
using System.Runtime.CompilerServices;
using DSDeathOverlay.Memory;
using DSDeathOverlay.Services;

namespace DSDeathOverlay;

/// <summary>
/// View-model bound to the overlay window. Exposes a single <see cref="DisplayText"/>
/// string so the XAML stays trivial; the string changes based on poller status and
/// the currently-active game.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private int? _deathCount;
    private PollerStatus _status = PollerStatus.WaitingForGame;
    private GameProfile? _game;

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
    /// What the overlay actually shows. We deliberately surface a friendly
    /// status string when the count is unavailable so the user knows the tool is
    /// alive even before they load a save.
    /// </summary>
    public string DisplayText
    {
        get
        {
            string tag = _game?.ShortTag is { Length: > 0 } t ? $"{t} - " : "";

            return _status switch
            {
                PollerStatus.WaitingForGame      => "Deaths: --   (waiting for game)",
                PollerStatus.ResolvingPattern    => $"{tag}Deaths: --   (locating)",
                PollerStatus.WaitingForCharacter => $"{tag}Deaths: --   (load a save)",
                PollerStatus.Reading             => $"{tag}Deaths: {(_deathCount ?? 0):N0}",
                _                                => "Deaths: --",
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void ApplyUpdate(DeathCountEventArgs e)
    {
        // Set status + game first so DisplayText reflects the combined state when the
        // count change notification fires.
        Status = e.Status;
        Game = e.Game;
        DeathCount = e.DeathCount;
    }
}
