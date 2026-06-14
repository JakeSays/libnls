using System.Text.RegularExpressions;

namespace NlsDataGenerator.Parsing;

// Reads the UScriptCode enum out of the vendored ICU uscript.h header (repo/icu/common/unicode/
// uscript.h), mapping each USCRIPT_* enumerant to its numeric value. The collation root build
// resolves script sample characters to these exact codes; reading the header keeps them in sync
// with the pinned ICU instead of hardcoding ~200 constants.
internal static partial class UScriptHeaderReader
{
    // An enumerant line: "USCRIPT_NAME = <value>", where <value> is a number or another enumerant.
    private static readonly Regex Enumerant = EnumerantRegex();

    public static Dictionary<string, int> Read(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var inEnum = false;
        foreach (var line in File.ReadLines(path))
        {
            if (!inEnum)
            {
                inEnum = line.Contains("typedef enum UScriptCode {");
                continue;
            }
            if (line.Contains("} UScriptCode;"))
            {
                break;
            }
            var match = Enumerant.Match(line);
            if (!match.Success)
            {
                continue;
            }
            var name = match.Groups[1].Value;
            var token = match.Groups[2].Value;
            // The value is either a literal, or an alias to an earlier enumerant already in the map.
            map[name] = int.TryParse(token, out var value) ? value : map[token];
        }
        return map;
    }

    // Maps each script's ISO 15924 code (from the `/* Latn */` trailing comment) to its numeric value,
    // for resolving [reorder Latn Cyrl …] script names. u_getPropertyValueEnum(UCHAR_SCRIPT, word) does
    // this in ICU; the header comments carry the same short codes, kept in sync with the pinned ICU.
    public static Dictionary<string, int> ReadScriptCodesByIsoName(string path)
    {
        var values = Read(path);
        var byName = values.ToDictionary(kv => kv.Key, kv => kv.Value);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var match = IsoCommentRegex().Match(line);
            if (match.Success && byName.TryGetValue(match.Groups[1].Value, out var value))
            {
                result[match.Groups[2].Value] = value;
            }
        }
        return result;
    }

    [GeneratedRegex(@"(USCRIPT_\w+)\s*=\s*(-?\w+)")]
    private static partial Regex EnumerantRegex();

    // "USCRIPT_LATIN = 25, /* Latn */" -> name, ISO code.
    [GeneratedRegex(@"(USCRIPT_\w+)\s*=.*?/\*\s*([A-Za-z]{4})\s*\*/")]
    private static partial Regex IsoCommentRegex();
}
