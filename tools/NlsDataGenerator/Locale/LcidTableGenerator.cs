using System.Globalization;
using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Locale;

// Emits the LCID <-> locale-name table backing LocaleNameToLCID / LCIDToLocaleName / IsValidLocale /
// GetLocaleInfo, from data/lcidmap.txt (one "locale-name:0xLCID" line per locale, scraped from the
// Microsoft [MS-LCID] reference and scoped to the locales we ship). LCID is a Windows concept with no
// CLDR/ICU data equivalent, so the mapping lives in that text file rather than being derived here.
//
// Format: ASCII magic "LCID", a uint32 version, a uint32 entry count, then per entry a uint32 LCID, a
// uint8 name byte-length, and that many ASCII name bytes. The invariant locale (LCID 0x007F, empty
// name) is always emitted first; some LCIDs map to more than one name (shared territories), in which
// case a reverse LCID->name lookup should take the first matching entry.
internal static class LcidTableGenerator
{
    private const uint FormatVersion = 1;
    private const uint InvariantLcid = 0x007F;

    public static void Write(string lcidMapPath, string outputDirectory)
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
        writer.WriteAsciiString("LCID");
        writer.WriteUInt32(FormatVersion);
        writer.WriteUInt32((uint)entries.Count);
        foreach (var entry in entries)
        {
            writer.WriteUInt32(entry.Lcid);
            writer.WriteByte((byte)entry.Name.Length);
            writer.WriteAsciiString(entry.Name);
        }

        var path = Path.Combine(outputDirectory, "lcid-locales.nlsdata");
        File.WriteAllBytes(path, writer.ToArray());
        Console.WriteLine($"wrote {path} ({writer.Length} bytes, {entries.Count} locales)");
    }
}
