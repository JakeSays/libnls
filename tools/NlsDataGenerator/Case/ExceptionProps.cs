namespace NlsDataGenerator.Case;

// Per-code-point data captured during the value computation for every code point that needs an
// exception entry. makeException turns this into the encoded exception slots. Simple mappings are
// -1 when absent; full mappings are empty arrays.
internal sealed class ExceptionProps
{
    public required int CodePoint { get; init; }

    public int SimpleLower { get; init; } = -1;

    public int SimpleUpper { get; init; } = -1;

    public int SimpleTitle { get; init; } = -1;

    public int SimpleFold { get; init; } = -1;

    public int Delta { get; init; }

    public int[] FullLower { get; init; } = [];

    public int[] FullUpper { get; init; } = [];

    public int[] FullTitle { get; init; } = [];

    public int[] FullFold { get; init; } = [];

    public int[] Turkic { get; init; } = [];

    public bool HasConditional { get; init; }

    public bool HasTurkic { get; init; }

    public bool HasNoSimpleCaseFolding { get; init; }

    // The case-closure code points (sorted, unique), filled by the closure pass before makeException.
    public SortedSet<int> Closure { get; } = [];
}
