namespace NlsDataGenerator.IcuFormat;

// Compaction support for the UCPTrie builder: the build-time value lookup, the value masking that
// narrows the 32-bit build values to the target width, and the high-start search. The block
// finders and the data/index compaction that consume these follow.
internal sealed partial class CodePointTrieBuilder
{
    public uint Get(int codePoint)
    {
        if ((uint)codePoint > Unicode.MaxCodePoint)
        {
            return _errorValue;
        }
        if (codePoint >= _highStart)
        {
            return _highValue;
        }

        var i = codePoint >> Shift3;
        if (_flags[i] == AllSame)
        {
            return _index[i];
        }
        return _data[_index[i] + (codePoint & SmallDataMask)];
    }

    // Narrows every value to the target width before compaction (the mutable trie always holds
    // 32-bit values).
    private void MaskValues(uint mask)
    {
        _initialValue &= mask;
        _errorValue &= mask;
        _highValue &= mask;

        var iLimit = _highStart >> Shift3;
        for (var i = 0; i < iLimit; i++)
        {
            if (_flags[i] == AllSame)
            {
                _index[i] &= mask;
            }
        }
        for (var i = 0; i < _dataLength; i++)
        {
            _data[i] &= mask;
        }
    }

    // Finds the start of the trailing run that all maps to highValue; indexes above it are omitted.
    private int FindHighStart()
    {
        var i = _highStart >> Shift3;
        while (i > 0)
        {
            bool match;
            if (_flags[--i] == AllSame)
            {
                match = _index[i] == _highValue;
            }
            else
            {
                var block = (int)_index[i];
                match = true;
                for (var j = 0; j < SmallDataBlockLength; j++)
                {
                    if (_data[block + j] != _highValue)
                    {
                        match = false;
                        break;
                    }
                }
            }
            if (!match)
            {
                return (i + 1) << Shift3;
            }
        }
        return 0;
    }

    // Finds whole data blocks that are entirely one value and shares them, choosing the most-used
    // value for the null block. Returns an upper bound for the compacted data length.
    private int CompactWholeDataBlocks(int fastILimit, AllSameBlocks allSameBlocks)
    {
        // ASCII is stored linearly; plus room for a small null block, special values, and padding.
        var newDataCapacity = Unicode.AsciiLimit + SmallDataBlockLength + 4;
        var iLimit = _highStart >> Shift3;
        var blockLength = FastDataBlockLength;
        var inc = SmallDataBlocksPerBmpBlock;
        for (var i = 0; i < iLimit; i += inc)
        {
            if (i == fastILimit)
            {
                blockLength = SmallDataBlockLength;
                inc = 1;
            }

            var value = _index[i];
            if (_flags[i] == Mixed)
            {
                // Really mixed, or all the same value?
                var block = (int)value;
                value = _data[block];
                if (AllValuesSameAs(_data, block + 1, blockLength - 1, value))
                {
                    _flags[i] = AllSame;
                    _index[i] = value;
                }
                else
                {
                    newDataCapacity += blockLength;
                    continue;
                }
            }
            else if (inc > 1)
            {
                // Do all of the fast block's all-same parts have the same value?
                var allSame = true;
                var nextI = i + inc;
                for (var j = i + 1; j < nextI; j++)
                {
                    if (_index[j] != value)
                    {
                        allSame = false;
                        break;
                    }
                }
                if (!allSame)
                {
                    GetDataBlock(i);
                    newDataCapacity += blockLength;
                    continue;
                }
            }

            var other = allSameBlocks.FindOrAdd(i, inc, value);
            if (other == AllSameBlocks.Overflow)
            {
                // The fixed table overflowed; do a slow scan for a duplicate block.
                var jInc = SmallDataBlocksPerBmpBlock;
                for (var j = 0; ; j += jInc)
                {
                    if (j == i)
                    {
                        allSameBlocks.Add(i, inc, value);
                        break;
                    }
                    if (j == fastILimit)
                    {
                        jInc = 1;
                    }
                    if (_flags[j] == AllSame && _index[j] == value)
                    {
                        allSameBlocks.Add(j, jInc + inc, value);
                        other = j;
                        break;
                    }
                }
            }

            if (other >= 0)
            {
                _flags[i] = SameAs;
                _index[i] = (uint)other;
            }
            else
            {
                newDataCapacity += blockLength;
            }
        }
        return newDataCapacity;
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

    private const int AsciiIndexLimit = Unicode.AsciiLimit >> Shift3;

    // Removes duplicate data blocks and overlaps each new block with the previously-written data as
    // much as possible, writing the result into newData. Returns the compacted data length.
    private int CompactData(int fastILimit, uint[] newData, int newDataCapacity, int dataNullIndex, MixedBlocks mixedBlocks)
    {
        // The linear ASCII data was already copied into newData; record its block offsets.
        var newDataLength = 0;
        for (var i = 0; newDataLength < Unicode.AsciiLimit; newDataLength += FastDataBlockLength, i += SmallDataBlocksPerBmpBlock)
        {
            _index[i] = (uint)newDataLength;
        }

        var blockLength = FastDataBlockLength;
        mixedBlocks.Init(newDataCapacity, blockLength);
        mixedBlocks.Extend(newData, 0, 0, newDataLength);

        var iLimit = _highStart >> Shift3;
        var inc = SmallDataBlocksPerBmpBlock;
        var fastLength = 0;
        for (var i = AsciiIndexLimit; i < iLimit; i += inc)
        {
            if (i == fastILimit)
            {
                blockLength = SmallDataBlockLength;
                inc = 1;
                fastLength = newDataLength;
                mixedBlocks.Init(newDataCapacity, blockLength);
                mixedBlocks.Extend(newData, 0, 0, newDataLength);
            }

            if (_flags[i] == AllSame)
            {
                var value = _index[i];
                var n = mixedBlocks.FindAllSameBlock(newData, value);
                // If the null data block matches the start of a fast block that is not entirely
                // this value, keep looking: a short null block there would make getRange() assume
                // the whole fast block is null.
                while (n >= 0 && i == dataNullIndex && i >= fastILimit && n < fastLength &&
                       IsStartOfSomeFastBlock((uint)n, _index, fastILimit))
                {
                    n = FindAllSameBlock(newData, n + 1, newDataLength, value, blockLength);
                }
                if (n >= 0)
                {
                    _index[i] = (uint)n;
                }
                else
                {
                    n = GetAllSameOverlap(newData, newDataLength, value, blockLength);
                    _index[i] = (uint)(newDataLength - n);
                    var prevDataLength = newDataLength;
                    while (n < blockLength)
                    {
                        newData[newDataLength++] = value;
                        n++;
                    }
                    mixedBlocks.Extend(newData, 0, prevDataLength, newDataLength);
                }
            }
            else if (_flags[i] == Mixed)
            {
                var blockOffset = (int)_index[i];
                var n = mixedBlocks.FindBlock(newData, _data, blockOffset);
                if (n >= 0)
                {
                    _index[i] = (uint)n;
                }
                else
                {
                    n = GetOverlap(newData, newDataLength, _data, blockOffset, blockLength);
                    _index[i] = (uint)(newDataLength - n);
                    var prevDataLength = newDataLength;
                    while (n < blockLength)
                    {
                        newData[newDataLength++] = _data[blockOffset + n];
                        n++;
                    }
                    mixedBlocks.Extend(newData, 0, prevDataLength, newDataLength);
                }
            }
            else
            {
                // SAME_AS: point at the block this one was found to duplicate.
                var j = (int)_index[i];
                _index[i] = _index[j];
            }
        }

        return newDataLength;
    }

    private static bool IsStartOfSomeFastBlock(uint dataOffset, uint[] index, int fastILimit)
    {
        for (var i = 0; i < fastILimit; i += SmallDataBlocksPerBmpBlock)
        {
            if (index[i] == dataOffset)
            {
                return true;
            }
        }
        return false;
    }

    private static int FindAllSameBlock(uint[] p, int start, int limit, uint value, int blockLength)
    {
        limit -= blockLength;
        for (var block = start; block <= limit; block++)
        {
            if (p[block] == value)
            {
                for (var i = 1; ; i++)
                {
                    if (i == blockLength)
                    {
                        return block;
                    }
                    if (p[block + i] != value)
                    {
                        block += i;
                        break;
                    }
                }
            }
        }
        return -1;
    }

    private static int GetAllSameOverlap(uint[] p, int length, uint value, int blockLength)
    {
        var min = length - (blockLength - 1);
        var i = length;
        while (min < i && p[i - 1] == value)
        {
            i--;
        }
        return length - i;
    }

    private static int GetOverlap(uint[] p, int length, uint[] q, int qStart, int blockLength)
    {
        var overlap = blockLength - 1;
        while (overlap > 0 && !EqualBlocks(p, length - overlap, q, qStart, overlap))
        {
            overlap--;
        }
        return overlap;
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
}
