namespace NlsDataGenerator.Collation;

// One <collation type="..."> from a CLDR collation XML: the type name and its tailoring rules, with
// escapes already resolved (as genrb's lexer would). Rules is empty when the collation has no <cr>
// block (e.g. root's "standard", which is the root itself and carries no tailoring).
internal sealed class CollationTypeRules
{
    public CollationTypeRules(string type, string rules)
    {
        Type = type;
        Rules = rules;
    }

    public string Type { get; }

    public string Rules { get; }

    public bool HasRules => Rules.Length != 0;
}
