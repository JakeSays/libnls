using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// The collation tailoring builder, ported from ICU's CollationBuilder (collationbuilder.cpp). It is
// the parser's Sink: each reset and relation builds a node in a doubly-linked weight graph, then
// makeTailoredCEs allocates the actual weights, closeOverComposites adds canonically-equivalent
// mappings, and finalizeCEs rewrites the temporary CEs into final ones. parseAndBuild drives the
// parser and returns the finished tailoring CollationData.
//
// This file holds the data structure and the bit-field encodings for nodes and temporary CEs; the
// methods live in the .Reset/.Relation/.Closure/.Finalize partials.
internal sealed partial class CollationBuilder : CollationRuleSink
{
    // At most 1M nodes, limited by the 20-bit node index fields.
    private const int MaxIndex = 0xfffff;

    // Node bit 6: a primary node has secondary nodes below the common secondary weight.
    private const int HasBefore2 = 0x40;

    // Node bit 5: a primary/secondary node has tertiary nodes below the common tertiary weight.
    private const int HasBefore3 = 0x20;

    // Node bit 3: a tailored node (creates a difference) vs a node with an explicit weight.
    private const int IsTailored = 8;

    private readonly BuildTimeNormalizer _nfd;
    private readonly CanonicalClosureData _canon;

    private readonly CollationData _baseData;
    private readonly CollationRootElements _rootElements;
    private uint _variableTop;

    private CollationDataBuilder _dataBuilder;
    private readonly Func<int, int> _getFcd16;
    private readonly IReadOnlyList<(int CodePoint, int DigitValue)> _decimalDigits;
    private bool _fastLatinEnabled = true;
    private readonly UnicodeSet _optimizeSet = new();

    private readonly long[] _ces = new long[Collator.MaxExpansionLength];
    private int _cesLength;

    // Indexes of nodes with root primary weights, sorted by primary (compact root-primary -> node map).
    private readonly List<int> _rootPrimaryIndexes = [];

    // The weight graph: doubly-linked lists of int64 nodes in mostly-collation order. See the class
    // comment in collationbuilder.h for the bit layout.
    private readonly List<long> _nodes = [];

    public CollationBuilder(CollationData baseData, BuildTimeNormalizer normalizer,
        CanonicalClosureData canon, Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits)
    {
        _nfd = normalizer;
        _canon = canon;
        _baseData = baseData;
        _getFcd16 = getFcd16;
        _decimalDigits = decimalDigits;
        _rootElements = new CollationRootElements(baseData.RootElements!);
        _dataBuilder = new CollationDataBuilder(getFcd16, decimalDigits);
        _dataBuilder.InitForTailoring(baseData);
    }

    public void DisableFastLatin()
    {
        _fastLatinEnabled = false;
    }

    // Node bit-field encoding (collationbuilder.h).
    private static long NodeFromWeight32(uint weight32) => (long)weight32 << 32;
    private static long NodeFromWeight16(uint weight16) => (long)weight16 << 48;
    private static long NodeFromPreviousIndex(int previous) => (long)previous << 28;
    private static long NodeFromNextIndex(int next) => (long)next << 8;
    private static long NodeFromStrength(int strength) => strength;

    private static uint Weight32FromNode(long node) => (uint)(node >> 32);
    private static uint Weight16FromNode(long node) => (uint)(node >> 48) & 0xffff;
    private static int PreviousIndexFromNode(long node) => (int)(node >> 28) & MaxIndex;
    private static int NextIndexFromNode(long node) => ((int)node >> 8) & MaxIndex;
    private static int StrengthFromNode(long node) => (int)node & 3;

    private static bool NodeHasBefore2(long node) => (node & HasBefore2) != 0;
    private static bool NodeHasBefore3(long node) => (node & HasBefore3) != 0;
    private static bool NodeHasAnyBefore(long node) => (node & (HasBefore2 | HasBefore3)) != 0;
    private static bool IsTailoredNode(long node) => (node & IsTailored) != 0;

    private static long ChangeNodePreviousIndex(long node, int previous)
    {
        return (node & unchecked((long)0xffff00000fffffff)) | NodeFromPreviousIndex(previous);
    }

    private static long ChangeNodeNextIndex(long node, int next)
    {
        return (node & unchecked((long)0xfffffffff00000ff)) | NodeFromNextIndex(next);
    }

    // Temporary-CE encoding: a CE that fits the CE32 structure while pointing at a node index and
    // strength. Secondary weights 06..45 mark a temporary CE.
    private static long TempCeFromIndexAndStrength(int index, int strength)
    {
        return 0x4040000006002000L
            + ((long)(index & 0xfe000) << 43)
            + ((long)(index & 0x1fc0) << 42)
            + ((long)(index & 0x3f) << 24)
            + ((long)strength << 8);
    }

    public static int IndexFromTempCe(long tempCe)
    {
        tempCe -= 0x4040000006002000L;
        return ((int)(tempCe >> 43) & 0xfe000)
            | ((int)(tempCe >> 42) & 0x1fc0)
            | ((int)(tempCe >> 24) & 0x3f);
    }

    private static int StrengthFromTempCe(long tempCe)
    {
        return ((int)tempCe >> 8) & 3;
    }

    public static bool IsTempCe(long ce)
    {
        var sec = (uint)ce >> 24;
        return sec is >= 6 and <= 0x45;
    }

    public static int IndexFromTempCe32(uint tempCe32)
    {
        tempCe32 -= 0x40400620;
        return ((int)(tempCe32 >> 11) & 0xfe000)
            | ((int)(tempCe32 >> 10) & 0x1fc0)
            | ((int)(tempCe32 >> 8) & 0x3f);
    }

    public static bool IsTempCe32(uint ce32)
    {
        return (ce32 & 0xff) >= 2
            && ((ce32 >> 8) & 0xff) is >= 6 and <= 0x45;
    }

    private static int CeStrength(long ce)
    {
        if (IsTempCe(ce))
        {
            return StrengthFromTempCe(ce);
        }
        if ((ce & unchecked((long)0xff00000000000000)) != 0)
        {
            return Ucol.Primary;
        }
        if (((uint)ce & 0xff000000) != 0)
        {
            return Ucol.Secondary;
        }
        return ce != 0 ? Ucol.Tertiary : Ucol.Identical;
    }
}
