using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

public class BossCatalogStoreTests
{
    [Fact]
    public void EmbeddedCatalog_CoversAllFourGames()
    {
        var set = BossCatalogStore.LoadEmbedded();

        Assert.NotNull(set);
        var ids = set!.Games.Select(g => g.GameId).ToArray();
        Assert.Contains("DSR", ids);
        Assert.Contains("DS2", ids);
        Assert.Contains("DS3", ids);
        Assert.Contains("SEK", ids);
    }

    [Fact]
    public void EmbeddedCatalog_DefaultsToManualDetection()
    {
        var set = BossCatalogStore.LoadEmbedded()!;
        foreach (var g in set.Games)
        {
            Assert.NotNull(g.Detection);
            Assert.Equal("manual", g.Detection!.Type);
        }
    }

    [Fact]
    public void EmbeddedCatalog_DS3_HasKnownMainBosses()
    {
        var ds3 = BossCatalogStore.LoadEmbedded()!["DS3"];
        Assert.NotNull(ds3);
        var bossIds = ds3!.Bosses.Select(b => b.Id).ToArray();

        Assert.Contains("iudex-gundyr", bossIds);
        Assert.Contains("soul-of-cinder", bossIds);
        Assert.Contains("slave-knight-gael", bossIds); // DLC2
        Assert.Contains("sister-friede", bossIds);     // DLC1
    }

    [Fact]
    public void EmbeddedCatalog_BossIdsAreUniqueWithinEachGame()
    {
        var set = BossCatalogStore.LoadEmbedded()!;
        foreach (var g in set.Games)
        {
            var dupes = g.Bosses
                .GroupBy(b => b.Id)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToArray();
            Assert.Empty(dupes);
        }
    }

    [Fact]
    public void Indexer_IsCaseInsensitive()
    {
        var set = BossCatalogStore.LoadEmbedded()!;
        Assert.NotNull(set["dsr"]);
        Assert.NotNull(set["Ds3"]);
    }

    [Fact]
    public void Deserialize_PointerChainFlagDetection_Parses()
    {
        const string json = """
            {
              "games": [
                {
                  "gameId": "DS3",
                  "detection": {
                    "type": "pointerChainFlag",
                    "chainOffsets64": [ 16, 32 ],
                    "flagToBossId": {
                      "0": "",
                      "100": "iudex-gundyr",
                      "200": "vordt"
                    }
                  },
                  "bosses": [
                    { "id": "iudex-gundyr", "name": "Iudex Gundyr" },
                    { "id": "vordt", "name": "Vordt" }
                  ]
                }
              ]
            }
            """;

        var set = BossCatalogStore.Deserialize(json);

        Assert.NotNull(set);
        var ds3 = set!["DS3"]!;
        Assert.Equal("pointerChainFlag", ds3.Detection!.Type);
        Assert.Equal(new[] { 16, 32 }, ds3.Detection.ChainOffsets64);
        Assert.Equal("iudex-gundyr", ds3.Detection.FlagToBossId!["100"]);
    }

    [Fact]
    public void Load_NoExternalFile_UsesEmbedded()
    {
        var set = BossCatalogStore.Load(NullLogger.Instance);
        Assert.NotEmpty(set.Games);
    }
}
