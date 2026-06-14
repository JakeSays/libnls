using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.ResourceBundle;

// Serializes a resource-bundle tree to the binary .res format, ported from genrb's SRBRoot. This
// targets the formatVersion 2 case ESE needs: a standalone little-endian bundle with no pool bundle
// and no copyright string. The tree is built by the caller through the factory methods, then Write()
// runs genrb's passes — compact the key pool, compact the string pool, assign 16-bit then byte
// offsets, and emit the udata "ResB" header followed by the indexes, keys, 16-bit units, and data.
internal sealed partial class ResourceBundleWriter
{
    // indexes[] length for formatVersion 2 with no pool bundle (URES_INDEX_16BIT_TOP + 1).
    private const int IndexLength = 7;

    // Key offsets begin after the space reserved for the root word and the indexes[] array.
    private const int KeysBottom = (1 + IndexLength) * 4;

    private byte[] _keys = new byte[256];
    private int _keysTop = KeysBottom;

    // The shared 16-bit-units pool; offset 0 holds a zero so empty resources can point at it.
    private readonly List<ushort> _pool = [0];
    private int _stringPoolUnits;
    private readonly Dictionary<string, StringResource> _stringSet = new(StringComparer.Ordinal);

    private readonly TableResource _root = new(-1);

    private Dictionary<int, int>? _keyMap;

    public int MaxTableLength;
    public int LocalKeyLimit;

    public TableResource Root => _root;

    public TableResource NewTable(string tag)
    {
        return new TableResource(AddTag(tag));
    }

    public StringResource NewString(string tag, string value)
    {
        return new StringResource(AddTag(tag), value);
    }

    public BinaryResource NewBinary(string tag, byte[] data)
    {
        return new BinaryResource(AddTag(tag), data);
    }

    // Appends a key string (ASCII plus NUL terminator) to the raw key pool and returns its offset.
    // genrb appends unconditionally; compactKeys() later removes the duplicates.
    public int AddTag(string tag)
    {
        var offset = _keysTop;
        EnsureKeyCapacity(tag.Length + 1);
        foreach (var character in tag)
        {
            _keys[_keysTop++] = (byte)character;
        }
        _keys[_keysTop++] = 0;
        return offset;
    }

    public string RawKeyString(int offset)
    {
        var end = offset;
        while (_keys[end] != 0)
        {
            ++end;
        }
        return System.Text.Encoding.ASCII.GetString(_keys, offset, end - offset);
    }

    public int MapKey(int oldPosition)
    {
        return _keyMap is null ? oldPosition : _keyMap[oldPosition];
    }

    public StringResource? FindString(string value)
    {
        return _stringSet.TryGetValue(value, out var resource) ? resource : null;
    }

    public void AddString(string value, StringResource resource)
    {
        _stringSet[value] = resource;
    }

    public void AddStringUnits(int count)
    {
        _stringPoolUnits += count;
    }

    public int PoolLength => _pool.Count;

    public void AppendPoolUnit(ushort unit)
    {
        _pool.Add(unit);
    }

    // The 16-bit-unit offset for a string-v2 resource word, or -1 if it does not fit a Resource16.
    // Without a pool bundle this is simply the string's offset when it is within 16-bit range.
    public int MakeRes16(uint resource)
    {
        if (resource == 0)
        {
            return 0;
        }
        if (ResourceType.GetType(resource) == ResourceType.StringV2)
        {
            var offset = ResourceType.GetOffset(resource);
            if (offset <= 0xffff)
            {
                return offset;
            }
        }
        return -1;
    }

    public static uint CalcPadding(uint size)
    {
        var remainder = size % 4;
        return remainder == 0 ? 0 : 4 - remainder;
    }

    public byte[] Write()
    {
        CompactKeys();
        // Pad the key pool so its end is 4-aligned for the 16-bit units that follow.
        while ((_keysTop & 3) != 0)
        {
            EnsureKeyCapacity(1);
            _keys[_keysTop++] = 0xaa;
        }
        LocalKeyLimit = KeysBottom < _keysTop ? Math.Min(_keysTop, 0x10000) : 0;

        _root.PreflightStrings(this);
        if (_stringPoolUnits > 0)
        {
            CompactStringsV2();
        }

        _root.Write16(this);
        if ((_pool.Count & 1) != 0)
        {
            _pool.Add(0xaaaa);
        }

        var byteOffset = (uint)(_keysTop + _pool.Count * 2);
        _root.PreWrite(ref byteOffset);
        var top = byteOffset;

        var writer = new LittleEndianWriter();
        new IcuDataHeader("ResB", [2, 0, 0, 0], [1, 4, 0, 0]).Write(writer);

        writer.WriteUInt32(_root.Res);

        var indexes = new int[IndexLength];
        indexes[0] = IndexLength;
        indexes[1] = _keysTop >> 2;
        indexes[2] = (int)(top >> 2);
        indexes[3] = indexes[2];
        indexes[4] = MaxTableLength;
        indexes[5] = 0;
        indexes[6] = (_keysTop >> 2) + (_pool.Count >> 1);
        foreach (var index in indexes)
        {
            writer.WriteUInt32((uint)index);
        }

        writer.WriteBytes(_keys.AsSpan(KeysBottom, _keysTop - KeysBottom));
        foreach (var unit in _pool)
        {
            writer.WriteUInt16(unit);
        }

        var bodyOffset = (uint)(_keysTop + _pool.Count * 2);
        _root.Write(writer, ref bodyOffset);
        if (bodyOffset != top)
        {
            throw new InvalidOperationException(
                $"resource bundle size mismatch: wrote {bodyOffset} bytes, counted {top}");
        }
        return writer.ToArray();
    }

    private void EnsureKeyCapacity(int additional)
    {
        var required = _keysTop + additional;
        if (required <= _keys.Length)
        {
            return;
        }
        var capacity = _keys.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }
        Array.Resize(ref _keys, capacity);
    }
}
