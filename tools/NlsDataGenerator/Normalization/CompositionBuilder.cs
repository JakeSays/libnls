namespace NlsDataGenerator.Normalization;

// Builds the composition lists, ported from ICU's CompositionBuilder (norms.cpp). Run as an
// EnumRanges handler: for each round-trip mapping (lead + trail), it records that the trail
// combines backward and inserts the (trail, composite) pair into the lead's sorted composition
// list.
internal sealed class CompositionBuilder
{
    private readonly CodePointDataTable _norms;

    public CompositionBuilder(CodePointDataTable norms)
    {
        _norms = norms;
    }

    public void RangeHandler(int start, int end, CodePointData norm)
    {
        if (norm.Kind != MappingKind.RoundTrip)
        {
            return;
        }
        if (start != end)
        {
            throw new InvalidOperationException(
                $"same round-trip mapping for more than 1 code point U+{start:X4}..U+{end:X4}");
        }
        if (norm.Cc != 0)
        {
            throw new InvalidOperationException(
                $"U+{start:X4} has a round-trip mapping and ccc!=0, "
                + "not possible in Unicode normalization");
        }

        // setRoundTripMapping ensured exactly two code points.
        var mapping = norm.Mapping!;
        var lead = Utf16.CodePointAt(mapping, 0);
        var trail = Utf16.LastCodePoint(mapping);
        if (_norms.GetCc(lead) != 0)
        {
            throw new InvalidOperationException(
                $"U+{start:X4}'s round-trip mapping's starter U+{lead:X4} has ccc!=0, "
                + "not possible in Unicode normalization");
        }

        // Flag the trailing character as combining backward.
        _norms.CreateNorm(trail).CombinesBack = true;

        // Insert the (trail, composite) pair into the lead's composition list, sorted by trail.
        var leadNorm = _norms.CreateNorm(lead);
        var compositions = leadNorm.Compositions;
        var i = 0;
        if (compositions is null)
        {
            compositions = [];
            leadNorm.Compositions = compositions;
        }
        else
        {
            // Insertion sort, checking for a duplicate trail.
            for (i = 0; i < compositions.Count; ++i)
            {
                if (trail == compositions[i].Trail)
                {
                    throw new InvalidOperationException(
                        $"same round-trip mapping for more than 1 code point (e.g., U+{start:X4}) "
                        + $"to U+{lead:X4} + U+{trail:X4}");
                }
                if (trail < compositions[i].Trail)
                {
                    break;
                }
            }
        }
        compositions.Insert(i, new CompositionPair(trail, start));
    }
}
