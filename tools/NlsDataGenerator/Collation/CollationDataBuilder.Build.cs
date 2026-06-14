using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// Finalizing the mappings into runtime data, ported from collationdatabuilder.cpp's
// buildMappings/getJamoCE32s/setDigitTags/setLeadSurrogates (non-icu4x). Builds the contexts,
// installs the conjoining-Jamo and Hangul CE32s, tags decimal digits, gives lead surrogates their
// worst-case values, and fills the CollationData arrays. Fast-Latin and the root-specific script
// data are added by the subclass build().
internal partial class CollationDataBuilder
{
    protected void BuildMappings(CollationData data)
    {
        BuildContexts();

        var jamoCe32s = new uint[CollationData.JamoCe32sLength];
        var jamoIndex = -1;
        if (GetJamoCe32s(jamoCe32s))
        {
            jamoIndex = Ce32s.Count;
            for (var i = 0; i < CollationData.JamoCe32sLength; ++i)
            {
                Ce32s.Add(jamoCe32s[i]);
            }
            // Set HANGUL_NO_SPECIAL_JAMO on a whole Jamo-L block of 588 syllables when none of its
            // Jamo CE32s is special, so the runtime can skip per-Jamo tests.
            var isAnyJamoVtSpecial = false;
            for (var i = Hangul.JamoLCount; i < CollationData.JamoCe32sLength; ++i)
            {
                if (Collator.IsSpecialCe32(jamoCe32s[i]))
                {
                    isAnyJamoVtSpecial = true;
                    break;
                }
            }
            var c = Hangul.HangulBase;
            for (var i = 0; i < Hangul.JamoLCount; ++i)
            {
                var hangulCe32 = Collator.MakeCe32FromTagAndIndex(Collator.HangulTag, 0);
                if (!isAnyJamoVtSpecial && !Collator.IsSpecialCe32(jamoCe32s[i]))
                {
                    hangulCe32 |= Collator.HangulNoSpecialJamo;
                }
                var limit = c + Hangul.JamoVtCount;
                Trie.SetRange(c, limit - 1, hangulCe32, true);
                c = limit;
            }
        }
        else
        {
            // A tailoring that does not assign conjoining Jamo: copy the Hangul CE32s from the base in
            // blocks per Jamo L (HANGUL_NO_SPECIAL_JAMO is set or not for whole blocks).
            for (var c = Hangul.HangulBase; c < Hangul.HangulLimit;)
            {
                var ce32 = Base!.GetCe32(c);
                var limit = c + Hangul.JamoVtCount;
                Trie.SetRange(c, limit - 1, ce32, true);
                c = limit;
            }
        }

        SetDigitTags();
        SetLeadSurrogates();

        // For U+0000, move its normal ce32 into ce32s[0] and set U0000_TAG.
        Ce32s[0] = Trie.Get(0);
        Trie.Set(0, Collator.MakeCe32FromTagAndIndex(Collator.U0000Tag, 0));

        // The trie is logically frozen here; serialization happens in the writer. Freezing does not
        // change lookups, so the fast-Latin pass may keep querying it.

        // Mark each lead surrogate "unsafe" if any of its 1024 supplementary code points is unsafe.
        var cp = Unicode.SupplementaryMin;
        for (var lead = Unicode.LeadSurrogateMin; lead < Unicode.TrailSurrogateMin;
            ++lead, cp += Unicode.SupplementaryBlockSize)
        {
            if (UnsafeBackwardSet.ContainsSome(cp, cp + Unicode.SupplementaryBlockMask))
            {
                UnsafeBackwardSet.Add(lead);
            }
        }
        UnsafeBackwardSet.Freeze();

        data.Trie = Trie;
        data.Ce32s = [.. Ce32s];
        data.Ces = [.. Ce64s];
        data.Contexts = [.. Contexts];
        data.Ce32sLength = Ce32s.Count;
        data.CesLength = Ce64s.Count;
        data.ContextsLength = Contexts.Count;
        data.Base = Base;
        data.JamoCe32sStart = jamoIndex;
        data.UnsafeBackwardSet = UnsafeBackwardSet;
    }

    protected void BuildFastLatinTable(CollationData data)
    {
        if (!FastLatinEnabled)
        {
            return;
        }
        var builder = new CollationFastLatinBuilder();
        if (builder.ForData(data))
        {
            var table = builder.GetTable();
            if (Base?.FastLatinTable is not null
                && table.Length == Base.FastLatinTableLength
                && table.AsSpan().SequenceEqual(Base.FastLatinTable))
            {
                // Same fast-Latin table as the base; reuse it (tailoring only).
                table = Base.FastLatinTable;
            }
            data.FastLatinTable = table;
            data.FastLatinTableLength = table.Length;
        }
    }

    private bool GetJamoCe32s(uint[] jamoCe32s)
    {
        // The base always sets jamoCE32s; a tailoring only does so if it assigns any Jamo. For a
        // tailoring, unassigned Jamo fall back to the base — context/expansion base mappings are copied
        // (deferred until we know any Jamo is assigned) so the per-syllable fast path stays consistent.
        var anyJamoAssigned = Base is null;
        var needToCopyFromBase = false;
        for (var j = 0; j < CollationData.JamoCe32sLength; ++j)
        {
            var jamo = JamoCpFromIndex(j);
            var fromBase = false;
            var ce32 = Trie.Get(jamo);
            anyJamoAssigned |= Collator.IsAssignedCe32(ce32);
            if (ce32 == Collator.FallbackCe32)
            {
                fromBase = true;
                ce32 = Base!.GetCe32(jamo);
            }
            if (Collator.IsSpecialCe32(ce32))
            {
                switch (Collator.TagFromCe32(ce32))
                {
                    case Collator.LongPrimaryTag:
                    case Collator.LongSecondaryTag:
                    case Collator.LatinExpansionTag:
                        break;
                    case Collator.Expansion32Tag:
                    case Collator.ExpansionTag:
                    case Collator.PrefixTag:
                    case Collator.ContractionTag:
                        if (fromBase)
                        {
                            ce32 = Collator.FallbackCe32;
                            needToCopyFromBase = true;
                        }
                        break;
                    case Collator.ImplicitTag:
                        ce32 = Collator.FallbackCe32;
                        needToCopyFromBase = true;
                        break;
                    case Collator.OffsetTag:
                        ce32 = GetCe32FromOffsetCe32(fromBase, jamo, ce32);
                        break;
                    default:
                        throw new InvalidOperationException($"unexpected Jamo CE32 at U+{jamo:X4}");
                }
            }
            jamoCe32s[j] = ce32;
        }
        if (anyJamoAssigned && needToCopyFromBase)
        {
            for (var j = 0; j < CollationData.JamoCe32sLength; ++j)
            {
                if (jamoCe32s[j] == Collator.FallbackCe32)
                {
                    var jamo = JamoCpFromIndex(j);
                    jamoCe32s[j] = CopyFromBaseCe32(jamo, Base!.GetCe32(jamo), true);
                }
            }
        }
        return anyJamoAssigned;
    }

    private void SetDigitTags()
    {
        foreach (var (c, digitValue) in _decimalDigits)
        {
            var ce32 = Trie.Get(c);
            if (ce32 != Collator.FallbackCe32 && ce32 != Collator.UnassignedCe32)
            {
                var index = AddCe32(ce32);
                if (index > Collator.MaxIndex)
                {
                    throw new InvalidOperationException("collation digit index overflow");
                }
                ce32 = Collator.MakeCe32FromTagIndexAndLength(Collator.DigitTag, index, digitValue);
                Trie.Set(c, ce32);
            }
        }
    }

    private void SetLeadSurrogates()
    {
        for (var lead = Unicode.LeadSurrogateMin; lead < Unicode.TrailSurrogateMin; ++lead)
        {
            var value = -1;
            var blockStart = Unicode.SupplementaryMin
                + (lead - Unicode.LeadSurrogateMin) * Unicode.SupplementaryBlockSize;
            for (var c = blockStart; c < blockStart + Unicode.SupplementaryBlockSize; ++c)
            {
                var ce32 = Trie.Get(c);
                int mapped;
                if (ce32 == Collator.UnassignedCe32)
                {
                    mapped = (int)Collator.LeadAllUnassigned;
                }
                else if (ce32 == Collator.FallbackCe32)
                {
                    mapped = (int)Collator.LeadAllFallback;
                }
                else
                {
                    value = (int)Collator.LeadMixed;
                    break;
                }
                if (value < 0)
                {
                    value = mapped;
                }
                else if (value != mapped)
                {
                    value = (int)Collator.LeadMixed;
                    break;
                }
            }
            Trie.SetForLeadSurrogate(lead,
                Collator.MakeCe32FromTagAndIndex(Collator.LeadSurrogateTag, 0) | (uint)value);
        }
    }
}
