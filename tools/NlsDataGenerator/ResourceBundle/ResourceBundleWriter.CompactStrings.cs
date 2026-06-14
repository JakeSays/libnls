namespace NlsDataGenerator.ResourceBundle;

// compactStringsV2(), ported from genrb (the non-pool-bundle path). Distinct string values were
// gathered during preflight; this sorts them so each is followed by its suffixes, lets a short
// string with an implicit length point into the suffix of a longer one, then re-sorts by ascending
// length (suffixes last) and writes the non-suffix strings into the 16-bit pool. Suffix strings get
// their resource word by offsetting into the string they share.
internal sealed partial class ResourceBundleWriter
{
    private void CompactStringsV2()
    {
        var array = new StringResource[_stringSet.Count];
        _stringSet.Values.CopyTo(array, 0);
        var count = array.Length;

        Array.Sort(array, CompareStringSuffixes);

        for (var i = 0; i < count;)
        {
            var res = array[i];
            res.UnitsSaved = (res.Copies - 1) * res.SixteenBitLength;
            var j = i + 1;
            for (; j < count; ++j)
            {
                var suffixRes = array[j];
                if (!res.Value.EndsWith(suffixRes.Value, StringComparison.Ordinal))
                {
                    break;
                }
                if (suffixRes.Written)
                {
                    // A pool-bundle string; this writer has none.
                }
                else if (suffixRes.CharsForLength == 0)
                {
                    suffixRes.Same = res;
                    suffixRes.SuffixOffset = res.Length - suffixRes.Length;
                    res.UnitsSaved += suffixRes.Copies * suffixRes.SixteenBitLength;
                }
                else
                {
                    // The suffix needs an explicit length, so it is written on its own.
                }
            }
            i = j;
        }

        Array.Sort(array, CompareStringLengths);

        var k = 0;
        for (; k < count && array[k].Same is null; ++k)
        {
            var res = array[k];
            if (!res.Written)
            {
                res.WriteUtf16V2(0, _pool);
            }
        }
        for (; k < count; ++k)
        {
            var res = array[k];
            if (res.Written)
            {
                continue;
            }
            var same = res.Same!;
            res.Res = same.Res + (uint)(same.CharsForLength + res.SuffixOffset);
            res.Written = true;
        }
    }

    // Sorts strings into reverse-character order so each is followed by its suffixes; equal suffixes
    // come longest-first.
    private static int CompareStringSuffixes(StringResource left, StringResource right)
    {
        var leftPos = left.Length;
        var rightPos = right.Length;
        while (leftPos > 0 && rightPos > 0)
        {
            var diff = left.Value[--leftPos] - right.Value[--rightPos];
            if (diff != 0)
            {
                return diff;
            }
        }
        return right.Length - left.Length;
    }

    // Sorts non-suffix strings before suffixes, then by ascending length, then by descending units
    // saved, then lexically — keeping as many as possible within 16-bit offset reach.
    private static int CompareStringLengths(StringResource left, StringResource right)
    {
        var diff = (left.Same is not null ? 1 : 0) - (right.Same is not null ? 1 : 0);
        if (diff != 0)
        {
            return diff;
        }
        diff = left.Length - right.Length;
        if (diff != 0)
        {
            return diff;
        }
        diff = right.UnitsSaved - left.UnitsSaved;
        if (diff != 0)
        {
            return diff;
        }
        return string.CompareOrdinal(left.Value, right.Value);
    }
}
