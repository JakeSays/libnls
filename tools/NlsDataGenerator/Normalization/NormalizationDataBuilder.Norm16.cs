using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Normalization;

// The norm16 encoding pass, ported from n2builder.cpp. Each code point's NormType plus its extra-
// data offset becomes a norm16 value written into the mutable trie; Hangul/Jamo get algorithmic
// values; the small-FCD bit table records which BMP blocks have any non-zero lccc/tccc.
internal sealed partial class NormalizationDataBuilder
{
    private int GetMinNoNoDelta()
    {
        return _indexes[NormalizationIndex.MinMaybeNo]
            - ((2 * Norm16Constants.MaxDelta + 1) << Norm16Constants.DeltaShift);
    }

    private void SetSmallFcd(int c)
    {
        var lead = c <= Unicode.MaxBmpCodePoint ? c : Unicode.LeadSurrogate(c);
        _smallFcd[lead >> 8] |= (byte)(1 << ((lead >> 5) & 7));
    }

    private void WriteNorm16(CodePointTrieBuilder trie, int start, int end, CodePointData norm)
    {
        if ((norm.LeadCc | norm.TrailCc) != 0)
        {
            for (var c = start; c <= end; ++c)
            {
                SetSmallFcd(c);
            }
        }

        int norm16;
        switch (norm.Type)
        {
            case NormType.Inert:
                norm16 = Norm16Constants.Inert;
                break;
            case NormType.YesYesCombinesFwd:
                norm16 = norm.Offset * 2;
                break;
            case NormType.YesNoCombinesFwd:
                norm16 = _indexes[NormalizationIndex.MinYesNo] + norm.Offset * 2;
                break;
            case NormType.YesNoMappingOnly:
                norm16 = _indexes[NormalizationIndex.MinYesNoMappingsOnly] + norm.Offset * 2;
                break;
            case NormType.NoNoCompYes:
                norm16 = _indexes[NormalizationIndex.MinNoNo] + norm.Offset * 2;
                break;
            case NormType.NoNoCompBoundaryBefore:
                norm16 = _indexes[NormalizationIndex.MinNoNoCompBoundaryBefore] + norm.Offset * 2;
                break;
            case NormType.NoNoCompNoMaybeCc:
                norm16 = _indexes[NormalizationIndex.MinNoNoCompNoMaybeCc] + norm.Offset * 2;
                break;
            case NormType.NoNoEmpty:
                norm16 = _indexes[NormalizationIndex.MinNoNoEmpty] + norm.Offset * 2;
                break;
            case NormType.NoNoDelta:
            {
                // Positive offset from minNoNoDelta, shifted left for the tccc bits.
                var offset = (norm.Offset + Norm16Constants.MaxDelta) << Norm16Constants.DeltaShift;
                if (norm.TrailCc == 1)
                {
                    offset |= Norm16Constants.DeltaTccc1;
                }
                else if (norm.TrailCc > 1)
                {
                    offset |= Norm16Constants.DeltaTcccGt1;
                }
                norm16 = GetMinNoNoDelta() + offset;
                break;
            }
            case NormType.MaybeNoMappingOnly:
                norm16 = _indexes[NormalizationIndex.MinMaybeNo] + norm.Offset * 2;
                break;
            case NormType.MaybeNoCombinesFwd:
                norm16 = _indexes[NormalizationIndex.MinMaybeNoCombinesFwd] + norm.Offset * 2;
                break;
            case NormType.MaybeYesCombinesFwd:
                norm16 = _indexes[NormalizationIndex.MinMaybeYes] + norm.Offset * 2;
                break;
            case NormType.MaybeYesSimple:
                norm16 = Norm16Constants.MinNormalMaybeYes + norm.Cc * 2;
                break;
            case NormType.YesYesWithCc:
                norm16 = Norm16Constants.MinYesYesWithCc - 2 + norm.Cc * 2;
                break;
            default:
                throw new InvalidOperationException($"unexpected norm type at U+{start:X4}");
        }

        if (norm.HasCompBoundaryAfter)
        {
            norm16 |= Norm16Constants.HasCompBoundaryAfter;
        }
        trie.SetRange(start, end, (uint)norm16);

        // Track the minimum code points the runtime quick-check loops need.
        var isDecompNo =
            ((int)norm.Type >= (int)NormType.YesNoCombinesFwd
                && (int)norm.Type <= (int)NormType.NoNoDelta)
            || norm.Cc != 0;
        if (isDecompNo && start < _indexes[NormalizationIndex.MinDecompNoCp])
        {
            _indexes[NormalizationIndex.MinDecompNoCp] = start;
        }
        var isCompNoMaybe = (int)norm.Type >= (int)NormType.NoNoCompYes;
        if (isCompNoMaybe && start < _indexes[NormalizationIndex.MinCompNoMaybeCp])
        {
            _indexes[NormalizationIndex.MinCompNoMaybeCp] = start;
        }
        if (norm.LeadCc != 0 && start < _indexes[NormalizationIndex.MinLcccCp])
        {
            _indexes[NormalizationIndex.MinLcccCp] = start;
        }
    }

    private void SetHangulData(CodePointTrieBuilder trie)
    {
        // None of the Hangul/Jamo code points may carry stored data.
        var ranges = new (int Start, int End)[]
        {
            (Hangul.JamoLBase, Hangul.JamoLEnd),
            (Hangul.JamoVBase, Hangul.JamoVEnd),
            // JAMO_T_BASE+1: not U+11A7.
            (Hangul.JamoTBase + 1, Hangul.JamoTEnd),
            (Hangul.HangulBase, Hangul.HangulEnd),
        };
        foreach (var range in ranges)
        {
            for (var c = range.Start; c <= range.End; ++c)
            {
                if (trie.Get(c) > Norm16Constants.Inert)
                {
                    throw new InvalidOperationException(
                        $"illegal mapping/composition/ccc data for Hangul or Jamo U+{c:X4}");
                }
            }
        }

        // Jamo V/T are maybeYes.
        if (Hangul.JamoVBase < _indexes[NormalizationIndex.MinCompNoMaybeCp])
        {
            _indexes[NormalizationIndex.MinCompNoMaybeCp] = Hangul.JamoVBase;
        }
        trie.SetRange(Hangul.JamoLBase, Hangul.JamoLEnd, Norm16Constants.JamoL);
        trie.SetRange(Hangul.JamoVBase, Hangul.JamoVEnd, Norm16Constants.JamoVt);
        trie.SetRange(Hangul.JamoTBase + 1, Hangul.JamoTEnd, Norm16Constants.JamoVt);

        // Hangul LV encoded as minYesNo; LVT as minYesNoMappingsOnly | HAS_COMP_BOUNDARY_AFTER.
        var lv = (uint)_indexes[NormalizationIndex.MinYesNo];
        var lvt = (uint)(_indexes[NormalizationIndex.MinYesNoMappingsOnly]
            | Norm16Constants.HasCompBoundaryAfter);
        if (Hangul.HangulBase < _indexes[NormalizationIndex.MinDecompNoCp])
        {
            _indexes[NormalizationIndex.MinDecompNoCp] = Hangul.HangulBase;
        }
        // Set the first LV, write all syllables as LVT, then overwrite the remaining LV syllables.
        trie.Set(Hangul.HangulBase, lv);
        trie.SetRange(Hangul.HangulBase + 1, Hangul.HangulEnd, lvt);
        var cp = Hangul.HangulBase;
        while ((cp += Hangul.JamoTCount) <= Hangul.HangulEnd)
        {
            trie.Set(cp, lv);
        }
    }
}
