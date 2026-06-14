namespace NlsDataGenerator;

// Shared Unicode code-point constants used across the data generators (code-space bounds and the
// UTF-16 surrogate ranges). Format-specific magic numbers (CE32 masks, norm16 thresholds, trie
// encoding constants) stay with their own format classes; this holds only general code-point
// values. Defined in the root namespace so every sub-namespace sees it without a using.
internal static class Unicode
{
    // The highest valid code point, U+10FFFF.
    public const int MaxCodePoint = 0x10FFFF;

    // One past the highest code point; the exclusive upper bound for code-point ranges.
    public const int CodePointLimit = 0x110000;

    // The first supplementary code point; also the number of BMP code points (the BMP limit).
    public const int SupplementaryMin = 0x10000;

    // The highest BMP code point, U+FFFF.
    public const int MaxBmpCodePoint = 0xFFFF;

    // One past the highest ASCII code point.
    public const int AsciiLimit = 0x80;

    // U+FFFD, the replacement character.
    public const int ReplacementCharacter = 0xFFFD;

    public const int LeadSurrogateMin = 0xD800;
    public const int LeadSurrogateMax = 0xDBFF;
    public const int TrailSurrogateMin = 0xDC00;
    public const int TrailSurrogateMax = 0xDFFF;

    // One past the last surrogate code point.
    public const int SurrogateLimit = 0xE000;

    // Base offset such that a supplementary code point's lead surrogate is
    // SurrogateLeadOffset + (c >> 10).
    public const int SurrogateLeadOffset = 0xD7C0;

    // The number of supplementary code points that share one lead surrogate (a "lead block").
    public const int SupplementaryBlockSize = 0x400;

    // Mask for a supplementary code point's offset within its lead surrogate's block.
    public const int SupplementaryBlockMask = 0x3FF;

    // The lead surrogate (ICU's U16_LEAD) for a supplementary code point.
    public static int LeadSurrogate(int codePoint)
    {
        return SurrogateLeadOffset + (codePoint >> 10);
    }
}
