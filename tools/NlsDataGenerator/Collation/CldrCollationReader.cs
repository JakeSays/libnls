using System.Xml.Linq;
using NlsDataGenerator.Parsing;


namespace NlsDataGenerator.Collation;

// Reads CLDR collation XML directly, replacing the standalone cldr-collation-extractor + genrb's .txt
// step. For each <collation type="..."> it extracts the <cr> tailoring rules (dropping comments and
// blank lines, concatenating with no separator, then resolving \u escapes exactly as genrb's lexer
// does) in document order. Provisional/unconfirmed drafts and alternate variants are skipped (approved
// and "contributed" collations are kept), matching ICU's converter.
// Parsed locales are cached so [import langTag] resolution does not re-read a file.
internal sealed class CldrCollationReader
{
    private readonly string _collationDirectory;
    private readonly Dictionary<string, LocaleCollations> _cache = new(StringComparer.Ordinal);

    public CldrCollationReader(string collationDirectory)
    {
        _collationDirectory = collationDirectory;
    }

    // Loads <collationDirectory>/<locale>.xml. Returns an empty set of types when the file has no
    // <collations> element (alias-only or inherits-root locales).
    public LocaleCollations Read(string locale)
    {
        if (_cache.TryGetValue(locale, out var cached))
        {
            return cached;
        }
        var path = Path.Combine(_collationDirectory, locale + ".xml");
        var result = Parse(locale, path);
        _cache[locale] = result;
        return result;
    }

    private static LocaleCollations Parse(string locale, string path)
    {
        var document = XDocument.Load(path);
        var collations = document.Root?.Element("collations");
        if (collations is null)
        {
            return new LocaleCollations(locale, null, []);
        }

        var defaultType = collations.Element("defaultCollation")?.Value.Trim();
        var types = new List<CollationTypeRules>();
        foreach (var collation in collations.Elements("collation"))
        {
            var type = collation.Attribute("type")?.Value;
            if (type is null)
            {
                continue;
            }
            // ICU's data build includes approved and "contributed" collations, dropping only the lower
            // "provisional"/"unconfirmed" draft levels. Alternate variants (alt="short") are also dropped.
            var draft = collation.Attribute("draft")?.Value;
            if (draft is "provisional" or "unconfirmed" || collation.Attribute("alt") is not null)
            {
                continue;
            }
            var lines = ExtractRuleLines(collation.Element("cr")?.Value ?? "");
            var rules = lines.Count > 0
                ? IcuUnescape.Unescape(string.Concat(lines))
                : "";
            types.Add(new CollationTypeRules(type, rules));
        }
        return new LocaleCollations(locale, defaultType, types);
    }

    // Splits the CDATA rule block into individual rule lines, dropping comments and blanks.
    private static List<string> ExtractRuleLines(string ruleBlock)
    {
        var lines = new List<string>();
        foreach (var rawLine in ruleBlock.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    // UCA rules use '#' for comments and single quotes to quote literals; this cuts the line at the
    // first unquoted '#'.
    private static string StripComment(string line)
    {
        var insideQuote = false;
        for (var index = 0; index < line.Length; ++index)
        {
            var character = line[index];
            if (character == '\'')
            {
                insideQuote = !insideQuote;
            }
            else if (character == '#' && !insideQuote)
            {
                return line[..index];
            }
        }
        return line;
    }
}
