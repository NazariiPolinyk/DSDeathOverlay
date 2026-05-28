using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

public class PointerChainBossContextReaderTests
{
    [Fact]
    public void Refresh_ChainResolvesToKnownFlag_SetsActiveBoss()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // moduleBase + 0x100 -> deref -> 66 (the boss flag).
        mem.Qwords[0x1000_0000UL + 0x100UL] = 66UL;

        var detection = new BossDetection
        {
            Type = "pointerChainFlag",
            ChainOffsets64 = new[] { 0x100 },
            FlagToBossId = new() { { "66", "iudex-gundyr" } },
        };

        var reader = new PointerChainBossContextReader(mem, detection, NullLogger.Instance);
        reader.Refresh();

        Assert.Equal("iudex-gundyr", reader.ActiveBossId);
    }

    [Fact]
    public void Refresh_FlagZero_ClearsActiveBoss()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0UL;

        var detection = new BossDetection
        {
            Type = "pointerChainFlag",
            ChainOffsets64 = new[] { 0x100 },
            FlagToBossId = new() { { "0", "" }, { "1", "vordt" } },
        };

        var reader = new PointerChainBossContextReader(mem, detection, NullLogger.Instance);
        reader.Refresh();

        Assert.Null(reader.ActiveBossId);
    }

    [Fact]
    public void Refresh_UnknownFlag_LeavesActiveBossNull()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 99UL;

        var detection = new BossDetection
        {
            Type = "pointerChainFlag",
            ChainOffsets64 = new[] { 0x100 },
            FlagToBossId = new() { { "1", "vordt" } },
        };

        var reader = new PointerChainBossContextReader(mem, detection, NullLogger.Instance);
        reader.Refresh();

        Assert.Null(reader.ActiveBossId);
    }

    [Fact]
    public void Constructor_NoChainForBitness_Throws()
    {
        var mem = new FakeMemoryReader { IsWow64 = true };
        var detection = new BossDetection
        {
            Type = "pointerChainFlag",
            // Only 64-bit chain provided, but reader is WoW64.
            ChainOffsets64 = new[] { 0x100 },
        };

        Assert.Throws<System.ArgumentException>(
            () => new PointerChainBossContextReader(mem, detection, NullLogger.Instance));
    }
}
