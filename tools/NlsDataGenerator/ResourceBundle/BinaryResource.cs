using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.ResourceBundle;

// An opaque binary value, ported from genrb's BinaryResource. Used for the %%CollationBin blob.
// The data is preceded by its 32-bit length and padded so the data itself starts 16-aligned (the
// embedded collation blob begins with its own udata header that the runtime maps in place).
internal sealed class BinaryResource : Resource
{
    private const int BinaryAlignment = 16;

    private readonly byte[] _data;

    public BinaryResource(int key, byte[] data)
        : base(key)
    {
        _data = data;
        if (data.Length == 0)
        {
            Res = ResourceType.MakeEmptyResource(ResourceType.Binary);
            Written = true;
        }
    }

    protected override void HandlePreWrite(ref uint byteOffset)
    {
        var dataStart = byteOffset + 4;
        if (dataStart % BinaryAlignment != 0)
        {
            byteOffset += BinaryAlignment - dataStart % BinaryAlignment;
        }
        Res = ResourceType.MakeResource(ResourceType.Binary, (int)(byteOffset >> 2));
        byteOffset += 4 + (uint)_data.Length;
    }

    protected override void HandleWrite(LittleEndianWriter writer, ref uint byteOffset)
    {
        var dataStart = byteOffset + 4;
        if (dataStart % BinaryAlignment != 0)
        {
            var pad = BinaryAlignment - dataStart % BinaryAlignment;
            writer.WriteFiller((int)pad);
            byteOffset += pad;
        }
        writer.WriteUInt32((uint)_data.Length);
        if (_data.Length > 0)
        {
            writer.WriteBytes(_data);
        }
        byteOffset += 4 + (uint)_data.Length;
    }
}
