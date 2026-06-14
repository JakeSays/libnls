namespace NlsDataGenerator.Collation;

// Resolves [import langTag] by reading the imported locale's CLDR collation XML, replacing ICU's
// GenrbImporter (which read the locale's .txt). The langTag is already split into a locale ID and
// collation type by CollationRuleParser (via CollationImportTag); here we look up that type's rules.
// Matching ICU's GenrbImporter, a locale or type that is absent yields empty rules rather than an
// error — the imported collation simply contributes nothing.
internal sealed class CldrCollationImporter : ICollationImporter
{
    private readonly CldrCollationReader _reader;

    public CldrCollationImporter(CldrCollationReader reader)
    {
        _reader = reader;
    }

    public string GetRules(string localeId, string collationType)
    {
        var locale = _reader.Read(localeId);
        return locale.RulesFor(collationType) ?? "";
    }
}
