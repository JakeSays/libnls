namespace NlsDataGenerator.Normalization;

// The per-code-point normalization store, ported from ICU's Norms (norms.h/.cpp). gennorm2 keeps a
// CPTrie mapping each code point to an index into a Norm array (index 0 = a shared inert default);
// here a dictionary serves the same role. Code points without data resolve to the immutable inert
// default via GetNormRef.
internal sealed class CodePointDataTable
{
    private readonly Dictionary<int, CodePointData> _byCodePoint = [];
    private readonly SortedSet<int> _codePoints = [];

    // The shared inert default returned for code points with no stored data. Read-only by
    // contract — handlers only ever query it.
    private readonly CodePointData _inert = new() { Type = NormType.Inert };

    // Every stored Norm, in no particular order. Used for the order-independent post-process pass.
    public IEnumerable<CodePointData> AllNorms => _byCodePoint.Values;

    // Every (code point, data) pair that has stored data, in no particular order.
    public IEnumerable<KeyValuePair<int, CodePointData>> AllEntries => _byCodePoint;

    // Returns the existing data for c, or null if c has none.
    public CodePointData? GetNorm(int c)
    {
        return _byCodePoint.TryGetValue(c, out var norm) ? norm : null;
    }

    // Returns the existing data for c, creating a fresh entry if necessary.
    public CodePointData CreateNorm(int c)
    {
        if (_byCodePoint.TryGetValue(c, out var norm))
        {
            return norm;
        }
        norm = new CodePointData();
        _byCodePoint.Add(c, norm);
        _codePoints.Add(c);
        return norm;
    }

    // Returns the existing data for c, or the immutable inert default if c has none.
    public CodePointData GetNormRef(int c)
    {
        return _byCodePoint.TryGetValue(c, out var norm) ? norm : _inert;
    }

    public byte GetCc(int c)
    {
        return GetNormRef(c).Cc;
    }

    public bool CombinesBack(int c)
    {
        return Hangul.IsJamoV(c) || Hangul.IsJamoT(c) || GetNormRef(c).CombinesBack;
    }

    // Canonically orders the combining marks in a mapping. Returns the reordered string (or the
    // original when no reordering was needed).
    public string Reorder(string mapping, ReorderingBuffer buffer)
    {
        var i = 0;
        while (i < mapping.Length)
        {
            var c = char.ConvertToUtf32(mapping, i);
            i += char.IsHighSurrogate(mapping[i]) ? 2 : 1;
            buffer.Append(c, GetCc(c));
        }
        if (buffer.DidReorder)
        {
            return buffer.ToMappingString();
        }
        return mapping;
    }

    // True if norm composes with any trailing character whose ccc lies strictly between lowCC and
    // highCC. (highCC is int so the caller can pass 256 as an open upper bound.)
    public bool CombinesWithCcBetween(CodePointData norm, byte lowCc, int highCc)
    {
        if ((highCc - lowCc) >= 2 && norm.Compositions is not null)
        {
            foreach (var pair in norm.Compositions)
            {
                var trailCc = GetCc(pair.Trail);
                if (lowCc < trailCc && trailCc < highCc)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Enumerates every code point that has data, in ascending order, mirroring ICU's enumRanges
    // (single-code-point ranges, since each entry has a distinct index). Handlers may create new
    // entries during enumeration; those at code points above the current position are picked up as
    // enumeration advances, matching the mutable-trie walk in ICU.
    public void EnumRanges(Action<int, int, CodePointData> rangeHandler)
    {
        var start = 0;
        while (start <= Unicode.MaxCodePoint)
        {
            var view = _codePoints.GetViewBetween(start, Unicode.MaxCodePoint);
            if (view.Count == 0)
            {
                break;
            }
            var c = view.Min;
            rangeHandler(c, c, _byCodePoint[c]);
            start = c + 1;
        }
    }
}
