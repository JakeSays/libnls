namespace NlsDataGenerator.IcuFormat;

// The UCPTrie build orchestration (compactTrie) and serialization (ucptrie_toBinary), specialized
// to the fast type with 16-bit values — the form normalization data uses. Build() returns the
// UCPTrieHeader + index + data bytes that ucptrie_openFromBinary reads.
internal sealed partial class CodePointTrieBuilder
{
    private const uint Signature = 0x54726933;
    private const int NoDataNullOffset = 0xFFFFF;

    public byte[] Build()
    {
        // The mutable trie holds 32-bit values; narrow to 16 bits before compacting.
        MaskValues(0xFFFF);

        var fastLimit = Unicode.SupplementaryMin;
        var indexLength = CompactTrie(fastLimit >> Shift3);

        // 16-bit data finalization: pad for 4-byte alignment, then store highValue and errorValue
        // as the last two data values.
        if (((indexLength ^ _dataLength) & 1) != 0)
        {
            _data[_dataLength++] = _errorValue;
        }
        if (_data[_dataLength - 1] != _errorValue || _data[_dataLength - 2] != _highValue)
        {
            _data[_dataLength++] = _highValue;
            _data[_dataLength++] = _errorValue;
        }

        var options = ((_dataLength & 0xF0000) >> 4) | ((_dataNullOffset & 0xF0000) >> 8);

        var writer = new LittleEndianWriter();
        writer.WriteUInt32(Signature);
        writer.WriteUInt16((ushort)options);
        writer.WriteUInt16((ushort)indexLength);
        writer.WriteUInt16((ushort)_dataLength);
        writer.WriteUInt16((ushort)_index3NullOffset);
        writer.WriteUInt16((ushort)_dataNullOffset);
        writer.WriteUInt16((ushort)(_highStart >> Shift2));

        if (_highStart <= fastLimit)
        {
            // Condense only the fast index from the mutable-trie index.
            for (int i = 0, j = 0; j < indexLength; i += SmallDataBlocksPerBmpBlock, j++)
            {
                writer.WriteUInt16((ushort)_index[i]);
            }
        }
        else
        {
            for (var j = 0; j < indexLength; j++)
            {
                writer.WriteUInt16((ushort)_index16[j]);
            }
        }

        for (var i = 0; i < _dataLength; i++)
        {
            writer.WriteUInt16((ushort)_data[i]);
        }

        return writer.ToArray();
    }

    private int CompactTrie(int fastILimit)
    {
        _highValue = Get(Unicode.MaxCodePoint);
        var realHighStart = FindHighStart();
        realHighStart = (realHighStart + (CpPerIndex2Entry - 1)) & ~(CpPerIndex2Entry - 1);
        if (realHighStart == Unicode.CodePointLimit)
        {
            _highValue = _initialValue;
        }

        // Always store indexes and data for the fast range; pin highStart to its top while building.
        var fastLimit = fastILimit << Shift3;
        if (realHighStart < fastLimit)
        {
            for (var i = realHighStart >> Shift3; i < fastILimit; i++)
            {
                _flags[i] = AllSame;
                _index[i] = _highValue;
            }
            _highStart = fastLimit;
        }
        else
        {
            _highStart = realHighStart;
        }

        var asciiData = new uint[Unicode.AsciiLimit];
        for (var i = 0; i < Unicode.AsciiLimit; i++)
        {
            asciiData[i] = Get(i);
        }

        // Deduplicate whole-value blocks and find a good null block; get a data-length upper bound.
        var allSameBlocks = new AllSameBlocks();
        var newDataCapacity = CompactWholeDataBlocks(fastILimit, allSameBlocks);
        var newData = new uint[newDataCapacity];
        Array.Copy(asciiData, newData, Unicode.AsciiLimit);

        var dataNullIndex = allSameBlocks.FindMostUsed();

        var mixedBlocks = new MixedBlocks();
        var newDataLength = CompactData(fastILimit, newData, newDataCapacity, dataNullIndex, mixedBlocks);
        _data = newData;
        _dataLength = newDataLength;
        if (_dataLength > (0x3FFFF + SmallDataBlockLength))
        {
            throw new InvalidOperationException("UCPTrie last data block offset too high for the index");
        }

        if (dataNullIndex >= 0)
        {
            _dataNullOffset = (int)_index[dataNullIndex];
            _initialValue = _data[_dataNullOffset];
        }
        else
        {
            _dataNullOffset = NoDataNullOffset;
        }

        var indexLength = CompactIndex(fastILimit, mixedBlocks);
        _highStart = realHighStart;
        return indexLength;
    }
}
