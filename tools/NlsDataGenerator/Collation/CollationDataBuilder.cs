using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// Low-level CollationData builder, ported from ICU's CollationDataBuilder
// (collationdatabuilder.cpp). Takes (character, CE) pairs and builds them into the runtime data
// structures: a UTrie2 of CE32 values, CE32/CE expansion arrays, and per-code-point context lists
// for prefixes and contraction suffixes. This is the non-ICU4X subset (ICU4X mode and the
// tailoring copy-from-base paths are omitted); CollationBaseDataBuilder extends it for the root.
internal partial class CollationDataBuilder
{
    // Builder-only marker bit on a BUILDER_DATA_TAG CE32 (see Collation::BUILDER_DATA_TAG).
    public const uint IsBuilderJamoCe32 = 0x100;

    // Base collation data, or null when this builder builds a base (root) itself. The root build
    // leaves this null; tailoring (Phase B) sets it.
    protected CollationData? Base = null;

    // The main lookup trie of CE32 values.
    protected Trie2Builder Trie = null!;

    // CE32 array; index 0 is reserved for CE32(U+0000).
    protected readonly List<uint> Ce32s = [];

    // CE array for expansions and OFFSET_TAG.
    protected readonly List<long> Ce64s = [];

    protected readonly List<ConditionalCE32> ConditionalCe32s = [];

    // Code points that have context (prefixes or contraction suffixes).
    protected readonly UnicodeSet ContextChars = new();

    // Serialized UCharsTrie structures for finalized contexts (UTF-16 units).
    protected readonly List<char> Contexts = [];

    protected readonly UnicodeSet UnsafeBackwardSet = new();

    protected bool Modified;
    protected bool FastLatinEnabled;

    // Returns the FCD16 value (lccc<<8 | tccc) for a code point; supplied from the normalization
    // data. Used only while building contraction contexts.
    private readonly Func<int, int> _getFcd16;

    // The decimal-digit code points (general category Nd) in ascending order with their 0..9 digit
    // values, used to set DIGIT_TAG CE32s. Supplied from the UCD.
    private readonly IReadOnlyList<(int CodePoint, int DigitValue)> _decimalDigits;

    // Bumped by ClearContexts() to invalidate every cached builtCE32 from before.
    private int _contextsEra;

    internal CollationDataBuilder(
        Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits)
    {
        _getFcd16 = getFcd16;
        _decimalDigits = decimalDigits;
        // Reserve the first CE32 for U+0000.
        Ce32s.Add(0);
    }

    public void EnableFastLatin()
    {
        FastLatinEnabled = true;
    }

    public virtual bool IsCompressibleLeadByte(uint b)
    {
        return Base!.IsCompressibleLeadByte(b);
    }

    public bool IsCompressiblePrimary(uint p)
    {
        return IsCompressibleLeadByte(p >> 24);
    }

    public bool IsAssigned(int c)
    {
        return Collator.IsAssignedCe32(Trie.Get(c));
    }

    // The three-byte primary if c maps to a single long-primary CE with no context, else 0.
    public uint GetLongPrimaryIfSingleCe(int c)
    {
        var ce32 = Trie.Get(c);
        return Collator.IsLongPrimaryCe32(ce32) ? Collator.PrimaryFromLongPrimaryCe32(ce32) : 0;
    }

    private uint GetCe32FromOffsetCe32(bool fromBase, int c, uint ce32)
    {
        var i = Collator.IndexFromCe32(ce32);
        var dataCe = fromBase ? Base!.Ces[i] : Ce64s[i];
        var p = Collator.GetThreeBytePrimaryForOffsetData(c, dataCe);
        return Collator.MakeLongPrimaryCe32(p);
    }

    // The single CE for c. Throws if c does not map to a single CE.
    public long GetSingleCe(int c)
    {
        var fromBase = false;
        var ce32 = Trie.Get(c);
        if (ce32 == Collator.FallbackCe32)
        {
            fromBase = true;
            ce32 = Base!.GetCe32(c);
        }
        while (Collator.IsSpecialCe32(ce32))
        {
            switch (Collator.TagFromCe32(ce32))
            {
                case Collator.LatinExpansionTag:
                case Collator.BuilderDataTag:
                case Collator.PrefixTag:
                case Collator.ContractionTag:
                case Collator.HangulTag:
                case Collator.LeadSurrogateTag:
                    throw new InvalidOperationException($"U+{c:X4} does not map to a single CE");
                case Collator.FallbackTag:
                case Collator.ReservedTag3:
                    throw new InvalidOperationException($"internal error resolving U+{c:X4}");
                case Collator.LongPrimaryTag:
                    return Collator.CeFromLongPrimaryCe32(ce32);
                case Collator.LongSecondaryTag:
                    return Collator.CeFromLongSecondaryCe32(ce32);
                case Collator.Expansion32Tag:
                    if (Collator.LengthFromCe32(ce32) == 1)
                    {
                        var i = Collator.IndexFromCe32(ce32);
                        ce32 = fromBase ? Base!.Ce32s[i] : Ce32s[i];
                        break;
                    }
                    throw new InvalidOperationException($"U+{c:X4} does not map to a single CE");
                case Collator.ExpansionTag:
                    if (Collator.LengthFromCe32(ce32) == 1)
                    {
                        var i = Collator.IndexFromCe32(ce32);
                        return fromBase ? Base!.Ces[i] : Ce64s[i];
                    }
                    throw new InvalidOperationException($"U+{c:X4} does not map to a single CE");
                case Collator.DigitTag:
                    ce32 = Ce32s[Collator.IndexFromCe32(ce32)];
                    break;
                case Collator.U0000Tag:
                    ce32 = fromBase ? Base!.Ce32s[0] : Ce32s[0];
                    break;
                case Collator.OffsetTag:
                    ce32 = GetCe32FromOffsetCe32(fromBase, c, ce32);
                    break;
                case Collator.ImplicitTag:
                    return Collator.UnassignedCeFromCodePoint(c);
            }
        }
        return Collator.CeFromSimpleCe32(ce32);
    }

    // Sets three-byte-primary offset CEs for start..end if worth it (>= 3 block boundaries, or a
    // boundary with >= 4 code points on each side). Returns true if an OFFSET_TAG range was used.
    public bool MaybeSetPrimaryRange(int start, int end, uint primary, int step)
    {
        var blockDelta = (end >> 5) - (start >> 5);
        if (2 <= step && step <= 0x7F
            && (blockDelta >= 3
                || (blockDelta > 0 && (start & 0x1F) <= 0x1C && (end & 0x1F) >= 3)))
        {
            var dataCe = ((long)primary << 32) | ((long)start << 8) | (uint)step;
            if (IsCompressiblePrimary(primary))
            {
                dataCe |= 0x80;
            }
            var index = AddCe(dataCe);
            if (index > Collator.MaxIndex)
            {
                throw new InvalidOperationException("collation CE index overflow");
            }
            var offsetCe32 = Collator.MakeCe32FromTagAndIndex(Collator.OffsetTag, index);
            Trie.SetRange(start, end, offsetCe32, true);
            Modified = true;
            return true;
        }
        return false;
    }

    public uint SetPrimaryRangeAndReturnNext(int start, int end, uint primary, int step)
    {
        var isCompressible = IsCompressiblePrimary(primary);
        if (MaybeSetPrimaryRange(start, end, primary, step))
        {
            return Collator.IncThreeBytePrimaryByOffset(primary, isCompressible, (end - start + 1) * step);
        }
        // Short range: set individual long-primary CE32s.
        for (;;)
        {
            Trie.Set(start, Collator.MakeLongPrimaryCe32(primary));
            ++start;
            primary = Collator.IncThreeBytePrimaryByOffset(primary, isCompressible, step);
            if (start > end)
            {
                Modified = true;
                return primary;
            }
        }
    }

    private ConditionalCE32 GetConditionalCe32(int index)
    {
        return ConditionalCe32s[index];
    }

    private ConditionalCE32 GetConditionalCe32ForCe32(uint ce32)
    {
        return GetConditionalCe32(Collator.IndexFromCe32(ce32));
    }

    private static uint MakeBuilderContextCe32(int index)
    {
        return Collator.MakeCe32FromTagAndIndex(Collator.BuilderDataTag, index);
    }

    private static bool IsBuilderContextCe32(uint ce32)
    {
        return Collator.HasCe32Tag(ce32, Collator.BuilderDataTag);
    }

    // The code point of the i-th conjoining Jamo (L, then V, then T).
    internal static int JamoCpFromIndex(int i)
    {
        if (i < Hangul.JamoLCount)
        {
            return Hangul.JamoLBase + i;
        }
        i -= Hangul.JamoLCount;
        if (i < Hangul.JamoVCount)
        {
            return Hangul.JamoVBase + i;
        }
        i -= Hangul.JamoVCount;
        return Hangul.JamoTBase + 1 + i;
    }
}
