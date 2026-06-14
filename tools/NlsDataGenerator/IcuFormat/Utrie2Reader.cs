namespace NlsDataGenerator.IcuFormat;

// Reads a serialized 32-bit UTrie2 (the on-disk form Trie2Builder.Freeze writes and utrie2.h reads)
// and resolves a code point's value, porting utrie2.h's _UTRIE2_INDEX_FROM_CP / _UTRIE2_GET32. Used to
// decode a reference collation trie so the exact CE32 each code point maps to can be inspected, rather
// than inferred from blob sizes.
internal sealed class Utrie2Reader
{
    private const int Shift1 = 11;
    private const int Shift2 = 5;
    private const int IndexShift = 2;
    private const int DataMask = (1 << Shift2) - 1;
    private const int Index2Mask = (1 << (Shift1 - Shift2)) - 1;
    private const int LscpIndex2Offset = 0x10000 >> Shift2;
    private const int LscpIndex2Length = 0x400 >> Shift2;
    private const int Index2BmpLength = LscpIndex2Offset + LscpIndex2Length;
    private const int Utf8TwoByteIndex2Length = 0x800 >> 6;
    private const int Index1Offset = Index2BmpLength + Utf8TwoByteIndex2Length;
    private const int OmittedBmpIndex1Length = 0x10000 >> Shift1;

    private readonly ushort[] _index;
    private readonly uint[] _data;
    private readonly int _highStart;
    private readonly uint _highValue;

    public Utrie2Reader(byte[] trie)
    {
        // UTrie2Header: signature, options, indexLength, shiftedDataLength, index2NullOffset,
        // dataNullOffset, shiftedHighStart (16 bytes).
        var indexLength = ReadUInt16(trie, 6);
        var dataLength = ReadUInt16(trie, 8) << IndexShift;
        _highStart = ReadUInt16(trie, 14) << Shift1;
        _index = new ushort[indexLength];
        var pos = 16;
        for (var i = 0; i < indexLength; ++i)
        {
            _index[i] = (ushort)ReadUInt16(trie, pos + i * 2);
        }
        pos += indexLength * 2;
        _data = new uint[dataLength];
        for (var i = 0; i < dataLength; ++i)
        {
            _data[i] = ReadUInt32(trie, pos + i * 4);
        }
        // The high value sits in the last data block (utrie2 highValueIndex = dataLength - granularity).
        _highValue = _data[dataLength - (1 << IndexShift)];
    }

    public uint Get32(int c)
    {
        int dataIndex;
        if (c < 0xD800)
        {
            dataIndex = (_index[c >> Shift2] << IndexShift) + (c & DataMask);
        }
        else if (c <= 0xFFFF)
        {
            var offset = c <= 0xDBFF ? LscpIndex2Offset - (0xD800 >> Shift2) : 0;
            dataIndex = (_index[offset + (c >> Shift2)] << IndexShift) + (c & DataMask);
        }
        else if (c >= _highStart)
        {
            return _highValue;
        }
        else
        {
            var i1 = _index[(Index1Offset - OmittedBmpIndex1Length) + (c >> Shift1)];
            var i2 = _index[i1 + ((c >> Shift2) & Index2Mask)];
            dataIndex = (i2 << IndexShift) + (c & DataMask);
        }
        return _data[dataIndex];
    }

    private static int ReadUInt16(byte[] b, int o)
    {
        return b[o] | (b[o + 1] << 8);
    }

    private static uint ReadUInt32(byte[] b, int o)
    {
        return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
