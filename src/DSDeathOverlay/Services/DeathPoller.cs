using System;
using System.Threading;
using System.Threading.Tasks;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Services;

/// <summary>
/// State surfaced to the UI alongside the death count.
/// </summary>
public enum PollerStatus
{
    /// <summary>We are looking for DarkSoulsRemastered.exe in the process list.</summary>
    WaitingForGame,
    /// <summary>Game found; trying to resolve the ChrClassBase pattern (game may still be loading).</summary>
    ResolvingPattern,
    /// <summary>Resolved, but the ChrClassBase pointer is null (likely title screen / no character loaded).</summary>
    WaitingForCharacter,
    /// <summary>Reading the death count successfully.</summary>
    Reading,
}

public sealed class DeathCountEventArgs : EventArgs
{
    public int? DeathCount { get; }
    public PollerStatus Status { get; }

    public DeathCountEventArgs(int? deathCount, PollerStatus status)
    {
        DeathCount = deathCount;
        Status = status;
    }
}

/// <summary>
/// Background service that owns the lifecycle of the connection to DSR:
///
///   * Polls for the game process every ~1s when not connected.
///   * Once connected, resolves the AOB once and caches it.
///   * Reads the death count every <see cref="PollInterval"/>.
///   * Raises <see cref="Updated"/> whenever the value or status changes.
///
/// Recovers automatically if the game is closed and reopened.
/// </summary>
public sealed class DeathPoller : IDisposable
{
    private readonly ILogger _log;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _reconnectInterval;

    private readonly CancellationTokenSource _cts = new();
    private Task? _runner;

    private ProcessAccess? _proc;
    private DeathReader? _reader;

    private int? _lastCount;
    private PollerStatus _lastStatus = (PollerStatus)(-1); // force initial change event

    /// <summary>Raised on every state change (death count or status transition).</summary>
    public event EventHandler<DeathCountEventArgs>? Updated;

    public DeathPoller(ILogger log, TimeSpan? pollInterval = null, TimeSpan? reconnectInterval = null)
    {
        _log = log ?? NullLogger.Instance;
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
                    Emit(null, PollerStatus.WaitingForGame);
                    _proc = ProcessAccess.TryOpenDarkSouls(_log);
                    if (_proc is null)
                    {
                        await Delay(_reconnectInterval, ct).ConfigureAwait(false);
                        continue;
                    }
                    _reader = new DeathReader(_proc, _log);
                }

                if (_reader is { IsResolved: false })
                {
                    Emit(null, PollerStatus.ResolvingPattern);
                    if (!_reader.ResolveChrClassBase())
                    {
                        // Could be transient (game still booting). Wait and retry.
                        await Delay(_reconnectInterval, ct).ConfigureAwait(false);

                        // Detect a closed/restarted game so we drop the stale handle.
                        if (!IsProcessAlive(_proc!.ProcessId))
                        {
                            DropConnection("game process exited during AOB scan");
                        }
                        continue;
                    }
                }

                int? value = _reader!.TryReadDeathCount();
                if (value is null)
                {
                    // Either the static pointer is null (title screen) or the read failed.
                    // Distinguish: a totally failed read means the process is gone.
                    if (!IsProcessAlive(_proc!.ProcessId))
                    {
                        DropConnection("game process exited");
                        continue;
                    }
                    Emit(null, PollerStatus.WaitingForCharacter);
                }
                else
                {
                    Emit(value, PollerStatus.Reading);
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

    private void DropConnection(string reason)
    {
        _log.Log($"Dropping DSR connection: {reason}");
        _reader = null;
        _proc?.Dispose();
        _proc = null;
        _lastCount = null;
    }

    private void Emit(int? value, PollerStatus status)
    {
        if (value == _lastCount && status == _lastStatus) return;
        _lastCount = value;
        _lastStatus = status;
        Updated?.Invoke(this, new DeathCountEventArgs(value, status));
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
