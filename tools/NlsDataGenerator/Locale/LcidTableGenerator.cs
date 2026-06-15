using System.Globalization;
using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Locale;

// Emits the LCID <-> locale-name table as an ICU data item for codepages.dat, backing
// LocaleNameToLCID / LCIDToLocaleName / IsValidLocale, from data/lcidmap.txt (one
// "locale-name:0xLCID" line per locale, scraped from the Microsoft [MS-LCID] reference and scoped
// to the locales we ship). LCID is a Windows concept with no CLDR/ICU equivalent, so the mapping
// lives in that text file rather than being derived here.
//
// Item layout: an ICU DataHeader (dataFormat "Lcid") followed by a uint32 entry count, then per
// entry a uint32 LCID, a uint8 name byte-length, and that many ASCII name bytes. The invariant
// locale (LCID 0x007F, empty name) is always emitted first; some LCIDs map to more than one name
// (shared territories), in which case a reverse LCID->name lookup takes the first matching entry.
internal static class LcidTableGenerator
{
    // The 4-char dataFormat the libnls reader checks. The LCID map is a fixed Windows table, so the
    // versions are nominal.
    private const string DataFormat = "Lcid";
    private static readonly byte[] FormatVersion = [1, 0, 0, 0];
    private static readonly byte[] DataVersion = [0, 0, 0, 0];

    private const uint InvariantLcid = 0x007F;

    public static byte[] Generate(string lcidMapPath)
    {
        var entries = new List<(uint Lcid, string Name)> { (InvariantLcid, "") };
        foreach (var rawLine in File.ReadAllLines(lcidMapPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            // "locale-name:0xLCID" — split on the first colon (locale names never contain one).
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidOperationException($"malformed lcidmap line: {rawLine}");
            }
            var name = line[..colon];
            var lcidText = line[(colon + 1)..].Trim();
            if (!lcidText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                || !uint.TryParse(lcidText.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
            {
                throw new InvalidOperationException($"malformed LCID in lcidmap line: {rawLine}");
            }
            entries.Add((lcid, name));
        }

        var writer = new LittleEndianWriter();
        new IcuDataHeader(DataFormat, FormatVersion, DataVersion).Write(writer);
        writer.WriteUInt32((uint)entries.Count);
        foreach (var entry in entries)
        {
            writer.WriteUInt32(entry.Lcid);
            writer.WriteByte((byte)entry.Name.Length);
            writer.WriteAsciiString(entry.Name);
        }
        return writer.ToArray();
    }
}
