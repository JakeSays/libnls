namespace NlsDataGenerator.Collation;

// A set of code points stored as sorted, disjoint, non-adjacent inclusive ranges, ported from the
// parts of ICU's UnicodeSet the collation builder uses (add/addAll/contains/containsSome/freeze and
// code-point iteration). The collator's unsafeBackwardSet and contextChars are UnicodeSets; the
// data writer later serializes this in ICU's inversion-list form.
internal sealed partial class UnicodeSet
{
    private readonly List<(int Start, int End)> _ranges = [];
    private bool _frozen;

    public bool IsEmpty => _ranges.Count == 0;

    public IReadOnlyList<(int Start, int End)> Ranges => _ranges;

    public void Add(int c)
    {
        Add(c, c);
    }

    // Adds the inclusive range [start, end], coalescing with overlapping and adjacent ranges.
    public void Add(int start, int end)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("UnicodeSet is frozen");
        }
        if (start > end)
        {
            return;
        }

        // Find the first range that could touch [start-1, end+1] and merge the run.
        var i = 0;
        while (i < _ranges.Count && _ranges[i].End + 1 < start)
        {
            ++i;
        }
        var mergedStart = start;
        var mergedEnd = end;
        var removeFrom = i;
        var removeCount = 0;
        while (i < _ranges.Count && _ranges[i].Start <= end + 1)
        {
            mergedStart = Math.Min(mergedStart, _ranges[i].Start);
            mergedEnd = Math.Max(mergedEnd, _ranges[i].End);
            ++removeCount;
            ++i;
        }
        _ranges.RemoveRange(removeFrom, removeCount);
        _ranges.Insert(removeFrom, (mergedStart, mergedEnd));
    }

    // Removes the inclusive range [start, end] from the set.
    public void Remove(int start, int end)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("UnicodeSet is frozen");
        }
        if (start > end)
        {
            return;
        }
        var result = new List<(int Start, int End)>();
        foreach (var range in _ranges)
        {
            if (range.End < start || range.Start > end)
            {
                result.Add(range);
                continue;
            }
            if (range.Start < start)
            {
                result.Add((range.Start, start - 1));
            }
            if (range.End > end)
            {
                result.Add((end + 1, range.End));
            }
        }
        _ranges.Clear();
        _ranges.AddRange(result);
    }

    // Removes every code point of another set.
    public void RemoveAll(UnicodeSet other)
    {
        foreach (var range in other.Ranges)
        {
            Remove(range.Start, range.End);
        }
    }

    // Adds every code point of the string (not a range).
    public void AddAll(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            Add(c);
        }
    }

    // Adds all code points from another set.
    public void AddAll(UnicodeSet other)
    {
        foreach (var range in other._ranges)
        {
            Add(range.Start, range.End);
        }
    }

    public bool Contains(int c)
    {
        var lo = 0;
        var hi = _ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (c < _ranges[mid].Start)
            {
                hi = mid - 1;
            }
            else if (c > _ranges[mid].End)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }
        return false;
    }

    public bool ContainsSome(int start, int end)
    {
        foreach (var range in _ranges)
        {
            if (range.Start <= end && start <= range.End)
            {
                return true;
            }
        }
        return false;
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public void Thaw()
    {
        _frozen = false;
    }

    public IEnumerable<int> CodePoints
    {
        get
        {
            foreach (var range in _ranges)
            {
                for (var c = range.Start; c <= range.End; ++c)
                {
                    yield return c;
                }
            }
        }
    }

    // The ICU inversion list: pairs of [start, limit) boundaries terminated by 0x110000.
    public List<int> InversionList()
    {
        var list = new List<int>(_ranges.Count * 2 + 1);
        foreach (var range in _ranges)
        {
            list.Add(range.Start);
            list.Add(range.End + 1);
        }
        list.Add(Unicode.CodePointLimit);
        return list;
    }

    // Serializes the set in ICU's UnicodeSet::serialize form: a length unit (high bit set if there
    // are supplementary values), an optional BMP-length unit, then the inversion-list boundaries —
    // BMP values as one uint16, supplementary values as two (high, low).
    public ushort[] Serialize()
    {
        var list = InversionList();
        // Boundary values, excluding the terminating 0x110000.
        var valueCount = list.Count - 1;
        if (valueCount == 0)
        {
            return [0];
        }

        int bmpLength;
        int length;
        if (list[valueCount - 1] <= Unicode.MaxBmpCodePoint)
        {
            bmpLength = valueCount;
            length = valueCount;
        }
        else if (list[0] >= Unicode.SupplementaryMin)
        {
            bmpLength = 0;
            length = valueCount * 2;
        }
        else
        {
            bmpLength = 0;
            while (bmpLength < valueCount && list[bmpLength] <= Unicode.MaxBmpCodePoint)
            {
                ++bmpLength;
            }
            length = bmpLength + 2 * (valueCount - bmpLength);
        }

        if (length > 0x7FFF)
        {
            throw new InvalidOperationException("UnicodeSet too large to serialize");
        }

        var hasSupplementary = length > bmpLength;
        var dest = new ushort[length + (hasSupplementary ? 2 : 1)];
        var d = 0;
        dest[d++] = (ushort)length;
        if (hasSupplementary)
        {
            dest[0] |= 0x8000;
            dest[d++] = (ushort)bmpLength;
        }
        for (var i = 0; i < bmpLength; ++i)
        {
            dest[d++] = (ushort)list[i];
        }
        for (var i = bmpLength; i < valueCount; ++i)
        {
            dest[d++] = (ushort)(list[i] >> 16);
            dest[d++] = (ushort)list[i];
        }
        return dest;
    }
}
