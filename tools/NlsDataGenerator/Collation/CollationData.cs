using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Collation;

// The collation data container, ported from ICU's CollationData (collationdata.h/.cpp). A
// CollationDataBuilder fills it; the fast-Latin builder and the data writer read it. It holds the
// CE32 trie plus the CE32/CE/context arrays and the script/reorder tables, and provides the lookups
// that resolve a code point to its CE32 (and onward to a CE).
internal sealed partial class CollationData
{
    public const int ReorderReservedBeforeLatin = UcolReorderCodeFirst + 14;
    public const int ReorderReservedAfterLatin = ReorderReservedBeforeLatin + 1;
    public const int MaxNumSpecialReorderCodes = 8;
    public const int MaxNumScriptRanges = 256;
    public const int JamoCe32sLength = 19 + 21 + 27;

    public const int UcolReorderCodeFirst = 0x1000;

    // UScriptCode values used by the reorder algorithm (stable Unicode script property values).
    public const int UscriptLatin = 25;
    public const int UscriptUnknown = 103;

    // UCOL_REORDER_CODE_DEFAULT.
    public const int UcolReorderCodeDefault = -1;

    // The main lookup trie of CE32 values. Queried mutably here (freezing does not change lookups);
    // the writer serializes it.
    public Trie2Builder Trie { get; set; } = null!;

    // CE32 values; index 0 holds CE32(U+0000) for NUL-termination handling.
    public uint[] Ce32s { get; set; } = [];

    // CE values for expansions and OFFSET_TAG.
    public long[] Ces { get; set; } = [];

    // Prefix and contraction-suffix matching data (UTF-16 units).
    public char[] Contexts { get; set; } = [];

    // Base collation data, or null if this data is itself a base.
    public CollationData? Base { get; set; }

    // Offset into Ce32s of the conjoining-Jamo CE32 block, or -1 if none.
    public int JamoCe32sStart { get; set; } = -1;

    // Single-byte primary weight (xx000000) for numeric collation.
    public uint NumericPrimary { get; set; } = 0x12000000;

    public int Ce32sLength { get; set; }
    public int CesLength { get; set; }
    public int ContextsLength { get; set; }

    // 256 flags for which primary-weight lead bytes are compressible.
    public bool[] CompressibleBytes { get; set; } = [];

    // Code points unsafe for starting comparison after an identical prefix / in backward iteration.
    public UnicodeSet? UnsafeBackwardSet { get; set; }

    // Fast-Latin table for common-Latin-text comparisons.
    public ushort[]? FastLatinTable { get; set; }
    public int FastLatinTableLength { get; set; }

    // Script / reordering-group tables. scriptsIndex has length numScripts+16 and maps a script or
    // special reorder code to an entry in scriptStarts (the group's start primary, top 16 bits).
    public int NumScripts { get; set; }
    public ushort[] ScriptsIndex { get; set; } = [];
    public ushort[] ScriptStarts { get; set; } = [];
    public int ScriptStartsLength { get; set; }

    // ISO 15924 script code -> UScriptCode value, for resolving [reorder Latn Cyrl …] (root only).
    public IReadOnlyDictionary<string, int> ScriptCodeByIsoName { get; set; } =
        new Dictionary<string, int>();

    // Root collation elements (CollationRootElements data); null in a tailoring.
    public uint[]? RootElements { get; set; }
    public int RootElementsLength { get; set; }

    // The data version (the ucadata dataVersion for the root); a tailoring derives its own from this.
    public byte[] Version { get; set; } = [0, 0, 0, 0];

    // Computes the FCD16 value for a code point (lead ccc << 8 | trail ccc); set from the build-time
    // normalizer. The collation engine uses it for discontiguous-contraction matching.
    public Func<int, int>? Fcd16Provider { get; set; }

    public uint GetCe32(int c)
    {
        return Trie.Get(c);
    }

    public uint GetCe32FromSupplementary(int c)
    {
        return Trie.Get(c);
    }

    // The CE32 for one UTF-16 lead unit; for a lead surrogate this is its code-unit value (the
    // LEAD_SURROGATE_TAG mapping), distinct from its code-point value. An iterator reading text units
    // must use this so a lead starting a surrogate pair resolves to the supplementary code point.
    public uint GetCe32ForU16SingleLead(int unit)
    {
        return Trie.GetForU16SingleLead(unit);
    }

    public int GetFcd16(int c)
    {
        return Fcd16Provider!(c);
    }

    // Set on builder data: the Jamo CE32s are indirections into the builder, not part of Ce32s.
    public uint[]? JamoCe32Override { get; set; }

    // The conjoining-Jamo CE32 at the given index into the Jamo block.
    public uint JamoCe32(int index)
    {
        return JamoCe32Override is not null ? JamoCe32Override[index] : Ce32s[JamoCe32sStart + index];
    }

    public bool IsCompressibleLeadByte(uint b)
    {
        return CompressibleBytes[b];
    }

    public bool IsCompressiblePrimary(uint p)
    {
        return IsCompressibleLeadByte(p >> 24);
    }

    // The CE32 from two context words; the defaultCE32 for contraction/prefix matching.
    public static uint ReadCe32(char[] contexts, int offset)
    {
        return ((uint)contexts[offset] << 16) | contexts[offset + 1];
    }

    // Resolves an indirect special CE32 (DIGIT/LEAD_SURROGATE/U0000 tags); requires ce32 special.
    public uint GetIndirectCe32(uint ce32)
    {
        var tag = Collator.TagFromCe32(ce32);
        if (tag == Collator.DigitTag)
        {
            ce32 = Ce32s[Collator.IndexFromCe32(ce32)];
        }
        else if (tag == Collator.LeadSurrogateTag)
        {
            ce32 = Collator.UnassignedCe32;
        }
        else if (tag == Collator.U0000Tag)
        {
            ce32 = Ce32s[0];
        }
        return ce32;
    }

    public uint GetFinalCe32(uint ce32)
    {
        if (Collator.IsSpecialCe32(ce32))
        {
            ce32 = GetIndirectCe32(ce32);
        }
        return ce32;
    }

    // The single CE for a code point that maps to exactly one CE (used by the builder's reset to a
    // [first/last position]). Ported from CollationData::getSingleCE. Throws if the code point maps to
    // zero or more than one CE (contraction/prefix/expansion>1/Hangul/lead surrogate).
    public long GetSingleCe(int c)
    {
        var d = this;
        var ce32 = GetCe32(c);
        if (ce32 == Collator.FallbackCe32)
        {
            d = Base!;
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
                    throw new NotSupportedException($"getSingleCE: U+{c:X4} does not map to a single CE");
                case Collator.FallbackTag:
                case Collator.ReservedTag3:
                    throw new InvalidOperationException($"getSingleCE: unexpected CE32 tag for U+{c:X4}");
                case Collator.LongPrimaryTag:
                    return Collator.CeFromLongPrimaryCe32(ce32);
                case Collator.LongSecondaryTag:
                    return Collator.CeFromLongSecondaryCe32(ce32);
                case Collator.Expansion32Tag:
                    if (Collator.LengthFromCe32(ce32) == 1)
                    {
                        ce32 = d.Ce32s[Collator.IndexFromCe32(ce32)];
                        break;
                    }
                    throw new NotSupportedException($"getSingleCE: U+{c:X4} expands to multiple CEs");
                case Collator.ExpansionTag:
                    if (Collator.LengthFromCe32(ce32) == 1)
                    {
                        return d.Ces[Collator.IndexFromCe32(ce32)];
                    }
                    throw new NotSupportedException($"getSingleCE: U+{c:X4} expands to multiple CEs");
                case Collator.DigitTag:
                    ce32 = d.Ce32s[Collator.IndexFromCe32(ce32)];
                    break;
                case Collator.U0000Tag:
                    ce32 = d.Ce32s[0];
                    break;
                case Collator.OffsetTag:
                    return d.GetCeFromOffsetCe32(c, ce32);
                case Collator.ImplicitTag:
                    return Collator.UnassignedCeFromCodePoint(c);
            }
        }
        return Collator.CeFromSimpleCe32(ce32);
    }

    public long GetCeFromOffsetCe32(int c, uint ce32)
    {
        var dataCe = Ces[Collator.IndexFromCe32(ce32)];
        return Collator.MakeCe(Collator.GetThreeBytePrimaryForOffsetData(c, dataCe));
    }

    // The first primary for the script's reordering group, or 0 if the script is unknown.
    public uint GetFirstPrimaryForGroup(int script)
    {
        var index = GetScriptIndex(script);
        return index == 0 ? 0 : (uint)ScriptStarts[index] << 16;
    }

    // The last primary for the script's reordering group, or 0 if the script is unknown.
    public uint GetLastPrimaryForGroup(int script)
    {
        var index = GetScriptIndex(script);
        if (index == 0)
        {
            return 0;
        }
        uint limit = ScriptStarts[index + 1];
        return (limit << 16) - 1;
    }

    private int GetScriptIndex(int script)
    {
        if (script < 0)
        {
            return 0;
        }
        if (script < NumScripts)
        {
            return ScriptsIndex[script];
        }
        if (script < UcolReorderCodeFirst)
        {
            return 0;
        }
        script -= UcolReorderCodeFirst;
        if (script < MaxNumSpecialReorderCodes)
        {
            return ScriptsIndex[NumScripts + script];
        }
        return 0;
    }
}
