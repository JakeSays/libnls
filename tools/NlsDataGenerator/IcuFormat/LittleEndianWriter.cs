namespace NlsDataGenerator.IcuFormat;

// Little-endian primitive writer over a self-grown byte array. Every NLS/ICU binary we emit
// targets little-endian (x86-64 / aarch64), so each width is written byte-by-byte here rather
// than relying on BitConverter's host endianness.
internal sealed class LittleEndianWriter
{
    private byte[] _buffer = new byte[256];
    private int _length;

    public int Length => _length;

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        _buffer[_length++] = (byte)(value & 0xFF);
        _buffer[_length++] = (byte)((value >> 8) & 0xFF);
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(4);
        _buffer[_length++] = (byte)(value & 0xFF);
        _buffer[_length++] = (byte)((value >> 8) & 0xFF);
        _buffer[_length++] = (byte)((value >> 16) & 0xFF);
        _buffer[_length++] = (byte)((value >> 24) & 0xFF);
    }

    public void WriteUInt64(ulong value)
    {
        EnsureCapacity(8);
        for (var i = 0; i < 8; ++i)
        {
            _buffer[_length++] = (byte)(value >> (8 * i));
        }
    }

    public void WriteUInt16Array(ReadOnlySpan<ushort> values)
    {
        foreach (var value in values)
        {
            WriteUInt16(value);
        }
    }

    public void WriteCharArray(ReadOnlySpan<char> values)
    {
        foreach (var value in values)
        {
            WriteUInt16(value);
        }
    }

    public void WriteUInt32Array(ReadOnlySpan<uint> values)
    {
        foreach (var value in values)
        {
            WriteUInt32(value);
        }
    }

    public void WriteUInt32Array(ReadOnlySpan<int> values)
    {
        foreach (var value in values)
        {
            WriteUInt32((uint)value);
        }
    }

    public void WriteUInt64Array(ReadOnlySpan<long> values)
    {
        foreach (var value in values)
        {
            WriteUInt64((ulong)value);
        }
    }

    // Writes each flag as a single byte: 1 for true, 0 for false.
    public void WriteBoolBytes(ReadOnlySpan<bool> values)
    {
        foreach (var value in values)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }
    }

    public void WriteAsciiString(string value)
    {
        EnsureCapacity(value.Length);
        foreach (var character in value)
        {
            _buffer[_length++] = (byte)character;
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    // Writes count zero bytes.
    public void WritePadding(int count)
    {
        EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            _buffer[_length++] = 0;
        }
    }

    // Writes count filler bytes (0xaa), matching ICU's udata_writePadding for resource data.
    public void WriteFiller(int count)
    {
        EnsureCapacity(count);
        for (var i = 0; i < count; i++)
        {
            _buffer[_length++] = 0xAA;
        }
    }

    // Pads with zero bytes until the length is a multiple of alignment.
    public void AlignTo(int alignment)
    {
        var remainder = _length % alignment;
        if (remainder != 0)
        {
            WritePadding(alignment - remainder);
        }
    }

    public byte[] ToArray()
    {
        var result = new byte[_length];
        Array.Copy(_buffer, result, _length);
        return result;
    }

    private void EnsureCapacity(int additional)
    {
        var required = _length + additional;
        if (required <= _buffer.Length)
        {
            return;
        }

        var capacity = _buffer.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _buffer, capacity);
    }
}
