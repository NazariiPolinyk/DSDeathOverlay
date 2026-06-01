using System;
using System.Threading;
using System.Threading.Tasks;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Services;

/// <summary>State surfaced to the UI alongside the death count.</summary>
public enum PollerStatus
{
    /// <summary>We are looking for one of the supported game processes.</summary>
    WaitingForGame,
    /// <summary>Game found; the reader is performing one-time setup (AOB scan).</summary>
    ResolvingPattern,
    /// <summary>Reader ready, but the pointer is null (title screen / no character loaded).</summary>
    WaitingForCharacter,
    /// <summary>Reading the death count successfully.</summary>
    Reading,
}

public sealed class DeathCountEventArgs : EventArgs
{
    public int? DeathCount { get; }
    public PollerStatus Status { get; }
    /// <summary>The active game profile (null when <see cref="Status"/> is WaitingForGame).</summary>
    public GameProfile? Game { get; }

    public DeathCountEventArgs(int? deathCount, PollerStatus status, GameProfile? game)
    {
        DeathCount = deathCount;
        Status = status;
        Game = game;
    }
}

/// <summary>
/// Background service that owns the lifecycle of the connection to any supported From game:
///
///   * Polls for known game processes every ~1s when not connected.
///   * Once connected, builds the appropriate <see cref="IDeathReader"/> (AOB for DSR,
///     pointer chain for DS2/DS3/Sekiro) and runs its one-time initialisation.
///   * Reads the death count every <see cref="_pollInterval"/>.
///   * Raises <see cref="Updated"/> on every state change.
///
/// Recovers automatically if the game is closed and reopened (possibly even a different
/// game next time — e.g. you close DS3 and start DSR).
/// </summary>
public sealed class DeathPoller : IDisposable
{
    private readonly ILogger _log;
    private readonly GameProfileSet _profiles;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _reconnectInterval;

    private readonly CancellationTokenSource _cts = new();
    private Task? _runner;

    private ProcessAccess? _proc;
    private IDeathReader? _reader;
    private GameProfile? _activeProfile;

    private int? _lastCount;
    private PollerStatus _lastStatus = (PollerStatus)(-1); // force initial change event
    private GameProfile? _lastGame;
    private int _lastDroppedPid;
    private DateTime _lastDropUtc = DateTime.MinValue;

    private static readonly TimeSpan ReconnectCooldown = TimeSpan.FromSeconds(2);

    /// <summary>Raised on every state change (death count, status, or active game).</summary>
    public event EventHandler<DeathCountEventArgs>? Updated;

    public DeathPoller(
        ILogger log,
        GameProfileSet profiles,
        TimeSpan? pollInterval = null,
        TimeSpan? reconnectInterval = null)
    {
        _log = log ?? NullLogger.Instance;
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _reconnectInterval = reconnectInterval ?? TimeSpan.FromSeconds(1);
    }

    public void Start()
    {
        if (_runner is not null) return;
        _runner = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_proc is null)
                {
                    Emit(null, PollerStatus.WaitingForGame, null);
                    if (DateTime.UtcNow - _lastDropUtc < ReconnectCooldown)
                    {
                        await Delay(ReconnectCooldown, ct).ConfigureAwait(false);
                        continue;
                    }

                    _proc = ProcessAccess.TryOpenAnyKnown(_profiles.Games, out _activeProfile, _log);
                    if (_proc is null || _activeProfile is null)
                    {
                        await Delay(_reconnectInterval, ct).ConfigureAwait(false);
                        continue;
                    }
                    _reader = BuildReader(_proc, _activeProfile);
                }

                if (_reader is { IsReady: false })
                {
                    Emit(null, PollerStatus.ResolvingPattern, _activeProfile);
                    if (!_reader.Initialize())
                    {
                        await Delay(_reconnectInterval, ct).ConfigureAwait(false);
                        if (!IsProcessAlive(_proc!.ProcessId))
                            DropConnection("game process exited during reader init");
                        continue;
                    }
                }

                int? value = _reader!.TryReadDeathCount();
                if (value is null)
                {
                    if (!IsProcessAlive(_proc!.ProcessId))
                    {
                        DropConnection("game process exited");
                        continue;
                    }
                    Emit(null, PollerStatus.WaitingForCharacter, _activeProfile);
                }
                else
                {
                    Emit(value, PollerStatus.Reading, _activeProfile);
                }

                await Delay(_pollInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.Log($"Poller crashed: {ex}");
        }
        finally
        {
            DropConnection("shutdown");
        }
    }

    private IDeathReader BuildReader(ProcessAccess proc, GameProfile profile)
    {
        if (profile.UsesAob)
            return new AobDeathReader(proc, profile, _log);
        if (profile.UsesPointerChain)
            return new PointerChainDeathReader(proc, profile, _log);

        throw new InvalidOperationException(
            $"Profile '{profile.DisplayName}' has neither an AOB pattern nor a pointer chain.");
    }

    private void DropConnection(string reason)
    {
        _log.Log($"Dropping connection ({_activeProfile?.ShortTag ?? "?"}): {reason}");
        if (_proc is not null)
        {
            _lastDroppedPid = _proc.ProcessId;
            _lastDropUtc = DateTime.UtcNow;
        }

        _reader = null;
        _proc?.Dispose();
        _proc = null;
        _activeProfile = null;
        _lastCount = null;
    }

    private void Emit(int? value, PollerStatus status, GameProfile? game)
    {
        if (value == _lastCount && status == _lastStatus && ReferenceEquals(game, _lastGame))
            return;
        _lastCount = value;
        _lastStatus = status;
        _lastGame = game;
        Updated?.Invoke(this, new DeathCountEventArgs(value, status, game));
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static async Task Delay(TimeSpan span, CancellationToken ct)
    {
        try { await Task.Delay(span, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) { /* ignore */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _runner?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
        _proc?.Dispose();
    }
}
