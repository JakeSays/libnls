namespace NlsDataGenerator.ResourceBundle;

// A minimal reader for the binary .res format, enough to walk a collation bundle: resolve tables and
// pull out their string and binary values. Used to validate the writer (extract the genrb-built
// %%CollationBin blobs and re-emit them) and, later, to diff generated tailorings against ICU's.
internal sealed class ResourceBundleReader
{
    private readonly byte[] _data;

    // File offset where the resource data begins (the udata header size).
    private readonly int _body;

    // Resource-relative byte offset of the 16-bit-units pool (where string-v2 values live).
    private readonly int _poolBytes;

    public uint RootWord { get; }

    public ResourceBundleReader(byte[] data)
    {
        _data = data;
        _body = ReadUInt16(0);
        RootWord = ReadUInt32(_body);
        // indexes[URES_INDEX_KEYS_TOP] is in 32-bit words; it is also the start of the 16-bit pool.
        _poolBytes = ReadInt32(_body + 4 + 4) << 2;
    }

    public Dictionary<string, uint> ReadTable(uint word)
    {
        var type = ResourceType.GetType(word);
        var file = _body + (ResourceType.GetOffset(word) << 2);
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (type == ResourceType.Table)
        {
            int count = ReadUInt16(file);
            var keysStart = file + 2;
            var valuesStart = file + 2 + count * 2;
            if ((count & 1) == 0)
            {
                valuesStart += 2;
            }
            for (var c = 0; c < count; ++c)
            {
                int key16 = ReadUInt16(keysStart + c * 2);
                var value = ReadUInt32(valuesStart + c * 4);
                result[KeyAt(key16)] = value;
            }
        }
        else if (type == ResourceType.Table32)
        {
            var count = ReadInt32(file);
            var keysStart = file + 4;
            var valuesStart = file + 4 + count * 4;
            for (var c = 0; c < count; ++c)
            {
                var key = ReadInt32(keysStart + c * 4);
                var value = ReadUInt32(valuesStart + c * 4);
                result[KeyAt(key)] = value;
            }
        }
        else if (type == ResourceType.Table16)
        {
            // Stored in the 16-bit pool; 16-bit key offsets and 16-bit values (implicitly STRING_V2
            // references into the 16-bit pool).
            var pos = _body + _poolBytes + ResourceType.GetOffset(word) * 2;
            int count = ReadUInt16(pos);
            var keysStart = pos + 2;
            var valuesStart = pos + 2 + count * 2;
            for (var c = 0; c < count; ++c)
            {
                int key16 = ReadUInt16(keysStart + c * 2);
                int value16 = ReadUInt16(valuesStart + c * 2);
                result[KeyAt(key16)] = ResourceType.MakeResource(ResourceType.StringV2, value16);
            }
        }
        else
        {
            throw new NotSupportedException($"unsupported table type {type}");
        }
        return result;
    }

    public byte[] ReadBinary(uint word)
    {
        var file = _body + (ResourceType.GetOffset(word) << 2);
        var length = ReadInt32(file);
        return _data[(file + 4)..(file + 4 + length)];
    }

    public string ReadString(uint word)
    {
        if (ResourceType.GetType(word) != ResourceType.StringV2)
        {
            throw new NotSupportedException("only string-v2 values are supported");
        }
        var pos = _body + _poolBytes + ResourceType.GetOffset(word) * 2;
        int first = ReadUInt16(pos);
        int length;
        if (first < 0xdc00)
        {
            length = 0;
            while (ReadUInt16(pos + length * 2) != 0)
            {
                ++length;
            }
            return ReadChars(pos, length);
        }
        if (first < 0xdfef)
        {
            length = first - 0xdc00;
            return ReadChars(pos + 2, length);
        }
        if (first < 0xdfff)
        {
            length = ((first - 0xdfef) << 16) | ReadUInt16(pos + 2);
            return ReadChars(pos + 4, length);
        }
        length = (ReadUInt16(pos + 2) << 16) | ReadUInt16(pos + 4);
        return ReadChars(pos + 6, length);
    }

    private string KeyAt(int keyOffset)
    {
        var pos = _body + keyOffset;
        var end = pos;
        while (_data[end] != 0)
        {
            ++end;
        }
        return System.Text.Encoding.ASCII.GetString(_data, pos, end - pos);
    }

    private string ReadChars(int fileOffset, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; ++i)
        {
            chars[i] = (char)ReadUInt16(fileOffset + i * 2);
        }
        return new string(chars);
    }

    private int ReadUInt16(int offset)
    {
        return _data[offset] | (_data[offset + 1] << 8);
    }

    private int ReadInt32(int offset)
    {
        return _data[offset]
            | (_data[offset + 1] << 8)
            | (_data[offset + 2] << 16)
            | (_data[offset + 3] << 24);
    }

    private uint ReadUInt32(int offset)
    {
        return (uint)ReadInt32(offset);
    }
}
