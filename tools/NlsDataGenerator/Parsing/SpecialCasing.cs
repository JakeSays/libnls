namespace NlsDataGenerator.Parsing;

// SpecialCasing.txt mappings. The unconditional full mappings (lower/title/upper) go into the
// ucase exception slots; code points that also carry a conditional row (a language tag or a
// context such as Final_Sigma) are flagged so the runtime applies its special-casing logic.
internal sealed class SpecialCasing
{
    public Dictionary<int, int[]> FullLower { get; } = new();

    public Dictionary<int, int[]> FullTitle { get; } = new();

    public Dictionary<int, int[]> FullUpper { get; } = new();

    public HashSet<int> HasConditional { get; } = new();
}
