using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

public class PatternScannerTests
{
    [Fact]
    public void ParsePattern_TextWithWildcards_ProducesCorrectMask()
    {
        var (bytes, mask) = PatternScanner.ParsePattern("48 8B 05 ? ? ? ? 48");

        Assert.Equal(new byte[] { 0x48, 0x8B, 0x05, 0, 0, 0, 0, 0x48 }, bytes);
        Assert.Equal("xxx????x", mask);
    }

    [Fact]
    public void ParsePattern_InvalidToken_Throws()
    {
        Assert.Throws<ArgumentException>(() => PatternScanner.ParsePattern("ZZ 11"));
    }

    [Fact]
    public void ParsePattern_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PatternScanner.ParsePattern("   "));
    }

    [Fact]
    public void Find_NoWildcards_FindsExactMatch()
    {
        byte[] haystack = { 0x00, 0x11, 0x22, 0x48, 0x8B, 0x05, 0xAA, 0xBB };
        byte[] pattern  = { 0x48, 0x8B, 0x05 };

        int idx = PatternScanner.Find(haystack, pattern, "xxx");

        Assert.Equal(3, idx);
    }

    [Fact]
    public void Find_WithWildcards_MatchesAcrossUnknownBytes()
    {
        // simulate "48 8B 05 ?? ?? ?? ?? 48 85 C0" surrounded by noise
        byte[] haystack =
        {
            0x90, 0x90, 0x90,
            0x48, 0x8B, 0x05, 0xDE, 0xAD, 0xBE, 0xEF, 0x48, 0x85, 0xC0,
            0xCC, 0xCC
        };
        var (pattern, mask) = PatternScanner.ParsePattern(
            "48 8B 05 ? ? ? ? 48 85 C0");

        int idx = PatternScanner.Find(haystack, pattern, mask);

        Assert.Equal(3, idx);
    }

    [Fact]
    public void Find_NoMatch_ReturnsMinusOne()
    {
        byte[] haystack = { 0x00, 0x11, 0x22, 0x33 };
        byte[] pattern  = { 0xAA, 0xBB };

        int idx = PatternScanner.Find(haystack, pattern, "xx");

        Assert.Equal(-1, idx);
    }

    [Fact]
    public void Find_PatternLongerThanHaystack_ReturnsMinusOne()
    {
        byte[] haystack = { 0x48 };
        byte[] pattern  = { 0x48, 0x8B };

        int idx = PatternScanner.Find(haystack, pattern, "xx");

        Assert.Equal(-1, idx);
    }

    [Fact]
    public void Find_MatchAtStart()
    {
        byte[] haystack = { 0xAA, 0xBB, 0xCC };
        byte[] pattern  = { 0xAA, 0xBB };

        Assert.Equal(0, PatternScanner.Find(haystack, pattern, "xx"));
    }

    [Fact]
    public void Find_MatchAtEnd()
    {
        byte[] haystack = { 0x00, 0x00, 0xAA, 0xBB };
        byte[] pattern  = { 0xAA, 0xBB };

        Assert.Equal(2, PatternScanner.Find(haystack, pattern, "xx"));
    }

    [Fact]
    public void Find_AllWildcards_MatchesAtZero()
    {
        byte[] haystack = { 0x01, 0x02, 0x03 };
        byte[] pattern  = { 0x00, 0x00 };

        Assert.Equal(0, PatternScanner.Find(haystack, pattern, "??"));
    }

    [Fact]
    public void Find_FullDSRPattern_FoundAtKnownOffset()
    {
        // Build a synthetic 256-byte buffer with the real DSR ChrClassBase pattern
        // dropped at offset 0x40, then ensure we recover that offset.
        const int InsertOffset = 0x40;
        var (pattern, mask) = PatternScanner.ParsePattern(DeathReader.ChrClassBasePattern);

        var haystack = new byte[256];
        for (int i = 0; i < haystack.Length; i++) haystack[i] = (byte)(i ^ 0x37); // noise
        // Insert pattern bytes; wildcards keep their noise.
        for (int i = 0; i < pattern.Length; i++)
        {
            if (mask[i] == 'x') haystack[InsertOffset + i] = pattern[i];
        }

        int idx = PatternScanner.Find(haystack, pattern, mask);

        Assert.Equal(InsertOffset, idx);
    }
}
