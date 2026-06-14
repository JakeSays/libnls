using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Normalization;

// The build orchestration, ported from n2builder.cpp's processData. Builds composition lists, fully
// decomposes mappings, classifies each Norm, lays out the extra data and the index thresholds,
// writes the norm16 trie (including algorithmic Hangul and the lead-surrogate worst-case
// optimization), and serializes the trie. Leaves _indexes, _extraData and _norm16TrieBytes ready
// for the container.
internal sealed partial class NormalizationDataBuilder
{
    private const int Inert = Norm16Constants.Inert;

    private void ProcessData()
    {
        // Build composition lists before recursive decomposition, while the raw pair-wise mappings
        // are still present.
        var compositionBuilder = new CompositionBuilder(_norms);
        _norms.EnumRanges(compositionBuilder.RangeHandler);

        // Recursively decompose all mappings.
        var decomposer = new Decomposer(_norms);
        do
        {
            decomposer.DidDecompose = false;
            _norms.EnumRanges(decomposer.RangeHandler);
        }
        while (decomposer.DidDecompose);

        // Classify each Norm (order-independent).
        foreach (var norm in _norms.AllNorms)
        {
            PostProcess(norm);
        }

        // Write mappings and composition lists into the per-type extra-data segments.
        var extra = new ExtraDataBuilder(_norms, optimizeFast: false);
        _norms.EnumRanges(extra.RangeHandler);

        _extraData = [];
        _extraData.AddRange(extra.YesYesCompositions);
        _indexes[NormalizationIndex.MinYesNo] = _extraData.Count * 2;
        _extraData.AddRange(extra.YesNoMappingsAndCompositions);
        _indexes[NormalizationIndex.MinYesNoMappingsOnly] = _extraData.Count * 2;
        _extraData.AddRange(extra.YesNoMappingsOnly);
        _indexes[NormalizationIndex.MinNoNo] = _extraData.Count * 2;
        _extraData.AddRange(extra.NoNoMappingsCompYes);
        _indexes[NormalizationIndex.MinNoNoCompBoundaryBefore] = _extraData.Count * 2;
        _extraData.AddRange(extra.NoNoMappingsCompBoundaryBefore);
        _indexes[NormalizationIndex.MinNoNoCompNoMaybeCc] = _extraData.Count * 2;
        _extraData.AddRange(extra.NoNoMappingsCompNoMaybeCc);
        _indexes[NormalizationIndex.MinNoNoEmpty] = _extraData.Count * 2;
        _extraData.AddRange(extra.NoNoMappingsEmpty);
        _indexes[NormalizationIndex.LimitNoNo] = _extraData.Count * 2;

        var maybeDataLength = extra.MaybeNoMappingsOnly.Count
            + extra.MaybeNoMappingsAndCompositions.Count
            + extra.MaybeYesCompositions.Count;
        // Adjust minMaybeNo down to 8-align it, so NO_NO_DELTA bits 2..1 can be used without
        // subtracting the center.
        var minMaybeNo = (Norm16Constants.MinNormalMaybeYes - maybeDataLength * 2) & ~7;

        var index = minMaybeNo;
        _indexes[NormalizationIndex.MinMaybeNo] = index;
        _extraData.AddRange(extra.MaybeNoMappingsOnly);
        index += extra.MaybeNoMappingsOnly.Count * 2;
        _indexes[NormalizationIndex.MinMaybeNoCombinesFwd] = index;
        _extraData.AddRange(extra.MaybeNoMappingsAndCompositions);
        index += extra.MaybeNoMappingsAndCompositions.Count * 2;
        _indexes[NormalizationIndex.MinMaybeYes] = index;
        _extraData.AddRange(extra.MaybeYesCompositions);

        // Pad to even length for 4-byte alignment of the following data.
        if ((_extraData.Count & 1) != 0)
        {
            _extraData.Add(0);
        }

        var minNoNoDelta = GetMinNoNoDelta();
        if (_indexes[NormalizationIndex.LimitNoNo] > minNoNoDelta)
        {
            throw new InvalidOperationException(
                "data structure overflow, too much mapping composition data");
        }

        // writeNorm16 and setHangulData reduce these as needed.
        _indexes[NormalizationIndex.MinDecompNoCp] = Unicode.CodePointLimit;
        _indexes[NormalizationIndex.MinCompNoMaybeCp] = Unicode.CodePointLimit;
        _indexes[NormalizationIndex.MinLcccCp] = Unicode.CodePointLimit;

        var trie = new CodePointTrieBuilder(Inert, Inert);
        _norms.EnumRanges((start, end, norm) => WriteNorm16(trie, start, end, norm));
        SetHangulData(trie);

        OptimizeLeadSurrogates(trie);

        // Move the supplementary minimum code points to their lead surrogates so the UTF-16 quick
        // check loops can break on code units.
        AdjustSupplementaryMin(NormalizationIndex.MinDecompNoCp);
        AdjustSupplementaryMin(NormalizationIndex.MinCompNoMaybeCp);
        AdjustSupplementaryMin(NormalizationIndex.MinLcccCp);

        _norm16TrieBytes = trie.Build();

        var offset = NormalizationIndex.Count * 4;
        _indexes[NormalizationIndex.NormTrieOffset] = offset;
        offset += _norm16TrieBytes.Length;
        _indexes[NormalizationIndex.ExtraDataOffset] = offset;
        offset += _extraData.Count * 2;
        _indexes[NormalizationIndex.SmallFcdOffset] = offset;
        offset += _smallFcd.Length;
        var totalSize = offset;
        for (var i = NormalizationIndex.Reserved3Offset; i <= NormalizationIndex.TotalSize; ++i)
        {
            _indexes[i] = totalSize;
        }
    }

    private void AdjustSupplementaryMin(int indexSlot)
    {
        var minCp = _indexes[indexSlot];
        if (minCp >= Unicode.SupplementaryMin)
        {
            _indexes[indexSlot] = Unicode.LeadSurrogate(minCp);
        }
    }

    // For each lead surrogate, set its trie value to the "worst" norm16 of any code point in its
    // supplementary block, so UTF-16 quick checks can examine only code units.
    private void OptimizeLeadSurrogates(CodePointTrieBuilder trie)
    {
        // Surrogate code points must be inert (the input rejects data for them).
        var end = trie.GetRange(Unicode.LeadSurrogateMin, out var value);
        if (value != Inert || end < Unicode.TrailSurrogateMax)
        {
            throw new InvalidOperationException(
                $"not all surrogate code points are inert: U+D800..U+{end:X4}={value:X}");
        }

        uint maxNorm16 = 0;
        // ANDing yields 0 bits where any value has a 0 — used for worst-case HAS_COMP_BOUNDARY_AFTER.
        uint andedNorm16 = 0;
        end = 0;
        for (var start = Unicode.SupplementaryMin;;)
        {
            if (start > end)
            {
                end = trie.GetRange(start, out value);
                if (end < 0)
                {
                    break;
                }
            }
            if ((start & Unicode.SupplementaryBlockMask) == 0)
            {
                // Data for a new lead surrogate.
                maxNorm16 = value;
                andedNorm16 = value;
            }
            else
            {
                if (value > maxNorm16)
                {
                    maxNorm16 = value;
                }
                andedNorm16 &= value;
            }
            // Intersect each range with the code points for one lead surrogate.
            var leadEnd = start | Unicode.SupplementaryBlockMask;
            if (leadEnd <= end)
            {
                if (maxNorm16 >= (uint)_indexes[NormalizationIndex.LimitNoNo])
                {
                    // Pin to the "worst" noNo value if it landed in less-bad maybeYes or ccc!=0,
                    // so it does not stay in the inner decomposition quick-check loop.
                    maxNorm16 = (uint)_indexes[NormalizationIndex.LimitNoNo];
                }
                maxNorm16 = (maxNorm16 & ~(uint)Norm16Constants.HasCompBoundaryAfter)
                    | (andedNorm16 & Norm16Constants.HasCompBoundaryAfter);
                if (maxNorm16 != Inert)
                {
                    trie.Set(Unicode.LeadSurrogate(start), maxNorm16);
                }
                if (value == Inert)
                {
                    // Skip inert supplementary blocks for several lead surrogates at once.
                    start = (end + 1) & ~Unicode.SupplementaryBlockMask;
                }
                else
                {
                    start = leadEnd + 1;
                }
            }
            else
            {
                start = end + 1;
            }
        }
    }
}
