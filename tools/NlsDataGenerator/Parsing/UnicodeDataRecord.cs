namespace NlsDataGenerator.Parsing;

// One row of UnicodeData.txt, holding the fields the case and normalization generators consume.
// For a "First>".."Last>" range row, CodePoint is the first scalar and RangeLast the last; such
// rows carry only general category and combining class (no case or decomposition mappings).
internal sealed class UnicodeDataRecord
{
    public int CodePoint { get; }

    public int RangeLast { get; }

    public string GeneralCategory { get; }

    public int CombiningClass { get; }

    // Null for canonical decomposition; the tag text (e.g. "compat", "wide") otherwise.
    public string? DecompositionTag { get; }

    // The decomposition scalars, or an empty array when the row has no decomposition mapping.
    public int[] DecompositionMapping { get; }

    public int? SimpleUppercase { get; }

    public int? SimpleLowercase { get; }

    public int? SimpleTitlecase { get; }

    public UnicodeDataRecord(
        int codePoint,
        int rangeLast,
        string generalCategory,
        int combiningClass,
        string? decompositionTag,
        int[] decompositionMapping,
        int? simpleUppercase,
        int? simpleLowercase,
        int? simpleTitlecase)
    {
        CodePoint = codePoint;
        RangeLast = rangeLast;
        GeneralCategory = generalCategory;
        CombiningClass = combiningClass;
        DecompositionTag = decompositionTag;
        DecompositionMapping = decompositionMapping;
        SimpleUppercase = simpleUppercase;
        SimpleLowercase = simpleLowercase;
        SimpleTitlecase = simpleTitlecase;
    }

    public bool IsRange
    {
        get
        {
            return RangeLast != CodePoint;
        }
    }
}
