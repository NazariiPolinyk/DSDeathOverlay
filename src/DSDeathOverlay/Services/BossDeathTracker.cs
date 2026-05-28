using System;
using System.Collections.Generic;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Services;

/// <summary>
/// Snapshot of per-boss state surfaced alongside each <see cref="DeathCountEventArgs"/>.
/// </summary>
public sealed class BossSnapshot
{
    /// <summary>Active boss id, or null when nothing is selected.</summary>
    public string? ActiveBossId { get; init; }

    /// <summary>Display name of the active boss, or null when nothing is selected.</summary>
    public string? ActiveBossName { get; init; }

    /// <summary>Active boss's current death count (0 when nothing is selected).</summary>
    public int ActiveBossCount { get; init; }

    /// <summary>
    /// All bosses for the active game with their current counts (including bosses
    /// that have never been died to — those show 0). Ordered the same way the
    /// catalog lists them.
    /// </summary>
    public IReadOnlyList<BossCountRow> Rows { get; init; } = System.Array.Empty<BossCountRow>();
}

public sealed class BossCountRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Attributes deaths to whichever boss is "active" at the moment the total death
/// counter ticks up. Pure orchestrator: takes its data from
/// <see cref="DeathPoller"/> updates and an <see cref="IBossContextReader"/>,
/// stores increments in a <see cref="BossDeathStore"/>, and exposes a snapshot
/// the view-model can render.
///
/// Single-threaded; the poller raises <see cref="DeathPoller.Updated"/> from one
/// background thread and the WPF UI thread reads <see cref="BuildSnapshot"/>.
/// Changes are guarded by a lock so the snapshot is always self-consistent.
/// </summary>
public sealed class BossDeathTracker
{
    private readonly ILogger _log;
    private readonly BossCatalogSet _catalog;
    private readonly BossDeathStore _store;
    private readonly object _gate = new();

    private GameProfile? _activeGame;
    private int? _lastDeathCount;

    /// <summary>
    /// Per-game manual reader. We keep one per game because the active-boss
    /// selection should stick when the user briefly closes/reopens the game,
    /// and so cycling logic resets correctly when switching titles.
    /// </summary>
    private readonly Dictionary<string, ManualBossContextReader> _manualReaders = new();

    public BossDeathTracker(ILogger log, BossCatalogSet catalog, BossDeathStore store)
    {
        _log = log ?? NullLogger.Instance;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Hooks the poller; call once at startup.</summary>
    public void Attach(DeathPoller poller)
    {
        if (poller is null) throw new ArgumentNullException(nameof(poller));
        poller.Updated += OnPollerUpdated;
    }

    private void OnPollerUpdated(object? sender, DeathCountEventArgs e)
        => HandleUpdate(e);

    /// <summary>
    /// Exposed for tests so the tracker can be driven without a real poller.
    /// The poller calls this via the <c>Updated</c> event in production.
    /// </summary>
    internal void HandleUpdate(DeathCountEventArgs e)
    {
        lock (_gate)
        {
            bool gameChanged = !ReferenceEquals(_activeGame, e.Game);
            _activeGame = e.Game;

            if (gameChanged)
            {
                // Don't attribute the first read after switching games to anything —
                // the new total is whatever the save already has.
                _lastDeathCount = e.DeathCount;
                return;
            }

            if (e.DeathCount is not int current) return;

            int previous = _lastDeathCount ?? current;
            _lastDeathCount = current;

            int delta = current - previous;
            if (delta <= 0) return;

            string? gameId = e.Game?.ShortTag;
            if (string.IsNullOrEmpty(gameId)) return;

            string? bossId = GetActiveBossIdFor(gameId);
            if (bossId is null) return;

            for (int i = 0; i < delta; i++)
                _store.Increment(gameId, bossId);

            _log.Log($"[{gameId}] attributed {delta} death(s) to '{bossId}' (total now {_store.GetCount(gameId, bossId)}).");
        }
    }

    /// <summary>
    /// Set the active boss for the current game. Returns false when there is no
    /// active game or the boss id is unknown.
    /// </summary>
    public bool SetActiveBoss(string? bossId)
    {
        lock (_gate)
        {
            if (_activeGame is null) return false;
            string gameId = _activeGame.ShortTag;
            var manual = GetOrCreateManualReader(gameId);

            if (bossId is null)
            {
                manual.SetActiveBossId(null);
                return true;
            }

            var catalog = _catalog[gameId];
            if (catalog is null) return false;

            foreach (var b in catalog.Bosses)
            {
                if (string.Equals(b.Id, bossId, StringComparison.OrdinalIgnoreCase))
                {
                    manual.SetActiveBossId(b.Id);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Cycle the active boss for the current game: null -> first -> second ... -> null.
    /// <paramref name="direction"/> +1 cycles forward, -1 cycles backward.
    /// </summary>
    public void CycleActiveBoss(int direction)
    {
        lock (_gate)
        {
            if (_activeGame is null) return;
            string gameId = _activeGame.ShortTag;
            var catalog = _catalog[gameId];
            if (catalog is null || catalog.Bosses.Length == 0) return;

            var manual = GetOrCreateManualReader(gameId);
            string? current = manual.ActiveBossId;

            // Build a virtual list: null + each boss in catalog order.
            int currentIndex = 0;
            for (int i = 0; i < catalog.Bosses.Length; i++)
            {
                if (string.Equals(catalog.Bosses[i].Id, current, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i + 1;
                    break;
                }
            }

            int total = catalog.Bosses.Length + 1; // +1 for the "none" slot
            int next = (currentIndex + (direction >= 0 ? 1 : -1) + total) % total;

            manual.SetActiveBossId(next == 0 ? null : catalog.Bosses[next - 1].Id);
        }
    }

    /// <summary>Reset the count for one boss in the current game.</summary>
    public void ResetActiveBossCount()
    {
        lock (_gate)
        {
            if (_activeGame is null) return;
            string gameId = _activeGame.ShortTag;
            string? bossId = GetActiveBossIdFor(gameId);
            if (bossId is null) return;
            _store.ResetBoss(gameId, bossId);
        }
    }

    /// <summary>Reset all boss counts for the current game.</summary>
    public void ResetAllForCurrentGame()
    {
        lock (_gate)
        {
            if (_activeGame is null) return;
            _store.ResetGame(_activeGame.ShortTag);
        }
    }

    /// <summary>Build a snapshot of the current state for the view-model.</summary>
    public BossSnapshot BuildSnapshot()
    {
        lock (_gate)
        {
            if (_activeGame is null) return new BossSnapshot();
            string gameId = _activeGame.ShortTag;

            var catalog = _catalog[gameId];
            if (catalog is null) return new BossSnapshot();

            string? activeId = GetActiveBossIdFor(gameId);

            var rows = new List<BossCountRow>(catalog.Bosses.Length);
            string? activeName = null;
            int activeCount = 0;

            foreach (var b in catalog.Bosses)
            {
                bool isActive = string.Equals(b.Id, activeId, StringComparison.OrdinalIgnoreCase);
                int count = _store.GetCount(gameId, b.Id);

                rows.Add(new BossCountRow
                {
                    Id = b.Id,
                    Name = b.Name,
                    Count = count,
                    IsActive = isActive,
                });

                if (isActive)
                {
                    activeName = b.Name;
                    activeCount = count;
                }
            }

            return new BossSnapshot
            {
                ActiveBossId = activeId,
                ActiveBossName = activeName,
                ActiveBossCount = activeCount,
                Rows = rows,
            };
        }
    }

    /// <summary>Persist to disk; call on shutdown.</summary>
    public void Save() => _store.Save();

    private string? GetActiveBossIdFor(string gameId)
    {
        return _manualReaders.TryGetValue(gameId, out var r) ? r.ActiveBossId : null;
    }

    private ManualBossContextReader GetOrCreateManualReader(string gameId)
    {
        if (!_manualReaders.TryGetValue(gameId, out var r))
        {
            r = new ManualBossContextReader();
            _manualReaders[gameId] = r;
        }
        return r;
    }
}
