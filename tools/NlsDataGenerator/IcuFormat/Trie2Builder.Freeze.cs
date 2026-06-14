namespace NlsDataGenerator.IcuFormat;

// The compaction + serialization half of the UTrie2 builder, ported from utrie2_builder.cpp's
// compactData / compactIndex2 / compactTrie / utrie2_freeze. Freeze() returns the exact bytes the
// vendored utrie2_openFromSerialized reads: a UTrie2Header, the 16-bit index, then the data array.
internal sealed partial class Trie2Builder
{
    private const int IndexShift = 2;
    private const int DataGranularity = 1 << IndexShift;
    private const int CodePointsPerIndex1Entry = 1 << Shift1;
    private const int BadUtf8DataOffset = 0x80;
    private const uint Signature = 0x54726932;
    private const int Index1OffsetSerialized = Index2BmpLength + Utf8TwoByteIndex2Length;
    private const int MaxSerializedIndexLength = 0xFFFF;
    private const int MaxSerializedDataLength = 0xFFFF << IndexShift;

    // Serializes the trie, compacting first if needed.
    public byte[] Freeze(Trie2ValueBits valueBits)
    {
        if (!_isCompacted)
        {
            CompactTrie();
        }

        var highStart = _highStart;
        var allIndexesLength = highStart <= 0x10000 ? Index1OffsetSerialized : _index2Length;
        var dataMove = valueBits == Trie2ValueBits.Bits16 ? allIndexesLength : 0;

        if (allIndexesLength > MaxSerializedIndexLength ||
            (dataMove + _dataNullOffset) > 0xFFFF ||
            (dataMove + Data0800Offset) > 0xFFFF ||
            (dataMove + _dataLength) > MaxSerializedDataLength)
        {
            throw new InvalidOperationException("UTrie2 exceeds serialized index/data limits");
        }

        var index2NullOffset = highStart <= 0x10000 ? 0xFFFF : _index2NullOffset;
        var dataNullOffset = dataMove + _dataNullOffset;

        var writer = new LittleEndianWriter();

        // UTrie2Header.
        writer.WriteUInt32(Signature);
        writer.WriteUInt16((ushort)valueBits);
        writer.WriteUInt16((ushort)allIndexesLength);
        writer.WriteUInt16((ushort)(_dataLength >> IndexShift));
        writer.WriteUInt16((ushort)index2NullOffset);
        writer.WriteUInt16((ushort)dataNullOffset);
        writer.WriteUInt16((ushort)(highStart >> Shift1));

        // BMP index-2 values, shifted right by IndexShift after adding dataMove.
        for (var i = 0; i < Index2BmpLength; i++)
        {
            writer.WriteUInt16((ushort)((dataMove + _index2[i]) >> IndexShift));
        }

        // UTF-8 2-byte index-2 values, not shifted: C0..C1 point at the bad-UTF-8 block, C2..DF at
        // the corresponding BMP data blocks.
        for (var i = 0; i < (0xC2 - 0xC0); i++)
        {
            writer.WriteUInt16((ushort)(dataMove + BadUtf8DataOffset));
        }
        for (var i = (0xC2 - 0xC0); i < (0xE0 - 0xC0); i++)
        {
            writer.WriteUInt16((ushort)(dataMove + _index2[i << (6 - Shift2)]));
        }

        if (highStart > 0x10000)
        {
            var index1Length = (highStart - 0x10000) >> Shift1;
            var index2Offset = Index2BmpLength + Utf8TwoByteIndex2Length + index1Length;

            // 16-bit index-1 values for supplementary code points.
            for (var i = 0; i < index1Length; i++)
            {
                writer.WriteUInt16((ushort)_index1[OmittedBmpIndex1Length + i]);
            }

            // Supplementary index-2 values, shifted right by IndexShift after adding dataMove.
            for (var i = 0; i < (_index2Length - index2Offset); i++)
            {
                writer.WriteUInt16((ushort)((dataMove + _index2[index2Offset + i]) >> IndexShift));
            }
        }

        // Data array.
        if (valueBits == Trie2ValueBits.Bits16)
        {
            for (var i = 0; i < _dataLength; i++)
            {
                writer.WriteUInt16((ushort)_data[i]);
            }
        }
        else
        {
            for (var i = 0; i < _dataLength; i++)
            {
                writer.WriteUInt32(_data[i]);
            }
        }

        return writer.ToArray();
    }

    // Build-time value lookup for callers that reread and rewrite values before freezing.
    public uint Get(int codePoint)
    {
        return GetValue(codePoint, true);
    }

    // utrie2's UTRIE2_GET32_FROM_U16_SINGLE_LEAD: the value for one UTF-16 unit. For a lead surrogate
    // this is its code-unit value (set via SetForLeadSurrogate), which is distinct from its code-point
    // value and is what a collation iterator needs when the lead starts a surrogate pair.
    public uint GetForU16SingleLead(int unit)
    {
        return GetValue(unit, false);
    }

    // Build-time value lookup (utrie2.cpp get32), used by CompactTrie before the trie is frozen.
    private uint GetValue(int codePoint, bool fromLeadSurrogate)
    {
        var isLead = codePoint >= Unicode.LeadSurrogateMin && codePoint <= Unicode.LeadSurrogateMax;
        if (codePoint >= _highStart && (!isLead || fromLeadSurrogate))
        {
            return _data[_dataLength - DataGranularity];
        }

        int i2;
        if (isLead && fromLeadSurrogate)
        {
            i2 = (LscpIndex2Offset - (Unicode.LeadSurrogateMin >> Shift2)) + (codePoint >> Shift2);
        }
        else
        {
            i2 = _index1[codePoint >> Shift1] + ((codePoint >> Shift2) & Index2Mask);
        }

        var block = _index2[i2];
        return _data[block + (codePoint & DataMask)];
    }

    private void CompactTrie()
    {
        var highValue = GetValue(Unicode.MaxCodePoint, true);
        var highStart = FindHighStart(highValue);
        highStart = (highStart + (CodePointsPerIndex1Entry - 1)) & ~(CodePointsPerIndex1Entry - 1);
        if (highStart == Unicode.CodePointLimit)
        {
            highValue = _errorValue;
        }

        // Set _highStart only after the GetValue calls above, which read the old high value.
        _highStart = highStart;

        if (highStart < Unicode.CodePointLimit)
        {
            // Blank out [highStart..10FFFF] so its data blocks are released.
            var suppHighStart = highStart <= 0x10000 ? 0x10000 : highStart;
            SetRange(suppHighStart, Unicode.MaxCodePoint, _initialValue, true);
        }

        CompactData();
        if (highStart > 0x10000)
        {
            CompactIndex2();
        }

        // Store the high value at the end of the data array, then round up to granularity. Done
        // after CompactData, which assumes dataLength is a multiple of the data block length.
        _data[_dataLength++] = highValue;
        while ((_dataLength & (DataGranularity - 1)) != 0)
        {
            _data[_dataLength++] = _initialValue;
        }

        _isCompacted = true;
    }

    // Finds the start of the trailing run that all maps to highValue; supplementary indexes above
    // it are omitted.
    private int FindHighStart(uint highValue)
    {
        var initialValue = _initialValue;
        var index2NullOffset = _index2NullOffset;
        var nullBlock = _dataNullOffset;

        int prevI2Block;
        int prevBlock;
        if (highValue == initialValue)
        {
            prevI2Block = index2NullOffset;
            prevBlock = nullBlock;
        }
        else
        {
            prevI2Block = -1;
            prevBlock = -1;
        }

        var c = Unicode.CodePointLimit;
        var i1 = Index1Length;
        while (c > 0)
        {
            var i2Block = _index1[--i1];
            if (i2Block == prevI2Block)
            {
                // Same index-2 block as the previous one, filled with highValue.
                c -= CodePointsPerIndex1Entry;
                continue;
            }

            prevI2Block = i2Block;
            if (i2Block == index2NullOffset)
            {
                if (highValue != initialValue)
                {
                    return c;
                }
                c -= CodePointsPerIndex1Entry;
            }
            else
            {
                for (var i2 = Index2BlockLength; i2 > 0;)
                {
                    var block = _index2[i2Block + --i2];
                    if (block == prevBlock)
                    {
                        c -= DataBlockLength;
                        continue;
                    }

                    prevBlock = block;
                    if (block == nullBlock)
                    {
                        if (highValue != initialValue)
                        {
                            return c;
                        }
                        c -= DataBlockLength;
                    }
                    else
                    {
                        for (var j = DataBlockLength; j > 0;)
                        {
                            if (_data[block + --j] != highValue)
                            {
                                return c;
                            }
                            --c;
                        }
                    }
                }
            }
        }

        return 0;
    }

    private void CompactData()
    {
        // Do not compact the linear ASCII data.
        var newStart = DataStartOffset;
        for (var s = 0; s < newStart; s += DataBlockLength)
        {
            _map[s >> Shift2] = s;
        }

        // Block length 64 for 2-byte UTF-8, then DataBlockLength.
        var blockLength = 64;
        var blockCount = blockLength >> Shift2;
        var start = newStart;
        while (start < _dataLength)
        {
            if (start == Data0800Offset)
            {
                blockLength = DataBlockLength;
                blockCount = 1;
            }

            if (_map[start >> Shift2] <= 0)
            {
                // Unused block; advance start, leave newStart.
                start += blockLength;
                continue;
            }

            var movedStart = FindSameDataBlock(newStart, start, blockLength);
            if (movedStart >= 0)
            {
                var mapIndex = start >> Shift2;
                for (var i = blockCount; i > 0; --i)
                {
                    _map[mapIndex++] = movedStart;
                    movedStart += DataBlockLength;
                }
                start += blockLength;
                continue;
            }

            // Maximum overlap (modulo granularity) with the previous, adjacent block.
            var overlap = blockLength - DataGranularity;
            while (overlap > 0 && !EqualData(newStart - overlap, start, overlap))
            {
                overlap -= DataGranularity;
            }

            if (overlap > 0 || newStart < start)
            {
                movedStart = newStart - overlap;
                var mapIndex = start >> Shift2;
                for (var i = blockCount; i > 0; --i)
                {
                    _map[mapIndex++] = movedStart;
                    movedStart += DataBlockLength;
                }

                start += overlap;
                for (var i = blockLength - overlap; i > 0; --i)
                {
                    _data[newStart++] = _data[start++];
                }
            }
            else
            {
                var mapIndex = start >> Shift2;
                for (var i = blockCount; i > 0; --i)
                {
                    _map[mapIndex++] = start;
                    start += DataBlockLength;
                }
                newStart = start;
            }
        }

        // Adjust the index-2 table, skipping the invalid gap.
        for (var i = 0; i < _index2Length; i++)
        {
            if (i == IndexGapOffset)
            {
                i += IndexGapLength;
            }
            _index2[i] = _map[_index2[i] >> Shift2];
        }
        _dataNullOffset = _map[_dataNullOffset >> Shift2];

        // Align the data length to the granularity.
        while ((newStart & (DataGranularity - 1)) != 0)
        {
            _data[newStart++] = _initialValue;
        }
        _dataLength = newStart;
    }

    private void CompactIndex2()
    {
        // Do not compact the linear BMP index-2 blocks.
        var newStart = Index2BmpLength;
        for (var s = 0; s < newStart; s += Index2BlockLength)
        {
            _map[s >> Shift1Minus2] = s;
        }

        // Reduce the index gap to what runtime needs.
        newStart += Utf8TwoByteIndex2Length + ((_highStart - 0x10000) >> Shift1);

        var start = Index2NullOffset;
        while (start < _index2Length)
        {
            var movedStart = FindSameIndex2Block(newStart, start);
            if (movedStart >= 0)
            {
                _map[start >> Shift1Minus2] = movedStart;
                start += Index2BlockLength;
                continue;
            }

            var overlap = Index2BlockLength - 1;
            while (overlap > 0 && !EqualIndex2(newStart - overlap, start, overlap))
            {
                --overlap;
            }

            if (overlap > 0 || newStart < start)
            {
                _map[start >> Shift1Minus2] = newStart - overlap;
                start += overlap;
                for (var i = Index2BlockLength - overlap; i > 0; --i)
                {
                    _index2[newStart++] = _index2[start++];
                }
            }
            else
            {
                _map[start >> Shift1Minus2] = start;
                start += Index2BlockLength;
                newStart = start;
            }
        }

        // Adjust the index-1 table.
        for (var i = 0; i < Index1Length; i++)
        {
            _index1[i] = _map[_index1[i] >> Shift1Minus2];
        }
        _index2NullOffset = _map[_index2NullOffset >> Shift1Minus2];

        // Align: granularity-aligned for 16-bit, 2-aligned for 32-bit. 0xFFFF<<IndexShift is an
        // impossible real value.
        while ((newStart & ((DataGranularity - 1) | 1)) != 0)
        {
            _index2[newStart++] = 0xFFFF << IndexShift;
        }
        _index2Length = newStart;
    }

    private int FindSameDataBlock(int dataLength, int otherBlock, int blockLength)
    {
        var limit = dataLength - blockLength;
        for (var block = 0; block <= limit; block += DataGranularity)
        {
            if (EqualData(block, otherBlock, blockLength))
            {
                return block;
            }
        }
        return -1;
    }

    private int FindSameIndex2Block(int index2Length, int otherBlock)
    {
        var limit = index2Length - Index2BlockLength;
        for (var block = 0; block <= limit; block++)
        {
            if (EqualIndex2(block, otherBlock, Index2BlockLength))
            {
                return block;
            }
        }
        return -1;
    }

    private bool EqualData(int a, int b, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (_data[a + i] != _data[b + i])
            {
                return false;
            }
        }
        return true;
    }

    private bool EqualIndex2(int a, int b, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (_index2[a + i] != _index2[b + i])
            {
                return false;
            }
        }
        return true;
    }
}
