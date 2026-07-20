using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

public class GameProfileStoreTests
{
    [Fact]
    public void EmbeddedGames_HasAllFourSupportedTitles()
    {
        var set = GameProfileStore.LoadEmbedded();

        Assert.NotNull(set);
        Assert.NotEmpty(set!.Games);

        var shortTags = set.Games.Select(g => g.ShortTag).ToArray();
        Assert.Contains("DSR", shortTags);
        Assert.Contains("DS2", shortTags);
        Assert.Contains("DS3", shortTags);
        Assert.Contains("SEK", shortTags);
    }

    [Fact]
    public void EmbeddedGames_DsrUsesAobPattern()
    {
        var set = GameProfileStore.LoadEmbedded()!;
        var dsr = set.Games.Single(g => g.ShortTag == "DSR");

        Assert.True(dsr.UsesAob);
        Assert.False(dsr.UsesPointerChain);
        Assert.Equal(0x98, dsr.AobValueOffset);
        Assert.Equal("DarkSoulsRemastered", dsr.ProcessName);
    }

    [Fact]
    public void EmbeddedGames_Ds2_HasBoth32And64BitChains()
    {
        var set = GameProfileStore.LoadEmbedded()!;
        var ds2 = set.Games.Single(g => g.ShortTag == "DS2");

        Assert.False(ds2.UsesAob);
        Assert.True(ds2.UsesPointerChain);
        Assert.NotNull(ds2.ChainOffsets32);
        Assert.NotNull(ds2.ChainOffsets64);
        Assert.NotNull(ds2.ChainVariants64);
        Assert.NotEmpty(ds2.ChainVariants64!);
        Assert.True(ds2.ChainVariants64![0].FinalHopInt32);

        // Must match DSDeaths / SrShadowy hex literals (0x16244F0 was a bad decimal typo).
        const int dsDeathsBase = 0x16148F0;
        Assert.Equal(dsDeathsBase, ds2.ChainOffsets64![0]);
        Assert.Equal(dsDeathsBase, ds2.ChainVariants64![0].Offsets[0]);
    }

    [Theory]
    [InlineData("DS3")]
    [InlineData("SEK")]
    public void EmbeddedGames_64BitOnlyTitles_OnlyHave64BitChain(string tag)
    {
        var set = GameProfileStore.LoadEmbedded()!;
        var profile = set.Games.Single(g => g.ShortTag == tag);

        Assert.True(profile.UsesPointerChain);
        Assert.Null(profile.ChainOffsets32);
        Assert.NotNull(profile.ChainOffsets64);
        Assert.NotEmpty(profile.ChainOffsets64!);
    }

    [Theory]
    // Base offset (first chain element) must match DSDeaths master hex exactly.
    // A mis-converted decimal reads a null static slot and stalls on "load a save".
    [InlineData("DS3", 0x47572B8, 0x98)]
    [InlineData("SEK", 0x3D5AAC0, 0x90)]
    public void EmbeddedGames_64BitBases_MatchDsDeathsMaster(string tag, int expectedBase, int expectedSecond)
    {
        var set = GameProfileStore.LoadEmbedded()!;
        var chain = set.Games.Single(g => g.ShortTag == tag).ChainOffsets64!;

        Assert.Equal(expectedBase, chain[0]);
        Assert.Equal(expectedSecond, chain[1]);
    }

    [Fact]
    public void Deserialize_ValidJson_RoundTrips()
    {
        const string json = """
            {
              "games": [
                {
                  "displayName": "Test",
                  "shortTag": "T",
                  "processName": "test",
                  "moduleName": "test.exe",
                  "chainOffsets64": [ 16, 32 ]
                }
              ]
            }
            """;

        var set = GameProfileStore.Deserialize(json);

        Assert.NotNull(set);
        Assert.Single(set!.Games);
        Assert.Equal("T", set.Games[0].ShortTag);
        Assert.Equal(new[] { 16, 32 }, set.Games[0].ChainOffsets64);
    }

    [Fact]
    public void Deserialize_JsonWithComments_Succeeds()
    {
        // The shipped games.json has a "$comment" key. Make sure the loader tolerates it.
        const string json = """
            {
              "$comment": "freeform",
              "games": []
            }
            """;

        var set = GameProfileStore.Deserialize(json);
        Assert.NotNull(set);
        Assert.Empty(set!.Games);
    }

    [Fact]
    public void Load_NoExternalFile_UsesEmbedded()
    {
        // AppContext.BaseDirectory under test won't contain a games.json — we should
        // transparently fall back to the embedded resource.
        var set = GameProfileStore.Load(NullLogger.Instance);
        Assert.NotEmpty(set.Games);
    }
}
