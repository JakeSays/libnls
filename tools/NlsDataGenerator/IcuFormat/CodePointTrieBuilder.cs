namespace NlsDataGenerator.IcuFormat;

// Faithful port of ICU's UMutableCPTrie (umutablecptrie.cpp), the builder for UCPTrie/CodePointTrie
// — the trie format normalization data uses. This file is the build-time half: a flat index (one
// entry per 16 code points, holding either a shared value or a data-block offset, distinguished by
// the per-block flag) plus the data array, with Set / SetRange. The compaction (BuildImmutable)
// and serialization (ToBinary) follow in the same class.
internal sealed partial class CodePointTrieBuilder
{
    private const int FastShift = 6;
    private const int Shift3 = 4;
    private const int FastDataBlockLength = 1 << FastShift;
    private const int SmallDataBlockLength = 1 << Shift3;
    private const int SmallDataMask = SmallDataBlockLength - 1;
    private const int CpPerIndex2Entry = 1 << 9;
    private const int SmallDataBlocksPerBmpBlock = 1 << (FastShift - Shift3);

    private const int IndexLimit = Unicode.CodePointLimit >> Shift3;
    private const int BmpIndexLimit = Unicode.SupplementaryMin >> Shift3;
    private const int InitialDataLength = 1 << 14;
    private const int MediumDataLength = 1 << 17;
    private const int MaxDataLength = Unicode.CodePointLimit;

    private const byte AllSame = 0;
    private const byte Mixed = 1;
    private const byte SameAs = 2;

    private uint[] _index = new uint[BmpIndexLimit];
    private uint[] _data = new uint[InitialDataLength];
    private readonly byte[] _flags = new byte[IndexLimit];
    private int _dataLength;
    private int _dataNullOffset = -1;
    private int _index3NullOffset = -1;

    private readonly uint _origInitialValue;
    private uint _initialValue;
    private uint _errorValue;
    private int _highStart;
    private uint _highValue;

    public CodePointTrieBuilder(uint initialValue, uint errorValue)
    {
        _origInitialValue = initialValue;
        _initialValue = initialValue;
        _errorValue = errorValue;
        _highStart = 0;
        _highValue = initialValue;
    }

    public void Set(int codePoint, uint value)
    {
        if ((uint)codePoint > Unicode.MaxCodePoint)
        {
            throw new ArgumentOutOfRangeException(nameof(codePoint));
        }

        EnsureHighStart(codePoint);
        var block = GetDataBlock(codePoint >> Shift3);
        _data[block + (codePoint & SmallDataMask)] = value;
    }

    public void SetRange(int start, int end, uint value)
    {
        if ((uint)start > Unicode.MaxCodePoint || (uint)end > Unicode.MaxCodePoint || start > end)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        EnsureHighStart(end);

        var limit = end + 1;
        if ((start & SmallDataMask) != 0)
        {
            // Partial block at the start.
            var block = GetDataBlock(start >> Shift3);
            var nextStart = (start + SmallDataMask) & ~SmallDataMask;
            if (nextStart <= limit)
            {
                FillBlock(block, start & SmallDataMask, SmallDataBlockLength, value);
                start = nextStart;
            }
            else
            {
                FillBlock(block, start & SmallDataMask, limit & SmallDataMask, value);
                return;
            }
        }

        var rest = limit & SmallDataMask;
        limit &= ~SmallDataMask;

        while (start < limit)
        {
            var i = start >> Shift3;
            if (_flags[i] == AllSame)
            {
                _index[i] = value;
            }
            else
            {
                FillBlock((int)_index[i], 0, SmallDataBlockLength, value);
            }
            start += SmallDataBlockLength;
        }

        if (rest > 0)
        {
            // Partial block at the end.
            var block = GetDataBlock(start >> Shift3);
            FillBlock(block, 0, rest, value);
        }
    }

    private void EnsureHighStart(int codePoint)
    {
        if (codePoint < _highStart)
        {
            return;
        }

        // Round up to an index-2 entry boundary to simplify compaction.
        var rounded = (codePoint + CpPerIndex2Entry) & ~(CpPerIndex2Entry - 1);
        var i = _highStart >> Shift3;
        var iLimit = rounded >> Shift3;
        if (iLimit > _index.Length)
        {
            Array.Resize(ref _index, IndexLimit);
        }
        do
        {
            _flags[i] = AllSame;
            _index[i] = _initialValue;
        }
        while (++i < iLimit);
        _highStart = rounded;
    }

    private int AllocateDataBlock(int blockLength)
    {
        var newBlock = _dataLength;
        var newTop = newBlock + blockLength;
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
                throw new InvalidOperationException("UCPTrie data array overflow");
            }
            Array.Resize(ref _data, capacity);
        }
        _dataLength = newTop;
        return newBlock;
    }

    private int GetDataBlock(int i)
    {
        if (_flags[i] == Mixed)
        {
            return (int)_index[i];
        }

        if (i < BmpIndexLimit)
        {
            // A BMP block covers SmallDataBlocksPerBmpBlock small blocks; split them all out.
            var newBlock = AllocateDataBlock(FastDataBlockLength);
            var iStart = i & ~(SmallDataBlocksPerBmpBlock - 1);
            var iLimit = iStart + SmallDataBlocksPerBmpBlock;
            do
            {
                WriteBlock(newBlock, _index[iStart]);
                _flags[iStart] = Mixed;
                _index[iStart++] = (uint)newBlock;
                newBlock += SmallDataBlockLength;
            }
            while (iStart < iLimit);
            return (int)_index[i];
        }

        var smallBlock = AllocateDataBlock(SmallDataBlockLength);
        WriteBlock(smallBlock, _index[i]);
        _flags[i] = Mixed;
        _index[i] = (uint)smallBlock;
        return smallBlock;
    }

    private void WriteBlock(int block, uint value)
    {
        var limit = block + SmallDataBlockLength;
        for (var i = block; i < limit; i++)
        {
            _data[i] = value;
        }
    }

    private void FillBlock(int block, int start, int limit, uint value)
    {
        var to = block + limit;
        for (var i = block + start; i < to; i++)
        {
            _data[i] = value;
        }
    }
}
