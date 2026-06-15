using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Codepage;

// Emits the windows-1252 -> UTF-16 table as an ICU data item for codepages.dat. cp1252 is the only
// non-Unicode code page ESE text columns use (the other, 1200, is native UTF-16 and needs no
// table). The item is an ICU DataHeader (dataFormat "Cp52") followed by 256 little-endian uint16,
// the Unicode scalar for each byte; libnls loads it via udata_open.
//
// Bytes 0x00-0x7F are ASCII identity and 0xA0-0xFF are Latin-1 identity; only 0x80-0x9F differ.
// The five undefined positions (0x81, 0x8D, 0x8F, 0x90, 0x9D) map to their own value.
internal static class Windows1252Generator
{
    // The 4-char dataFormat the libnls reader checks. cp1252 is a fixed Windows table, so the
    // versions are nominal.
    private const string DataFormat = "Cp52";
    private static readonly byte[] FormatVersion = [1, 0, 0, 0];
    private static readonly byte[] DataVersion = [0, 0, 0, 0];

    private static readonly ushort[] HighRange =
    [
        0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
        0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178
    ];

    public static byte[] Generate()
    {
        var table = new ushort[256];
        for (var b = 0; b < 256; b++)
        {
            if (b is >= 0x80 and <= 0x9F)
            {
                table[b] = HighRange[b - 0x80];
            }
            else
            {
                table[b] = (ushort)b;
            }
        }

        var writer = new LittleEndianWriter();
        new IcuDataHeader(DataFormat, FormatVersion, DataVersion).Write(writer);
        foreach (var scalar in table)
        {
            writer.WriteUInt16(scalar);
        }
        return writer.ToArray();
    }
}
