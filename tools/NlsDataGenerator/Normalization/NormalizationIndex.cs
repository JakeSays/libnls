namespace NlsDataGenerator.Normalization;

// Offsets into the .nrm indexes[] table (ICU's Normalizer2Impl IX_* enum). The first entries are
// byte offsets to the trie / extra data / small-FCD / total size; the rest are code-point and
// norm16 thresholds the runtime uses for quick checks.
internal static class NormalizationIndex
{
    public const int NormTrieOffset = 0;
    public const int ExtraDataOffset = 1;
    public const int SmallFcdOffset = 2;
    public const int Reserved3Offset = 3;
    public const int Reserved4Offset = 4;
    public const int Reserved5Offset = 5;
    public const int Reserved6Offset = 6;
    public const int TotalSize = 7;

    public const int MinDecompNoCp = 8;
    public const int MinCompNoMaybeCp = 9;

    public const int MinYesNo = 10;
    public const int MinNoNo = 11;
    public const int LimitNoNo = 12;
    public const int MinMaybeYes = 13;

    public const int MinYesNoMappingsOnly = 14;
    public const int MinNoNoCompBoundaryBefore = 15;
    public const int MinNoNoCompNoMaybeCc = 16;
    public const int MinNoNoEmpty = 17;

    public const int MinLcccCp = 18;
    public const int Reserved19 = 19;

    public const int MinMaybeNo = 20;
    public const int MinMaybeNoCombinesFwd = 21;

    public const int Count = 22;
}
