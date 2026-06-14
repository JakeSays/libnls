namespace NlsDataGenerator.Parsing;

// CaseFolding.txt mappings, keyed by code point. Status letters: C = common (both simple and full
// fold to the same single value), S = simple-only, F = full-only, T = Turkic.
//   Simple fold (scf) comes from C and S rows; full fold (cf) from C and F rows; Turkic from T.
internal sealed class CaseFolding
{
    public Dictionary<int, int> SimpleFold { get; } = new();

    public Dictionary<int, int[]> FullFold { get; } = new();

    public Dictionary<int, int[]> TurkicFold { get; } = new();
}
