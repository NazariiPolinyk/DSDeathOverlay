using System.IO;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;
using DSDeathOverlay.Services;

namespace DSDeathOverlay.Tests;

public class BossDeathTrackerTests
{
    private static readonly GameProfile Ds3Profile = new()
    {
        DisplayName = "Dark Souls III",
        ShortTag = "DS3",
        ProcessName = "DarkSoulsIII",
        ModuleName = "DarkSoulsIII.exe",
        ChainOffsets64 = new[] { 0x100 },
    };

    private static readonly GameProfile DsrProfile = new()
    {
        DisplayName = "Dark Souls: Remastered",
        ShortTag = "DSR",
        ProcessName = "DarkSoulsRemastered",
        ModuleName = "DarkSoulsRemastered.exe",
        AobPattern = "00",
        AobValueOffset = 0,
    };

    private static BossCatalogSet TwoGameCatalog() => new()
    {
        Games = new[]
        {
            new BossGameCatalog
            {
                GameId = "DS3",
                Detection = new BossDetection { Type = "manual" },
                Bosses = new[]
                {
                    new BossEntry { Id = "iudex-gundyr", Name = "Iudex Gundyr" },
                    new BossEntry { Id = "vordt", Name = "Vordt" },
                    new BossEntry { Id = "soul-of-cinder", Name = "Soul of Cinder" },
                },
            },
            new BossGameCatalog
            {
                GameId = "DSR",
                Detection = new BossDetection { Type = "manual" },
                Bosses = new[]
                {
                    new BossEntry { Id = "asylum-demon", Name = "Asylum Demon" },
                },
            },
        },
    };

    private static (BossDeathTracker tracker, BossDeathStore store) NewTracker()
    {
        // BossDeathStore.Load returns an empty store when no file is present at
        // %LOCALAPPDATA%\DSDeathOverlay\boss-deaths.json. For unit tests we don't
        // want to touch real disk, so we use the public API only after seeding
        // via the tracker itself. Concretely: tests do not call Save().
        var store = BossDeathStore.Load(NullLogger.Instance);
        // Clear anything left over from a previous run on the developer's machine.
        store.ResetGame("DS3");
        store.ResetGame("DSR");
        var tracker = new BossDeathTracker(NullLogger.Instance, TwoGameCatalog(), store);
        return (tracker, store);
    }

    private static DeathCountEventArgs Read(GameProfile p, int deaths)
        => new(deaths, PollerStatus.Reading, p);

    [Fact]
    public void NoActiveBoss_DeathDeltaIsIgnored()
    {
        var (tracker, store) = NewTracker();

        tracker.HandleUpdate(Read(Ds3Profile, 10));
        tracker.HandleUpdate(Read(Ds3Profile, 13));

        Assert.Equal(0, store.GetCount("DS3", "iudex-gundyr"));
        Assert.Equal(0, store.GetCount("DS3", "vordt"));
    }

    [Fact]
    public void ActiveBossSelected_NextDeltaAttributesToThatBoss()
    {
        var (tracker, store) = NewTracker();

        // First update establishes baseline so the first observation isn't credited.
        tracker.HandleUpdate(Read(Ds3Profile, 100));
        Assert.True(tracker.SetActiveBoss("iudex-gundyr"));

        tracker.HandleUpdate(Read(Ds3Profile, 101));
        tracker.HandleUpdate(Read(Ds3Profile, 103));

        Assert.Equal(3, store.GetCount("DS3", "iudex-gundyr"));
    }

    [Fact]
    public void SwitchingGames_DoesNotCreditTheNewGame()
    {
        var (tracker, store) = NewTracker();

        tracker.HandleUpdate(Read(Ds3Profile, 50));
        tracker.SetActiveBoss("iudex-gundyr");
        tracker.HandleUpdate(Read(Ds3Profile, 52)); // +2 to gundyr

        // User Alt-F4s DS3, opens DSR. Total deaths in DSR is much higher.
        tracker.HandleUpdate(Read(DsrProfile, 999));
        tracker.HandleUpdate(Read(DsrProfile, 1000)); // no active boss for DSR yet

        Assert.Equal(2, store.GetCount("DS3", "iudex-gundyr"));
        Assert.Equal(0, store.GetCount("DSR", "asylum-demon"));
    }

    [Fact]
    public void ActiveBoss_IsPerGame()
    {
        var (tracker, store) = NewTracker();

        tracker.HandleUpdate(Read(Ds3Profile, 0));
        tracker.SetActiveBoss("iudex-gundyr");
        tracker.HandleUpdate(Read(Ds3Profile, 1));

        tracker.HandleUpdate(Read(DsrProfile, 100));
        tracker.SetActiveBoss("asylum-demon");
        tracker.HandleUpdate(Read(DsrProfile, 105)); // +5 to asylum-demon

        // Switch back to DS3. The DS3 active boss is still iudex-gundyr.
        tracker.HandleUpdate(Read(Ds3Profile, 1));
        tracker.HandleUpdate(Read(Ds3Profile, 4)); // +3 to gundyr

        Assert.Equal(4, store.GetCount("DS3", "iudex-gundyr"));
        Assert.Equal(5, store.GetCount("DSR", "asylum-demon"));
    }

    [Fact]
    public void CycleActiveBoss_VisitsNoneAndEachBossInOrder()
    {
        var (tracker, _) = NewTracker();
        tracker.HandleUpdate(Read(Ds3Profile, 0));

        // Start: none. Forward -> first boss.
        tracker.CycleActiveBoss(+1);
        Assert.Equal("iudex-gundyr", tracker.BuildSnapshot().ActiveBossId);

        tracker.CycleActiveBoss(+1);
        Assert.Equal("vordt", tracker.BuildSnapshot().ActiveBossId);

        tracker.CycleActiveBoss(+1);
        Assert.Equal("soul-of-cinder", tracker.BuildSnapshot().ActiveBossId);

        tracker.CycleActiveBoss(+1);
        Assert.Null(tracker.BuildSnapshot().ActiveBossId);

        // Backwards from none -> last boss.
        tracker.CycleActiveBoss(-1);
        Assert.Equal("soul-of-cinder", tracker.BuildSnapshot().ActiveBossId);
    }

    [Fact]
    public void DecreasingTotal_DoesNotDecrementBoss()
    {
        var (tracker, store) = NewTracker();
        tracker.HandleUpdate(Read(Ds3Profile, 50));
        tracker.SetActiveBoss("vordt");
        tracker.HandleUpdate(Read(Ds3Profile, 52)); // +2

        // User loaded a different save with a lower death count.
        tracker.HandleUpdate(Read(Ds3Profile, 10));
        tracker.HandleUpdate(Read(Ds3Profile, 11)); // +1 again

        Assert.Equal(3, store.GetCount("DS3", "vordt"));
    }

    [Fact]
    public void SetActiveBoss_UnknownId_ReturnsFalseAndChangesNothing()
    {
        var (tracker, _) = NewTracker();
        tracker.HandleUpdate(Read(Ds3Profile, 0));

        bool ok = tracker.SetActiveBoss("not-a-real-boss");

        Assert.False(ok);
        Assert.Null(tracker.BuildSnapshot().ActiveBossId);
    }

    [Fact]
    public void Snapshot_IncludesEveryBossWithItsCount()
    {
        var (tracker, _) = NewTracker();
        tracker.HandleUpdate(Read(Ds3Profile, 0));
        tracker.SetActiveBoss("vordt");
        tracker.HandleUpdate(Read(Ds3Profile, 7));

        var snap = tracker.BuildSnapshot();

        Assert.Equal(3, snap.Rows.Count);
        Assert.Contains(snap.Rows, r => r.Id == "vordt" && r.Count == 7 && r.IsActive);
        Assert.Contains(snap.Rows, r => r.Id == "iudex-gundyr" && r.Count == 0 && !r.IsActive);
        Assert.Equal("Vordt", snap.ActiveBossName);
        Assert.Equal(7, snap.ActiveBossCount);
    }

    [Fact]
    public void ResetActiveBossCount_ClearsJustThatBoss()
    {
        var (tracker, store) = NewTracker();
        tracker.HandleUpdate(Read(Ds3Profile, 0));
        tracker.SetActiveBoss("vordt");
        tracker.HandleUpdate(Read(Ds3Profile, 5));
        tracker.SetActiveBoss("iudex-gundyr");
        tracker.HandleUpdate(Read(Ds3Profile, 8));

        tracker.SetActiveBoss("vordt");
        tracker.ResetActiveBossCount();

        Assert.Equal(0, store.GetCount("DS3", "vordt"));
        Assert.Equal(3, store.GetCount("DS3", "iudex-gundyr"));
    }

    [Fact]
    public void ResetAllForCurrentGame_ClearsOnlyCurrentGame()
    {
        var (tracker, store) = NewTracker();
        tracker.HandleUpdate(Read(DsrProfile, 0));
        tracker.SetActiveBoss("asylum-demon");
        tracker.HandleUpdate(Read(DsrProfile, 4));

        tracker.HandleUpdate(Read(Ds3Profile, 0));
        tracker.SetActiveBoss("vordt");
        tracker.HandleUpdate(Read(Ds3Profile, 2));

        // Currently in DS3 — resetting should leave DSR alone.
        tracker.ResetAllForCurrentGame();

        Assert.Equal(0, store.GetCount("DS3", "vordt"));
        Assert.Equal(4, store.GetCount("DSR", "asylum-demon"));
    }
}
