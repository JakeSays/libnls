namespace NlsDataGenerator.IcuFormat;

// The mutable-trie getRange, ported from umutablecptrie.cpp (the filter==null path that gennorm2
// uses). Returns the end of the maximal run of equal values starting at start, and that value via
// the out parameter. Must be called before Build(), which compacts and invalidates this view.
internal sealed partial class CodePointTrieBuilder
{
    public int GetRange(int start, out uint value)
    {
        value = 0;
        if ((uint)start > Unicode.MaxCodePoint)
        {
            return -1;
        }
        if (start >= _highStart)
        {
            value = _highValue;
            return Unicode.MaxCodePoint;
        }

        uint trieValue = 0;
        var haveValue = false;
        var c = start;
        var i = c >> Shift3;
        do
        {
            if (_flags[i] == AllSame)
            {
                var trieValue2 = _index[i];
                if (haveValue)
                {
                    if (trieValue2 != trieValue)
                    {
                        return c - 1;
                    }
                }
                else
                {
                    trieValue = trieValue2;
                    value = trieValue2;
                    haveValue = true;
                }
                c = (c + SmallDataBlockLength) & ~SmallDataMask;
            }
            else
            {
                var di = (int)_index[i] + (c & SmallDataMask);
                var trieValue2 = _data[di];
                if (haveValue)
                {
                    if (trieValue2 != trieValue)
                    {
                        return c - 1;
                    }
                }
                else
                {
                    trieValue = trieValue2;
                    value = trieValue2;
                    haveValue = true;
                }
                while ((++c & SmallDataMask) != 0)
                {
                    trieValue2 = _data[++di];
                    if (trieValue2 != trieValue)
                    {
                        return c - 1;
                    }
                }
            }
            ++i;
        }
        while (c < _highStart);

        if (_highValue != value)
        {
            return c - 1;
        }
        return Unicode.MaxCodePoint;
    }
}
