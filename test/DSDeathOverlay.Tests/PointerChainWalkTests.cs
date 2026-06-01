using System.Collections.Generic;
using DSDeathOverlay.Logging;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

/// <summary>Test-only logger that keeps every line for assertions.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = new();
    public void Log(string message) => Lines.Add(message);
}

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

    [Fact]
    public void TryWalkDiagnostic_NullDerefMidChain_ReportsHopAndReason()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // First hop returns 0 -> second hop should fail.
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0;

        bool ok = PointerChainDeathReader.TryWalkDiagnostic(
            mem,
            new[] { 0x100, 0x010 },
            out _,
            out _,
            out string? failure);

        Assert.False(ok);
        Assert.NotNull(failure);
        Assert.Contains("hop 1", failure);
        Assert.Contains("title screen", failure!);
    }

    [Fact]
    public void TryWalkDiagnostic_UnreadableMemory_ReportsHopAndFailedRead()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // Nothing populated -> first read fails.
        bool ok = PointerChainDeathReader.TryWalkDiagnostic(
            mem,
            new[] { 0x100 },
            out _,
            out _,
            out string? failure);

        Assert.False(ok);
        Assert.NotNull(failure);
        Assert.Contains("hop 0", failure);
        Assert.Contains("TryReadUInt64", failure!);
    }
}

public class PointerChainDeathReaderTests
{
    private static GameProfile FakeProfile(int[] chain) => new()
    {
        DisplayName = "Test",
        ShortTag = "TST",
        ProcessName = "test",
        ModuleName = "test.exe",
        ChainOffsets64 = chain,
    };

    [Fact]
    public void TryReadDeathCount_HighValue_NoLongerRejected()
    {
        // Pre-fix this would have been silently dropped (was > 1,000,000 cap).
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 1_500_000UL;

        var reader = new PointerChainDeathReader(
            mem, FakeProfile(new[] { 0x100 }), NullLogger.Instance);
        Assert.True(reader.Initialize());

        int? value = reader.TryReadDeathCount();

        Assert.Equal(1_500_000, value);
    }

    [Fact]
    public void TryReadDeathCount_ChainFails_ReturnsNull()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // No memory populated; first hop's deref fails.
        var reader = new PointerChainDeathReader(
            mem, FakeProfile(new[] { 0x100 }), NullLogger.Instance);
        Assert.True(reader.Initialize());

        int? value = reader.TryReadDeathCount();

        Assert.Null(value);
    }

    [Fact]
    public void TryReadDeathCount_NegativeValue_RejectedAsGarbage()
    {
        var mem = new FakeMemoryReader { IsWow64 = false };
        // -1 as int = 0xFFFFFFFF; as the low 32 bits of a qword the rest is 0.
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0x0000_0000_FFFF_FFFFUL;

        var reader = new PointerChainDeathReader(
            mem, FakeProfile(new[] { 0x100 }), NullLogger.Instance);
        Assert.True(reader.Initialize());

        int? value = reader.TryReadDeathCount();

        Assert.Null(value);
    }

    [Fact]
    public void TryReadDeathCount_NegativeValue_LogsRejection()
    {
        // Regression for the silent path that left DS2 SotFS sitting on
        // "(load a save)" with zero log output. The endpoint qword's low 32 bits
        // are 0xFFFFFFFF (-1 as signed int) — simulating the chain walking
        // through readable memory but landing on a slot whose value is garbage.
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0x0000_0000_FFFF_FFFFUL;
        var logger = new CapturingLogger();

        var reader = new PointerChainDeathReader(mem, FakeProfile(new[] { 0x100 }), logger);
        Assert.True(reader.Initialize());

        int? value = reader.TryReadDeathCount();

        Assert.Null(value);
        Assert.Contains(logger.Lines, line =>
            line.Contains("[TST]")
            && line.Contains("chain produced negative value")
            && line.Contains("-1"));
    }

    [Fact]
    public void TryReadDeathCount_NegativeValue_ThrottlesRepeatedLogs()
    {
        // Two back-to-back rejections must collapse to a single log line; the
        // 5-second throttle is shared with the chain-failure path.
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0x0000_0000_FFFF_FFFFUL;
        var logger = new CapturingLogger();

        var reader = new PointerChainDeathReader(mem, FakeProfile(new[] { 0x100 }), logger);
        Assert.True(reader.Initialize());

        reader.TryReadDeathCount();
        reader.TryReadDeathCount();

        int rejectionLines = logger.Lines.FindAll(l =>
            l.Contains("chain produced negative value")).Count;
        Assert.Equal(1, rejectionLines);
    }

    [Fact]
    public void TryReadDeathCount_ZeroDeaths_IsSurfaced()
    {
        // A brand-new character must report 0, not be confused with a failed read.
        var mem = new FakeMemoryReader { IsWow64 = false };
        mem.Qwords[0x1000_0000UL + 0x100UL] = 0UL;

        var reader = new PointerChainDeathReader(
            mem, FakeProfile(new[] { 0x100 }), NullLogger.Instance);
        Assert.True(reader.Initialize());

        // First hop dereferences to 0, which the walker treats as "null pointer
        // for the next hop". For a 1-hop chain, the deref *is* the final value,
        // so the walker should return 0, not abort.
        int? value = reader.TryReadDeathCount();

        Assert.Equal(0, value);
    }
}
