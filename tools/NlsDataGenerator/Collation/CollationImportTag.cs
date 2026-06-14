namespace NlsDataGenerator.Collation;

// Parses the BCP 47 language tag in an [import langTag] directive into a locale ID and a CLDR
// collation type, replacing ICU's ulocimp_forLanguageTag / getBaseName / getKeywordValue. The tag
// looks like "und-u-co-search" or "de-u-co-phonebk": the part before "-u-" is the locale, and the
// Unicode-extension "co" key gives the collation type as a BCP 47 alias that maps to the CLDR name
// the XML uses (e.g. phonebk -> phonebook).
internal static class CollationImportTag
{
    // BCP 47 collation type aliases -> CLDR collation type names (CLDR bcp47/collation.xml). Types
    // that are already the CLDR name (search, standard, stroke, pinyin, …) need no entry.
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.Ordinal)
    {
        ["phonebk"] = "phonebook",
        ["trad"] = "traditional",
        ["dict"] = "dictionary",
        ["gb2312"] = "gb2312han",
    };

    public static (string LocaleId, string CollationType) Parse(string tag)
    {
        var language = tag;
        var collationType = "standard";

        var extensionIndex = tag.IndexOf("-u-", StringComparison.Ordinal);
        if (extensionIndex >= 0)
        {
            language = tag.Substring(0, extensionIndex);
            var extension = tag.Substring(extensionIndex + 3);
            var parts = extension.Split('-', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 1 < parts.Length; ++i)
            {
                if (parts[i] == "co")
                {
                    // A Unicode-extension key is exactly two chars; its value is all following subtags
                    // (3-8 chars each) until the next key. So "co-private-unihan" -> "private-unihan".
                    var value = new List<string>();
                    for (var j = i + 1; j < parts.Length && parts[j].Length > 2; ++j)
                    {
                        value.Add(parts[j]);
                    }
                    if (value.Count > 0)
                    {
                        collationType = string.Join("-", value);
                    }
                    break;
                }
            }
        }

        var localeId = language.Replace('-', '_');
        // ulocimp_forLanguageTag drops the "und" undefined-language subtag to an empty base, which
        // the rule parser then resolves to "root"; a bare "und" therefore imports from root.
        if (localeId.Length == 0 || localeId == "und")
        {
            localeId = "root";
        }
        if (TypeAliases.TryGetValue(collationType, out var mapped))
        {
            collationType = mapped;
        }
        return (localeId, collationType);
    }
}
