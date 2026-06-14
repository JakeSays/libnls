namespace NlsDataGenerator.IcuFormat;

// Ports umutablecptrie.cpp's compactIndex: condenses the fast (BMP) index, then builds and
// compacts the multi-stage index-3/index-2/index-1 tables for the small range, handling the rare
// case where data offsets exceed 16 bits (18-bit index-3 blocks with packed upper-bit words).
// All index arrays are held as uint here; stores into the 16-bit index16 are masked to 16 bits to
// match ICU's uint16 truncation (the only place it matters is the 18-bit data offsets).
internal sealed partial class CodePointTrieBuilder
{
    private const int Shift2 = 9;
    private const int Shift2Minus3 = 5;
    private const int Shift1Minus2 = 5;
    private const int BmpIndexLength = 0x10000 >> FastShift;
    private const int Index3BlockLength = 1 << Shift2Minus3;
    private const int Index2BlockLength = 1 << Shift1Minus2;
    private const int Index2Mask = Index2BlockLength - 1;
    private const int Index3EighteenBitBlockLength = Index3BlockLength + Index3BlockLength / 8;
    private const int NoIndex3NullOffset = 0x7FFF;

    private const byte Index3Null = 0;
    private const byte Index3Bmp = 1;
    private const byte Index3Bits16 = 2;
    private const byte Index3Bits18 = 3;

    private uint[] _index16 = [];

    private int CompactIndex(int fastILimit, MixedBlocks mixedBlocks)
    {
        var fastIndexLength = fastILimit >> (FastShift - Shift3);
        if ((_highStart >> FastShift) <= fastIndexLength)
        {
            // Only the linear fast index, no multi-stage tables.
            _index3NullOffset = NoIndex3NullOffset;
            return fastIndexLength;
        }

        // Condense the fast index, and look for an all-null index-3 block.
        var fastIndex = new uint[BmpIndexLength];
        var i3FirstNull = -1;
        for (int i = 0, j = 0; i < fastILimit; j++)
        {
            var i3 = _index[i];
            fastIndex[j] = i3 & 0xFFFF;
            if (i3 == (uint)_dataNullOffset)
            {
                if (i3FirstNull < 0)
                {
                    i3FirstNull = j;
                }
                else if (_index3NullOffset < 0 && (j - i3FirstNull + 1) == Index3BlockLength)
                {
                    _index3NullOffset = i3FirstNull;
                }
            }
            else
            {
                i3FirstNull = -1;
            }
            // Fill the sub-block index entries compactData skipped.
            var iNext = i + SmallDataBlocksPerBmpBlock;
            while (++i < iNext)
            {
                i3 += SmallDataBlockLength;
                _index[i] = i3;
            }
        }

        mixedBlocks.Init(fastIndexLength, Index3BlockLength);
        mixedBlocks.Extend(fastIndex, 0, 0, fastIndexLength);

        // Classify each index-3 block: null, same as a fast block, 16-bit, or 18-bit.
        var index3Capacity = 0;
        i3FirstNull = _index3NullOffset;
        var hasLongI3Blocks = false;
        var iStart = fastILimit < BmpIndexLimit ? 0 : BmpIndexLimit;
        var iLimit = _highStart >> Shift3;
        for (var i = iStart; i < iLimit;)
        {
            var j = i;
            var jLimit = i + Index3BlockLength;
            var oredI3 = 0u;
            var isNull = true;
            do
            {
                var i3 = _index[j];
                oredI3 |= i3;
                if (i3 != (uint)_dataNullOffset)
                {
                    isNull = false;
                }
            }
            while (++j < jLimit);

            if (isNull)
            {
                _flags[i] = Index3Null;
                if (i3FirstNull < 0)
                {
                    if (oredI3 <= 0xFFFF)
                    {
                        index3Capacity += Index3BlockLength;
                    }
                    else
                    {
                        index3Capacity += Index3EighteenBitBlockLength;
                        hasLongI3Blocks = true;
                    }
                    i3FirstNull = 0;
                }
            }
            else if (oredI3 <= 0xFFFF)
            {
                var n = mixedBlocks.FindBlock(fastIndex, _index, i);
                if (n >= 0)
                {
                    _flags[i] = Index3Bmp;
                    _index[i] = (uint)n;
                }
                else
                {
                    _flags[i] = Index3Bits16;
                    index3Capacity += Index3BlockLength;
                }
            }
            else
            {
                _flags[i] = Index3Bits18;
                index3Capacity += Index3EighteenBitBlockLength;
                hasLongI3Blocks = true;
            }
            i = j;
        }

        var index2Capacity = (iLimit - iStart) >> Shift2Minus3;
        var index1Length = (index2Capacity + Index2Mask) >> Shift1Minus2;

        // Index table: fast index, index-1, index-3, index-2. +1 for possible padding.
        var index16Capacity = fastIndexLength + index1Length + index3Capacity + index2Capacity + 1;
        _index16 = new uint[index16Capacity];
        Array.Copy(fastIndex, _index16, fastIndexLength);

        mixedBlocks.Init(index16Capacity, Index3BlockLength);
        var longI3Blocks = new MixedBlocks();
        if (hasLongI3Blocks)
        {
            longI3Blocks.Init(index16Capacity, Index3EighteenBitBlockLength);
        }

        // Compact the index-3 table and write an uncompacted index-2 table.
        var index2 = new uint[Unicode.CodePointLimit >> Shift2];
        var i2Length = 0;
        i3FirstNull = _index3NullOffset;
        var index3Start = fastIndexLength + index1Length;
        var indexLength = index3Start;
        for (var i = iStart; i < iLimit; i += Index3BlockLength)
        {
            int i3;
            var f = _flags[i];
            if (f == Index3Null && i3FirstNull < 0)
            {
                // First null index-3 block: write & overlap it like a normal block, then remember.
                f = _dataNullOffset <= 0xFFFF ? Index3Bits16 : Index3Bits18;
                i3FirstNull = 0;
            }

            if (f == Index3Null)
            {
                i3 = _index3NullOffset;
            }
            else if (f == Index3Bmp)
            {
                i3 = (int)_index[i];
            }
            else if (f == Index3Bits16)
            {
                var n = mixedBlocks.FindBlock(_index16, _index, i);
                if (n >= 0)
                {
                    i3 = n;
                }
                else
                {
                    if (indexLength == index3Start)
                    {
                        n = 0;
                    }
                    else
                    {
                        n = GetOverlap(_index16, indexLength, _index, i, Index3BlockLength);
                    }
                    i3 = indexLength - n;
                    var prevIndexLength = indexLength;
                    while (n < Index3BlockLength)
                    {
                        _index16[indexLength++] = _index[i + n++] & 0xFFFF;
                    }
                    mixedBlocks.Extend(_index16, index3Start, prevIndexLength, indexLength);
                    if (hasLongI3Blocks)
                    {
                        longI3Blocks.Extend(_index16, index3Start, prevIndexLength, indexLength);
                    }
                }
            }
            else
            {
                // 18-bit index-3 block: pack each group of 8 data offsets plus an upper-bits word.
                var j = i;
                var jLimit = i + Index3BlockLength;
                var k = indexLength;
                do
                {
                    k++;
                    var v = _index[j++];
                    var upperBits = (v & 0x30000) >> 2;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 4;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 6;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 8;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 10;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 12;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 14;
                    _index16[k++] = v & 0xFFFF;
                    v = _index[j++];
                    upperBits |= (v & 0x30000) >> 16;
                    _index16[k++] = v & 0xFFFF;
                    _index16[k - 9] = upperBits;
                }
                while (j < jLimit);

                var n = longI3Blocks.FindBlock(_index16, _index16, indexLength);
                if (n >= 0)
                {
                    i3 = n | 0x8000;
                }
                else
                {
                    if (indexLength == index3Start)
                    {
                        n = 0;
                    }
                    else
                    {
                        n = GetOverlap(_index16, indexLength, _index16, indexLength, Index3EighteenBitBlockLength);
                    }
                    i3 = (indexLength - n) | 0x8000;
                    var prevIndexLength = indexLength;
                    if (n > 0)
                    {
                        var start = indexLength;
                        while (n < Index3EighteenBitBlockLength)
                        {
                            _index16[indexLength++] = _index16[start + n++];
                        }
                    }
                    else
                    {
                        indexLength += Index3EighteenBitBlockLength;
                    }
                    mixedBlocks.Extend(_index16, index3Start, prevIndexLength, indexLength);
                    if (hasLongI3Blocks)
                    {
                        longI3Blocks.Extend(_index16, index3Start, prevIndexLength, indexLength);
                    }
                }
            }

            if (_index3NullOffset < 0 && i3FirstNull >= 0)
            {
                _index3NullOffset = i3;
            }
            index2[i2Length++] = (uint)i3;
        }

        if (_index3NullOffset < 0)
        {
            _index3NullOffset = NoIndex3NullOffset;
        }
        if (indexLength >= (NoIndex3NullOffset + Index3BlockLength))
        {
            throw new InvalidOperationException("UCPTrie index-3 offsets exceed 15 bits");
        }

        // Compact the index-2 table and write the index-1 table.
        var blockLength = Index2BlockLength;
        var i1 = fastIndexLength;
        for (var i = 0; i < i2Length; i += blockLength)
        {
            int n;
            if ((i2Length - i) >= blockLength)
            {
                n = mixedBlocks.FindBlock(_index16, index2, i);
            }
            else
            {
                // highStart is inside the last index-2 block; shorten it.
                blockLength = i2Length - i;
                n = FindSameBlock(_index16, index3Start, indexLength, index2, i, blockLength);
            }

            int i2;
            if (n >= 0)
            {
                i2 = n;
            }
            else
            {
                if (indexLength == index3Start)
                {
                    n = 0;
                }
                else
                {
                    n = GetOverlap(_index16, indexLength, index2, i, blockLength);
                }
                i2 = indexLength - n;
                var prevIndexLength = indexLength;
                while (n < blockLength)
                {
                    _index16[indexLength++] = index2[i + n++];
                }
                mixedBlocks.Extend(_index16, index3Start, prevIndexLength, indexLength);
            }
            _index16[i1++] = (uint)i2;
        }

        return indexLength;
    }

    // Linear search for a block in p[pStart..length) matching q[qStart..qStart+blockLength).
    private static int FindSameBlock(uint[] p, int pStart, int length, uint[] q, int qStart, int blockLength)
    {
        length -= blockLength;
        while (pStart <= length)
        {
            if (EqualBlocks(p, pStart, q, qStart, blockLength))
            {
                return pStart;
            }
            pStart++;
        }
        return -1;
    }
}
