namespace NlsDataGenerator.Collation;

// Resolves [import locale] directives in collation rules, ported from CollationRuleParser::Importer.
// Given a locale ID and collation type, it returns that collation's rule string, which the parser
// then parses inline. The implementation reads the CLDR collation XML for the imported locale.
internal interface ICollationImporter
{
    string GetRules(string localeId, string collationType);
}
