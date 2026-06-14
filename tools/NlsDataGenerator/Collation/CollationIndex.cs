namespace NlsDataGenerator.Collation;

// Offsets into the collation data indexes[] table (ICU's CollationDataReader IX_* enum). The first
// few are options/metadata; the rest are byte offsets (from the start of the indexes, after the
// generic header) marking the start of each data section. The base writes all of them.
internal static class CollationIndex
{
    public const int IndexesLength = 0;
    public const int Options = 1;
    public const int Reserved2 = 2;
    public const int Reserved3 = 3;
    public const int JamoCe32sStart = 4;

    public const int ReorderCodesOffset = 5;
    public const int ReorderTableOffset = 6;
    public const int TrieOffset = 7;

    public const int Reserved8Offset = 8;
    public const int CesOffset = 9;
    public const int Reserved10Offset = 10;
    public const int Ce32sOffset = 11;

    public const int RootElementsOffset = 12;
    public const int ContextsOffset = 13;
    public const int UnsafeBwdOffset = 14;
    public const int FastLatinTableOffset = 15;

    public const int ScriptsOffset = 16;
    public const int CompressibleBytesOffset = 17;
    public const int Reserved18Offset = 18;
    public const int TotalSize = 19;

    public const int Count = TotalSize + 1;
}
