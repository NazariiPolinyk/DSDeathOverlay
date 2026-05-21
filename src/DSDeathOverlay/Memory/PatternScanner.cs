using System;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Pure in-memory AOB (array-of-bytes) + mask pattern scanner.
///
/// Designed to be trivially unit-testable: every method operates on
/// <see cref="ReadOnlySpan{Byte}"/> with no I/O. The remote-process integration
/// happens one layer up in <see cref="ProcessAccess"/>, which reads memory into
/// a buffer and hands the buffer to this class.
/// </summary>
public static class PatternScanner
{
    /// <summary>
    /// Parse a textual cheat-engine style pattern like
    /// "48 8B 05 ? ? ? ? 48 85 C0" into a (bytes, mask) tuple.
    /// 'x' in the mask = match exactly, '?' = wildcard byte.
    /// </summary>
    public static (byte[] Bytes, string Mask) ParsePattern(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Pattern is empty.", nameof(text));

        var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        Span<char> mask = stackalloc char[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            string tok = tokens[i];
            if (tok == "?" || tok == "??")
            {
                bytes[i] = 0;
                mask[i] = '?';
            }
            else if (tok.Length == 2 &&
                     byte.TryParse(tok, System.Globalization.NumberStyles.HexNumber,
                                   System.Globalization.CultureInfo.InvariantCulture, out byte b))
            {
                bytes[i] = b;
                mask[i] = 'x';
            }
            else
            {
                throw new ArgumentException($"Invalid pattern token: '{tok}'", nameof(text));
            }
        }

        return (bytes, new string(mask));
    }

    /// <summary>
    /// Scan <paramref name="haystack"/> for the first occurrence of <paramref name="pattern"/>
    /// where <paramref name="mask"/>[i] == 'x' requires an exact byte match and any other
    /// character (typically '?') is a wildcard.
    /// </summary>
    /// <returns>The 0-based offset of the first match, or -1 if not found.</returns>
    public static int Find(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> pattern,
        ReadOnlySpan<char> mask)
    {
        if (pattern.Length == 0)
            throw new ArgumentException("Pattern is empty.", nameof(pattern));
        if (mask.Length != pattern.Length)
            throw new ArgumentException("Mask length must equal pattern length.", nameof(mask));
        if (haystack.Length < pattern.Length)
            return -1;

        int lastStart = haystack.Length - pattern.Length;

        // Find the first non-wildcard position; that's our cheap quick-reject byte.
        int anchorIdx = 0;
        while (anchorIdx < mask.Length && mask[anchorIdx] != 'x')
            anchorIdx++;

        if (anchorIdx == mask.Length)
        {
            // Degenerate: a pattern made entirely of wildcards matches at offset 0.
            return haystack.Length >= pattern.Length ? 0 : -1;
        }

        byte anchor = pattern[anchorIdx];

        for (int i = 0; i <= lastStart; i++)
        {
            if (haystack[i + anchorIdx] != anchor)
                continue;

            if (MatchesAt(haystack, i, pattern, mask))
                return i;
        }

        return -1;
    }

    private static bool MatchesAt(
        ReadOnlySpan<byte> haystack,
        int offset,
        ReadOnlySpan<byte> pattern,
        ReadOnlySpan<char> mask)
    {
        for (int j = 0; j < pattern.Length; j++)
        {
            if (mask[j] == 'x' && haystack[offset + j] != pattern[j])
                return false;
        }
        return true;
    }
}
