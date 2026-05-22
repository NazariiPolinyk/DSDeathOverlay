using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

public class PointerChainWalkTests
{
    [Fact]
    public void Walk_x64_SingleHop_ReadsValueAtBasePlusOffset()
    {
        // Module base 0x10000000, single offset 0x40, deref to 0x42.
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000 + 0x40] = 0x42; // first deref returns 0x42 as 8 bytes

        bool ok = PointerChainDeathReader.TryWalk(mem, new[] { 0x40 }, out int value);

        Assert.True(ok);
        Assert.Equal(0x42, value);
    }

    [Fact]
    public void Walk_x64_TwoHops_FollowsPointerThenReadsValue()
    {
        // Simulates DS3's [0x47572B8, 0x98] chain in miniature:
        //   addr = base + 0x100
        //   deref(addr)        -> 0x2000_0000
        //   addr = 0x2000_0000 + 0x10
        //   deref(addr)        -> 0xDEAD (the death count, in the low dword)
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000 + 0x100] = 0x2000_0000;
        mem.Qwords[0x2000_0000 + 0x010] = 0xDEAD;

        bool ok = PointerChainDeathReader.TryWalk(mem, new[] { 0x100, 0x010 }, out int value);

        Assert.True(ok);
        Assert.Equal(0xDEAD, value);
    }

    [Fact]
    public void Walk_x86_TwoHops_Uses32BitReadsAtEachStep()
    {
        // Same shape as the x64 test but the reader is in WoW64 mode so we use TryReadUInt32.
        var mem = new FakeMemoryReader { IsWow64 = true };
        mem.Dwords[0x1000_0000 + 0x100] = 0x2000_0000;
        mem.Dwords[0x2000_0000 + 0x010] = 0xBEEF;

        bool ok = PointerChainDeathReader.TryWalk(mem, new[] { 0x100, 0x010 }, out int value);

        Assert.True(ok);
        Assert.Equal(0xBEEF, value);
    }

    [Fact]
    public void Walk_NullPointerInChain_ReturnsFalse()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // First deref returns 0 (title screen / character not loaded yet).
        mem.Qwords[0x1000_0000 + 0x100] = 0;

        bool ok = PointerChainDeathReader.TryWalk(mem, new[] { 0x100, 0x010 }, out int value);

        Assert.False(ok);
        Assert.Equal(0, value);
    }

    [Fact]
    public void Walk_MissingMemory_ReturnsFalse()
    {
        // No entries populated -> TryReadUInt64 fails on the very first hop.
        var mem = new FakeMemoryReader { IsWow64 = false };

        bool ok = PointerChainDeathReader.TryWalk(mem, new[] { 0x100 }, out int value);

        Assert.False(ok);
    }

    [Fact]
    public void Walk_EmptyOffsets_ReturnsFalse()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        bool ok = PointerChainDeathReader.TryWalk(mem, System.Array.Empty<int>(), out _);
        Assert.False(ok);
    }

    [Fact]
    public void Walk_LongChain_DS2Style_WorksWithFourHops()
    {
        // Simulates DS2 SotFS x64 chain {0x16148F0, 0xD0, 0x490, 0x104} layout.
        var mem = new FakeMemoryReader { IsWow64 = false };
        ulong a = 0x1000_0000UL + 0x16148F0UL;
        ulong b = 0x3000_0000UL;
        ulong c = 0x4000_0000UL;
        ulong d = 0x5000_0000UL;

        mem.Qwords[a] = b;
        mem.Qwords[b + 0xD0] = c;
        mem.Qwords[c + 0x490] = d;
        mem.Qwords[d + 0x104] = 1234;

        bool ok = PointerChainDeathReader.TryWalk(
            mem, new[] { 0x16148F0, 0xD0, 0x490, 0x104 }, out int value);

        Assert.True(ok);
        Assert.Equal(1234, value);
    }
}
