using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Locale;

// Emits the LCID <-> locale-name table backing LocaleNameToLCID / LCIDToLocaleName /
// IsValidLocale / GetLocaleInfo. The LCID is a Windows concept ICU has no equivalent for, so this
// is a fixed libnls-internal table, not ICU data.
//
// Format: ASCII magic "LCID", a uint32 version, a uint32 entry count, then per entry a uint32
// LCID, a uint8 name byte-length, and that many ASCII name bytes. The invariant locale is LCID
// 0x007F with an empty name.
internal sealed class LcidTableGenerator
{
    private const uint FormatVersion = 1;

    private static readonly (uint Lcid, string Name)[] Entries =
    {
        (0x007F, ""),
        (0x0409, "en-US"),
        (0x0809, "en-GB"),
        (0x0407, "de-DE"),
        (0x0807, "de-CH"),
        (0x040C, "fr-FR"),
        (0x0C0C, "fr-CA"),
        (0x0410, "it-IT"),
        (0x0C0A, "es-ES"),
        (0x0416, "pt-BR"),
        (0x0816, "pt-PT"),
        (0x0413, "nl-NL"),
        (0x041D, "sv-SE"),
        (0x0406, "da-DK"),
        (0x0414, "nb-NO"),
        (0x040B, "fi-FI"),
        (0x0419, "ru-RU"),
        (0x0415, "pl-PL"),
        (0x0405, "cs-CZ"),
        (0x040E, "hu-HU"),
        (0x0408, "el-GR"),
        (0x041F, "tr-TR"),
        (0x0411, "ja-JP"),
        (0x0412, "ko-KR"),
        (0x0804, "zh-CN"),
        (0x0404, "zh-TW"),
        (0x0401, "ar-SA"),
        (0x040D, "he-IL"),
        (0x041E, "th-TH"),
        (0x042A, "vi-VN"),
    };

    public void Write(string outputDirectory)
    {
        var writer = new LittleEndianWriter();
        writer.WriteAsciiString("LCID");
        writer.WriteUInt32(FormatVersion);
        writer.WriteUInt32((uint)Entries.Length);
        foreach (var entry in Entries)
        {
            writer.WriteUInt32(entry.Lcid);
            writer.WriteByte((byte)entry.Name.Length);
            writer.WriteAsciiString(entry.Name);
        }

        var path = Path.Combine(outputDirectory, "lcid-locales.nlsdata");
        File.WriteAllBytes(path, writer.ToArray());
        Console.WriteLine($"wrote {path} ({writer.Length} bytes, {Entries.Length} locales)");
    }
}
