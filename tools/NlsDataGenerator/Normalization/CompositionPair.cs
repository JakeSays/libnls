namespace NlsDataGenerator.Normalization;

// A (trailing character, composite) pair: a starter combines with Trail to form Composite.
internal readonly struct CompositionPair
{
    public CompositionPair(int trail, int composite)
    {
        Trail = trail;
        Composite = composite;
    }

    public int Trail { get; }

    public int Composite { get; }
}
