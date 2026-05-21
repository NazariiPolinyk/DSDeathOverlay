using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DSDeathOverlay.Services;

namespace DSDeathOverlay;

/// <summary>
/// View-model bound to the overlay window. Exposes a single <see cref="DisplayText"/>
/// string so the XAML stays trivial; the string changes based on poller status.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private int? _deathCount;
    private PollerStatus _status = PollerStatus.WaitingForGame;

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

    /// <summary>
    /// What the overlay actually shows. We deliberately surface a friendly
    /// status string when the count is unavailable so the user knows the tool is
    /// alive even before they load a save.
    /// </summary>
    public string DisplayText => _status switch
    {
        PollerStatus.WaitingForGame      => "Deaths: --   (waiting for DSR)",
        PollerStatus.ResolvingPattern    => "Deaths: --   (locating)",
        PollerStatus.WaitingForCharacter => "Deaths: --   (load a save)",
        PollerStatus.Reading             => $"Deaths: {(_deathCount ?? 0):N0}",
        _                                => "Deaths: --",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void ApplyUpdate(DeathCountEventArgs e)
    {
        // Set status first so DisplayText reflects the combined state in one notification.
        Status = e.Status;
        DeathCount = e.DeathCount;
    }
}
