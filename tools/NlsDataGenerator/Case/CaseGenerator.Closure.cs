namespace NlsDataGenerator.Case;

// Ports casepropsbuilder's case-closure and unfold building. The unfold table records, for each
// multi-character case folding, the source code points; makeCaseClosure uses it plus repeated
// analysis of simple mappings to complete every case-equivalence group, storing each code point's
// closure set in its exception props.
internal sealed partial class CaseGenerator
{
    private const int UnfoldStringWidth = 3;
    private const int UnfoldCpWidth = 2;
    private const int UnfoldWidth = UnfoldStringWidth + UnfoldCpWidth;
    private const uint UcaseDeltaMask = 0xFF80;

    // Row 0 is the header: [row count, row width, string width, 0, 0]. Rows follow.
    private List<ushort> _unfold = [0, 5, 3, 0, 0];

    // Appends a reverse-folding row: the fold string padded to the string width, then the source
    // code point padded to the code-point width.
    private void AddUnfolding(int codePoint, int[] fold)
    {
        var foldUnits = new List<ushort>();
        AppendUtf16(foldUnits, fold);
        if (foldUnits.Count > UnfoldStringWidth)
        {
            throw new InvalidOperationException("case folding too long for unfold[]");
        }

        _unfold.AddRange(foldUnits);
        for (var i = foldUnits.Count; i < UnfoldStringWidth; i++)
        {
            _unfold.Add(0);
        }

        var sourceUnits = new List<ushort>();
        AppendUtf16(sourceUnits, [codePoint]);
        _unfold.AddRange(sourceUnits);
        for (var i = sourceUnits.Count; i < UnfoldCpWidth; i++)
        {
            _unfold.Add(0);
        }
    }

    // Sorts the unfold rows and merges rows that share a fold string, concatenating their source
    // code points; writes the final row count into the header.
    private void MakeUnfoldData()
    {
        var rows = new List<ushort[]>();
        for (var i = UnfoldWidth; i < _unfold.Count; i += UnfoldWidth)
        {
            rows.Add([_unfold[i], _unfold[i + 1], _unfold[i + 2], _unfold[i + 3], _unfold[i + 4]]);
        }

        rows.Sort(CompareUnfoldRows);

        var merged = new List<ushort[]>();
        foreach (var row in rows)
        {
            if (merged.Count > 0 && SameFoldString(merged[^1], row))
            {
                MergeSourceColumns(merged[^1], row);
            }
            else
            {
                merged.Add(row);
            }
        }

        var rebuilt = new List<ushort> { (ushort)merged.Count, UnfoldWidth, UnfoldStringWidth, 0, 0 };
        foreach (var row in merged)
        {
            rebuilt.AddRange(row);
        }
        _unfold = rebuilt;
    }

    private static int CompareUnfoldRows(ushort[] a, ushort[] b)
    {
        for (var i = 0; i < UnfoldWidth; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] - b[i];
            }
        }
        return 0;
    }

    private static bool SameFoldString(ushort[] a, ushort[] b)
    {
        for (var i = 0; i < UnfoldStringWidth; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    private static void MergeSourceColumns(ushort[] into, ushort[] from)
    {
        var target = 1;
        while (target < UnfoldCpWidth && into[UnfoldStringWidth + target] != 0)
        {
            target++;
        }

        var source = 0;
        while (source < UnfoldCpWidth && from[UnfoldStringWidth + source] != 0)
        {
            if (target >= UnfoldCpWidth)
            {
                throw new InvalidOperationException("too many source code points in unfold[]");
            }
            into[UnfoldStringWidth + target] = from[UnfoldStringWidth + source];
            target++;
            source++;
        }
    }

    private void MakeCaseClosure()
    {
        // The unfold data must be finalized first: characters that fold to the same string are
        // case-equivalent (e.g. FB05 and FB06 both fold to "st").
        MakeUnfoldData();

        var rowCount = _unfold[0];
        for (var row = 0; row < rowCount; row++)
        {
            var sourceBase = (row + 1) * UnfoldWidth + UnfoldStringWidth;
            var offset = 0;
            var first = DecodeUtf16(sourceBase, ref offset);
            while (offset < UnfoldCpWidth && _unfold[sourceBase + offset] != 0)
            {
                var other = DecodeUtf16(sourceBase, ref offset);
                AddClosure(first, -1, first, other, 0);
            }
        }

        // Repeatedly analyze simple mappings until no further closure mappings are added.
        bool added;
        do
        {
            added = false;
            for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
            {
                var value = _trie.Get(codePoint);
                if (value != 0)
                {
                    added |= AddClosure(codePoint, -1, -1, codePoint, value);
                }
            }
        }
        while (added);
    }

    // Turns case-mapping relationships into the symmetric, transitive closure within depth 3:
    // prev2/prev track the two code points that mapped to c; when c does not map back to orig a
    // closure mapping c->orig is added.
    private bool AddClosure(int orig, int prev2, int prev, int c, uint value)
    {
        var someAdded = false;
        if (c != orig)
        {
            value = _trie.Get(c);
        }

        if ((value & Exception) != 0)
        {
            var props = _exceptions[(int)(value >> TempExceptionShift)];
            var mapsToOrig = c == orig;

            var destinations = new SortedSet<int>();
            if (props.SimpleUpper >= 0 && props.SimpleUpper != c)
            {
                destinations.Add(props.SimpleUpper);
            }
            if (props.SimpleLower >= 0 && props.SimpleLower != c)
            {
                destinations.Add(props.SimpleLower);
            }
            if (props.SimpleUpper != props.SimpleTitle && props.SimpleTitle >= 0 && props.SimpleTitle != c)
            {
                destinations.Add(props.SimpleTitle);
            }
            if (props.SimpleFold >= 0 && props.SimpleFold != c)
            {
                destinations.Add(props.SimpleFold);
            }
            destinations.UnionWith(props.Closure);

            foreach (var next in destinations)
            {
                if (next == orig)
                {
                    mapsToOrig = true;
                }
                else if (prev2 < 0 && next != prev)
                {
                    someAdded |= AddClosure(orig, prev, c, next, 0);
                }
            }

            if (!mapsToOrig)
            {
                AddClosureMapping(c, orig);
                return true;
            }
        }
        else if ((value & 3) > TypeNone)
        {
            var next = c + GetDelta(value);
            if (next != c)
            {
                if (prev2 < 0 && next != orig && next != prev)
                {
                    someAdded |= AddClosure(orig, prev, c, next, 0);
                }
                if (c != orig && next != orig)
                {
                    AddClosureMapping(c, orig);
                    return true;
                }
            }
        }

        return someAdded;
    }

    private void AddClosureMapping(int src, int dest)
    {
        var value = _trie.Get(src);
        if ((value & Exception) == 0)
        {
            value = MakeExcProps(src, value);
            _trie.Set(src, value);
        }
        _exceptions[(int)(value >> TempExceptionShift)].Closure.Add(dest);
    }

    // Promotes a code point with only a simple (delta) mapping to an exception so a closure set can
    // be attached, decoding the delta into the appropriate simple-mapping field.
    private uint MakeExcProps(int codePoint, uint value)
    {
        var simpleUpper = -1;
        var simpleTitle = -1;
        var simpleLower = -1;
        if ((value & 3) > TypeNone)
        {
            var next = codePoint + GetDelta(value);
            if (next != codePoint)
            {
                if ((value & 3) == TypeLower)
                {
                    simpleUpper = next;
                    simpleTitle = next;
                }
                else
                {
                    simpleLower = next;
                }
            }
        }

        var index = _exceptions.Count;
        _exceptions.Add(new ExceptionProps
        {
            CodePoint = codePoint,
            SimpleUpper = simpleUpper,
            SimpleTitle = simpleTitle,
            SimpleLower = simpleLower,
        });

        value &= ~(UgencaseExcMask | UcaseDeltaMask);
        value |= ((uint)index << TempExceptionShift) | Exception;
        return value;
    }

    private int DecodeUtf16(int baseIndex, ref int offset)
    {
        var first = _unfold[baseIndex + offset];
        offset++;
        if (first >= 0xD800 && first <= 0xDBFF)
        {
            var second = _unfold[baseIndex + offset];
            offset++;
            return 0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00);
        }
        return first;
    }

    private static int GetDelta(uint value)
    {
        return (short)value >> DeltaShift;
    }
}
