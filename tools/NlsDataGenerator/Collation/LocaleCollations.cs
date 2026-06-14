namespace NlsDataGenerator.Collation;

// The collation data of one CLDR locale XML: its default collation type (if declared) and the
// collation types in document order. The order matters — genrb's resource bundle key pool follows
// construction order, so the .res is only byte-identical if the types are emitted in XML order.
internal sealed class LocaleCollations
{
    public LocaleCollations(string locale, string? defaultType, IReadOnlyList<CollationTypeRules> types)
    {
        Locale = locale;
        DefaultType = defaultType;
        Types = types;
    }

    public string Locale { get; }

    public string? DefaultType { get; }

    public IReadOnlyList<CollationTypeRules> Types { get; }

    public bool IsRoot => Locale == "root";

    // Returns the named type's rules, or null if the locale doesn't define that type.
    public string? RulesFor(string type)
    {
        foreach (var collation in Types)
        {
            if (collation.Type == type)
            {
                return collation.Rules;
            }
        }
        return null;
    }
}
