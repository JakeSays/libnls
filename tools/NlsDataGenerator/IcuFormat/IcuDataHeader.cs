namespace NlsDataGenerator.IcuFormat;

// The fixed header every udata_open file begins with, so the vendored ICU readers accept our
// output. Layout (little-endian, ASCII charset):
//
//   MappedData { uint16 headerSize; uint8 magic1=0xDA; uint8 magic2=0x27; }
//   UDataInfo  { uint16 size=20; uint16 reservedWord=0; uint8 isBigEndian=0; uint8 charsetFamily=0;
//                uint8 sizeofUChar=2; uint8 reservedByte=0; uint8 dataFormat[4];
//                uint8 formatVersion[4]; uint8 dataVersion[4]; }
//   zero padding to headerSize (16-byte aligned) so the body that follows starts aligned.
//
// dataFormat is the 4-char subsystem tag (e.g. "cAsE", "Nrm2", "UCol"); formatVersion is the
// subsystem's binary-format version that its reader validates; dataVersion is the Unicode version.
internal readonly struct IcuDataHeader
{
    private const int HeaderSize = 32;
    private const int UDataInfoSize = 20;
    private const int MappedDataSize = 4;

    private readonly string _dataFormat;
    private readonly byte[] _formatVersion;
    private readonly byte[] _dataVersion;

    public IcuDataHeader(string dataFormat, byte[] formatVersion, byte[] dataVersion)
    {
        if (dataFormat.Length != 4)
        {
            throw new ArgumentException("dataFormat must be exactly 4 characters", nameof(dataFormat));
        }

        if (formatVersion.Length != 4)
        {
            throw new ArgumentException("formatVersion must be exactly 4 bytes", nameof(formatVersion));
        }

        if (dataVersion.Length != 4)
        {
            throw new ArgumentException("dataVersion must be exactly 4 bytes", nameof(dataVersion));
        }

        _dataFormat = dataFormat;
        _formatVersion = formatVersion;
        _dataVersion = dataVersion;
    }

    // Writes the 32-byte header. Must be the first thing written to the file (it starts at offset 0).
    public void Write(LittleEndianWriter writer)
    {
        // MappedData.
        writer.WriteUInt16(HeaderSize);
        writer.WriteMagic();

        // UDataInfo.
        writer.WriteUInt16(UDataInfoSize);
        writer.WriteUInt16(0);
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteByte(2);
        writer.WriteByte(0);
        writer.WriteAsciiString(_dataFormat);
        writer.WriteBytes(_formatVersion);
        writer.WriteBytes(_dataVersion);

        // Zero padding to headerSize.
        writer.WritePadding(HeaderSize - MappedDataSize - UDataInfoSize);
    }
}
