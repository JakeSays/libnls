namespace NlsDataGenerator.IcuFormat;

// Open-addressing hash table for finding a mixed-value block (or an all-same block) anywhere in the
// already-compacted data or index. Ported from umutablecptrie.cpp. ICU templates this over 16- and
// 32-bit arrays; the UCPTrie data is 32-bit and the index is held as 32-bit during compaction, so
// here everything is uint. Each table entry packs a partial hash in the high bits and a data index
// (+1, so 0 means empty) in the low bits.
internal sealed class MixedBlocks
{
    private uint[] _table = [];
    private int _length;
    private int _shift;
    private uint _mask;
    private int _blockLength;

    public void Init(int maxLength, int newBlockLength)
    {
        // Entries store the data index + 1, so the table size and mask are chosen by the highest
        // index that can occur.
        var maxDataIndex = maxLength - newBlockLength + 1;
        int newLength;
        if (maxDataIndex <= 0xFFF)
        {
            newLength = 6007;
            _shift = 12;
            _mask = 0xFFF;
        }
        else if (maxDataIndex <= 0x7FFF)
        {
            newLength = 50021;
            _shift = 15;
            _mask = 0x7FFF;
        }
        else if (maxDataIndex <= 0x1FFFF)
        {
            newLength = 200003;
            _shift = 17;
            _mask = 0x1FFFF;
        }
        else
        {
            newLength = 1500007;
            _shift = 21;
            _mask = 0x1FFFFF;
        }

        if (newLength > _table.Length)
        {
            _table = new uint[newLength];
        }
        else
        {
            Array.Clear(_table, 0, newLength);
        }
        _length = newLength;
        _blockLength = newBlockLength;
    }

    // Adds the blocks in data[minStart..newDataLength) that were not already present.
    public void Extend(uint[] data, int minStart, int prevDataLength, int newDataLength)
    {
        var start = prevDataLength - _blockLength;
        if (start >= minStart)
        {
            // Skip the last block added previously.
            start++;
        }
        else
        {
            start = minStart;
        }

        for (var end = newDataLength - _blockLength; start <= end; start++)
        {
            var hashCode = MakeHashCode(data, start);
            AddEntry(data, start, hashCode, start);
        }
    }

    public int FindBlock(uint[] data, uint[] blockData, int blockStart)
    {
        var hashCode = MakeHashCode(blockData, blockStart);
        var entryIndex = FindEntry(data, blockData, blockStart, hashCode);
        if (entryIndex >= 0)
        {
            return (int)(_table[entryIndex] & _mask) - 1;
        }
        return -1;
    }

    public int FindAllSameBlock(uint[] data, uint blockValue)
    {
        var hashCode = MakeHashCodeValue(blockValue);
        var entryIndex = FindEntryValue(data, blockValue, hashCode);
        if (entryIndex >= 0)
        {
            return (int)(_table[entryIndex] & _mask) - 1;
        }
        return -1;
    }

    private uint MakeHashCode(uint[] blockData, int blockStart)
    {
        var blockLimit = blockStart + _blockLength;
        var hashCode = blockData[blockStart++];
        do
        {
            hashCode = 37 * hashCode + blockData[blockStart++];
        }
        while (blockStart < blockLimit);
        return hashCode;
    }

    private uint MakeHashCodeValue(uint blockValue)
    {
        var hashCode = blockValue;
        for (var i = 1; i < _blockLength; i++)
        {
            hashCode = 37 * hashCode + blockValue;
        }
        return hashCode;
    }

    private void AddEntry(uint[] data, int blockStart, uint hashCode, int dataIndex)
    {
        var entryIndex = FindEntry(data, data, blockStart, hashCode);
        if (entryIndex < 0)
        {
            _table[~entryIndex] = (hashCode << _shift) | (uint)(dataIndex + 1);
        }
    }

    private int FindEntry(uint[] data, uint[] blockData, int blockStart, uint hashCode)
    {
        var shiftedHashCode = hashCode << _shift;
        var initialEntryIndex = (int)(hashCode % (uint)(_length - 1)) + 1;
        for (var entryIndex = initialEntryIndex; ;)
        {
            var entry = _table[entryIndex];
            if (entry == 0)
            {
                return ~entryIndex;
            }
            if ((entry & ~_mask) == shiftedHashCode)
            {
                var dataIndex = (int)(entry & _mask) - 1;
                if (EqualBlocks(data, dataIndex, blockData, blockStart, _blockLength))
                {
                    return entryIndex;
                }
            }
            entryIndex = NextIndex(initialEntryIndex, entryIndex);
        }
    }

    private int FindEntryValue(uint[] data, uint blockValue, uint hashCode)
    {
        var shiftedHashCode = hashCode << _shift;
        var initialEntryIndex = (int)(hashCode % (uint)(_length - 1)) + 1;
        for (var entryIndex = initialEntryIndex; ;)
        {
            var entry = _table[entryIndex];
            if (entry == 0)
            {
                return ~entryIndex;
            }
            if ((entry & ~_mask) == shiftedHashCode)
            {
                var dataIndex = (int)(entry & _mask) - 1;
                if (AllValuesSameAs(data, dataIndex, _blockLength, blockValue))
                {
                    return entryIndex;
                }
            }
            entryIndex = NextIndex(initialEntryIndex, entryIndex);
        }
    }

    private int NextIndex(int initialEntryIndex, int entryIndex)
    {
        return (entryIndex + initialEntryIndex) % _length;
    }

    private static bool EqualBlocks(uint[] a, int aStart, uint[] b, int bStart, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (a[aStart + i] != b[bStart + i])
            {
                return false;
            }
        }
        return true;
    }

    private static bool AllValuesSameAs(uint[] data, int start, int length, uint value)
    {
        for (var i = 0; i < length; i++)
        {
            if (data[start + i] != value)
            {
                return false;
            }
        }
        return true;
    }
}
