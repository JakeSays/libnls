namespace NlsDataGenerator.IcuFormat;

// Faithful port of ICU's utrie2_builder. A UTrie2 maps every code point (0..Unicode.MaxCodePoint) to a 16- or
// 32-bit value through a compact, serializable two/multi-stage index. This file is the build-time
// half: the reference-counted block machinery plus Set / SetRange. Freeze + Serialize (the
// compaction and the on-disk form the vendored reader loads) follow in the same class.
//
// The constants and algorithm match utrie2.h / utrie2_impl.h / utrie2_builder.cpp exactly; the
// only deviation is C# exceptions in place of UErrorCode for the "cannot happen" program errors.
internal sealed partial class Trie2Builder
{
    // Format constants (utrie2.h).
    private const int Shift1 = 11;
    private const int Shift2 = 5;
    private const int Shift1Minus2 = Shift1 - Shift2;
    private const int DataBlockLength = 1 << Shift2;
    private const int DataMask = DataBlockLength - 1;
    private const int Index2BlockLength = 1 << Shift1Minus2;
    private const int Index2Mask = Index2BlockLength - 1;
    private const int OmittedBmpIndex1Length = 0x10000 >> Shift1;
    private const int LscpIndex2Offset = 0x10000 >> Shift2;
    private const int LscpIndex2Length = 0x400 >> Shift2;
    private const int Index2BmpLength = LscpIndex2Offset + LscpIndex2Length;
    private const int Utf8TwoByteIndex2Length = 0x800 >> 6;
    private const int MaxIndex1Length = 0x100000 >> Shift1;
    private const int DataStartOffset = 0xC0;

    // Build-time layout constants (utrie2_impl.h).
    private const int IndexGapOffset = Index2BmpLength;
    private const int IndexGapLength = ((Utf8TwoByteIndex2Length + MaxIndex1Length) + Index2Mask) & ~Index2Mask;
    private const int Index2NullOffset = IndexGapOffset + IndexGapLength;
    private const int Index2StartOffset = Index2NullOffset + Index2BlockLength;
    private const int DataNullOffset = DataStartOffset;
    private const int DataStart = DataNullOffset + 0x40;
    private const int Data0800Offset = DataStart + 0x780;
    private const int MaxIndex2Length = (Unicode.CodePointLimit >> Shift2) + LscpIndex2Length + IndexGapLength + Index2BlockLength;
    private const int Index1Length = Unicode.CodePointLimit >> Shift1;
    private const int MaxDataLength = Unicode.CodePointLimit + 0x40 + 0x40 + 0x400;
    private const int InitialDataLength = 1 << 14;
    private const int MediumDataLength = 1 << 17;

    private readonly int[] _index1 = new int[Index1Length];
    private readonly int[] _index2 = new int[MaxIndex2Length];
    private readonly int[] _map = new int[MaxDataLength >> Shift2];
    private uint[] _data = new uint[InitialDataLength];

    private readonly uint _initialValue;
    private readonly uint _errorValue;
    private int _index2Length;
    private int _dataLength;
    private int _firstFreeBlock;
    private int _index2NullOffset;
    private int _dataNullOffset;
    private int _highStart;
    private bool _isCompacted;

    public Trie2Builder(uint initialValue, uint errorValue)
    {
        _initialValue = initialValue;
        _errorValue = errorValue;
        _highStart = Unicode.CodePointLimit;
        _firstFreeBlock = 0;
        _isCompacted = false;

        // Preallocate and reset ASCII, the bad-UTF-8 block, and the null data block.
        for (var i = 0; i < 0x80; i++)
        {
            _data[i] = initialValue;
        }
        for (var i = 0x80; i < 0xC0; i++)
        {
            _data[i] = errorValue;
        }
        for (var i = DataNullOffset; i < DataStart; i++)
        {
            _data[i] = initialValue;
        }
        _dataNullOffset = DataNullOffset;
        _dataLength = DataStart;

        // Index-2 entries and reference counts for the four ASCII data blocks.
        var block = 0;
        var dataOffset = 0;
        for (; dataOffset < 0x80; block++, dataOffset += DataBlockLength)
        {
            _index2[block] = dataOffset;
            _map[block] = 1;
        }
        // Reference counts for the bad-UTF-8 block.
        for (; dataOffset < 0xC0; block++, dataOffset += DataBlockLength)
        {
            _map[block] = 0;
        }
        // The null data block: referenced by every block except the ASCII ones, plus one so it is
        // not dropped during compaction, plus one per lead-surrogate index-2 entry.
        _map[block++] = (Unicode.CodePointLimit >> Shift2) - (0x80 >> Shift2) + 1 + LscpIndex2Length;
        dataOffset += DataBlockLength;
        for (; dataOffset < DataStart; block++, dataOffset += DataBlockLength)
        {
            _map[block] = 0;
        }

        // Remaining BMP index-2 entries point at the null data block.
        for (var i = 0x80 >> Shift2; i < Index2BmpLength; i++)
        {
            _index2[i] = DataNullOffset;
        }

        // Fill the index gap with impossible values so compaction does not overlap it.
        for (var i = 0; i < IndexGapLength; i++)
        {
            _index2[IndexGapOffset + i] = -1;
        }

        // The null index-2 block.
        for (var i = 0; i < Index2BlockLength; i++)
        {
            _index2[Index2NullOffset + i] = DataNullOffset;
        }
        _index2NullOffset = Index2NullOffset;
        _index2Length = Index2StartOffset;

        // Index-1 entries for the linear index-2 block, then the null index-2 block.
        var index2Offset = 0;
        var index1Slot = 0;
        for (; index1Slot < OmittedBmpIndex1Length; index1Slot++, index2Offset += Index2BlockLength)
        {
            _index1[index1Slot] = index2Offset;
        }
        for (; index1Slot < Index1Length; index1Slot++)
        {
            _index1[index1Slot] = Index2NullOffset;
        }

        // Preallocate data for U+0080..U+07FF so 2-byte UTF-8 compacts in 64-blocks.
        for (var c = 0x80; c < 0x800; c += DataBlockLength)
        {
            Set(c, initialValue);
        }
    }

    // Sets the value for a single code point.
    public void Set(int codePoint, uint value)
    {
        if ((uint)codePoint > Unicode.MaxCodePoint)
        {
            throw new ArgumentOutOfRangeException(nameof(codePoint));
        }

        SetValue(codePoint, true, value);
    }

    // Enumerates the maximal ascending ranges of equal value over all code points, mirroring
    // utrie2_enum (used by the collation builder's copyFrom). Ranges may differ from ICU's internal
    // block-aligned boundaries, but cover identical code points with identical values in ascending
    // order, so applying setRange per range reproduces the same destination trie.
    public void EnumRanges(Action<int, int, uint> handler)
    {
        var start = 0;
        while (start <= Unicode.MaxCodePoint)
        {
            var value = Get(start);
            var end = start;
            while (end < Unicode.MaxCodePoint && Get(end + 1) == value)
            {
                ++end;
            }
            handler(start, end, value);
            start = end + 1;
        }
    }

    // Sets the value for a lead-surrogate code unit (distinct from the code point's value).
    public void SetForLeadSurrogate(int leadSurrogate, uint value)
    {
        if (leadSurrogate < Unicode.LeadSurrogateMin || leadSurrogate > Unicode.LeadSurrogateMax)
        {
            throw new ArgumentOutOfRangeException(nameof(leadSurrogate));
        }

        SetValue(leadSurrogate, false, value);
    }

    // Sets value across [start..end]. When overwrite is false, only positions still holding the
    // initial value are changed.
    public void SetRange(int start, int end, uint value, bool overwrite)
    {
        if ((uint)start > Unicode.MaxCodePoint || (uint)end > Unicode.MaxCodePoint || start > end)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        if (_isCompacted)
        {
            throw new InvalidOperationException("trie is already compacted");
        }
        if (!overwrite && value == _initialValue)
        {
            return;
        }

        var limit = end + 1;
        if ((start & DataMask) != 0)
        {
            // Partial block at the start, up to the next block boundary.
            var block = GetDataBlock(start, true);
            var nextStart = (start + DataMask) & ~DataMask;
            if (nextStart <= limit)
            {
                FillBlock(block, start & DataMask, DataBlockLength, value, _initialValue, overwrite);
                start = nextStart;
            }
            else
            {
                FillBlock(block, start & DataMask, limit & DataMask, value, _initialValue, overwrite);
                return;
            }
        }

        // Positions in the last, partial block, then round the limit down to a block boundary.
        var rest = limit & DataMask;
        limit &= ~DataMask;

        var repeatBlock = value == _initialValue ? _dataNullOffset : -1;
        while (start < limit)
        {
            var setRepeatBlock = false;

            if (value == _initialValue && IsInNullBlock(start, true))
            {
                start += DataBlockLength;
                continue;
            }

            var i2 = GetIndex2Block(start, true);
            i2 += (start >> Shift2) & Index2Mask;
            var block = _index2[i2];
            if (IsWritableBlock(block))
            {
                if (overwrite && block >= Data0800Offset)
                {
                    // Unprotected, full-overwrite block: replace it with the repeat block.
                    setRepeatBlock = true;
                }
                else
                {
                    FillBlock(block, 0, DataBlockLength, value, _initialValue, overwrite);
                }
            }
            else if (_data[block] != value && (overwrite || block == _dataNullOffset))
            {
                setRepeatBlock = true;
            }

            if (setRepeatBlock)
            {
                if (repeatBlock >= 0)
                {
                    SetIndex2Entry(i2, repeatBlock);
                }
                else
                {
                    repeatBlock = GetDataBlock(start, true);
                    WriteBlock(repeatBlock, value);
                }
            }

            start += DataBlockLength;
        }

        if (rest > 0)
        {
            // Partial block at the end.
            var block = GetDataBlock(start, true);
            FillBlock(block, 0, rest, value, _initialValue, overwrite);
        }
    }

    private void SetValue(int codePoint, bool forLeadSurrogate, uint value)
    {
        if (_isCompacted)
        {
            throw new InvalidOperationException("trie is already compacted");
        }

        var block = GetDataBlock(codePoint, forLeadSurrogate);
        _data[block + (codePoint & DataMask)] = value;
    }

    private bool IsInNullBlock(int codePoint, bool forLeadSurrogate)
    {
        var i2 = codePoint is >= Unicode.LeadSurrogateMin and <= Unicode.LeadSurrogateMax && forLeadSurrogate
            ? (LscpIndex2Offset - (Unicode.LeadSurrogateMin >> Shift2)) + (codePoint >> Shift2)
            : _index1[codePoint >> Shift1] + ((codePoint >> Shift2) & Index2Mask);

        return _index2[i2] == _dataNullOffset;
    }

    private int AllocateIndex2Block()
    {
        var newBlock = _index2Length;
        var newTop = newBlock + Index2BlockLength;
        if (newTop > _index2.Length)
        {
            throw new InvalidOperationException("UTrie2 index-2 array overflow");
        }
        _index2Length = newTop;
        Array.Copy(_index2, _index2NullOffset, _index2, newBlock, Index2BlockLength);
        return newBlock;
    }

    private int GetIndex2Block(int codePoint, bool forLeadSurrogate)
    {
        if (codePoint is >= Unicode.LeadSurrogateMin and <= Unicode.LeadSurrogateMax && forLeadSurrogate)
        {
            return LscpIndex2Offset;
        }

        var i1 = codePoint >> Shift1;
        var i2 = _index1[i1];
        if (i2 == _index2NullOffset)
        {
            i2 = AllocateIndex2Block();
            _index1[i1] = i2;
        }
        return i2;
    }

    private int AllocateDataBlock(int copyBlock)
    {
        int newBlock;
        if (_firstFreeBlock != 0)
        {
            // Reuse the first block on the free list.
            newBlock = _firstFreeBlock;
            _firstFreeBlock = -_map[newBlock >> Shift2];
        }
        else
        {
            // Take a new block from the high end, growing the data array as needed.
            newBlock = _dataLength;
            var newTop = newBlock + DataBlockLength;
            if (newTop > _data.Length)
            {
                int capacity;
                if (_data.Length < MediumDataLength)
                {
                    capacity = MediumDataLength;
                }
                else if (_data.Length < MaxDataLength)
                {
                    capacity = MaxDataLength;
                }
                else
                {
                    throw new InvalidOperationException("UTrie2 data array overflow");
                }
                Array.Resize(ref _data, capacity);
            }
            _dataLength = newTop;
        }

        Array.Copy(_data, copyBlock, _data, newBlock, DataBlockLength);
        _map[newBlock >> Shift2] = 0;
        return newBlock;
    }

    private void ReleaseDataBlock(int block)
    {
        // Push the block onto the front of the free-block chain.
        _map[block >> Shift2] = -_firstFreeBlock;
        _firstFreeBlock = block;
    }

    private bool IsWritableBlock(int block)
    {
        return block != _dataNullOffset && _map[block >> Shift2] == 1;
    }

    private void SetIndex2Entry(int i2, int block)
    {
        // Increment first, in case block == oldBlock.
        _map[block >> Shift2]++;
        var oldBlock = _index2[i2];
        if (--_map[oldBlock >> Shift2] == 0)
        {
            ReleaseDataBlock(oldBlock);
        }
        _index2[i2] = block;
    }

    private int GetDataBlock(int codePoint, bool forLeadSurrogate)
    {
        var i2 = GetIndex2Block(codePoint, forLeadSurrogate);
        i2 += (codePoint >> Shift2) & Index2Mask;

        var oldBlock = _index2[i2];
        if (IsWritableBlock(oldBlock))
        {
            return oldBlock;
        }

        var newBlock = AllocateDataBlock(oldBlock);
        SetIndex2Entry(i2, newBlock);
        return newBlock;
    }

    private void WriteBlock(int block, uint value)
    {
        var limit = block + DataBlockLength;
        for (var i = block; i < limit; i++)
        {
            _data[i] = value;
        }
    }

    // initialValue is ignored when overwrite is true.
    private void FillBlock(int block, int start, int limit, uint value, uint initialValue, bool overwrite)
    {
        var from = block + start;
        var to = block + limit;
        if (overwrite)
        {
            for (var i = from; i < to; i++)
            {
                _data[i] = value;
            }
        }
        else
        {
            for (var i = from; i < to; i++)
            {
                if (_data[i] == initialValue)
                {
                    _data[i] = value;
                }
            }
        }
    }
}
