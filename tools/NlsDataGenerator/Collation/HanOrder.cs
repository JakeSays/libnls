namespace NlsDataGenerator.Collation;

// Collects the Unified_Ideograph ranges for one Han ordering (implicit or radical-stroke), ported
// from genuca's HanOrder. Ranges are stored as flat (start, end) pairs, merging adjacent ones; the
// set is kept to cross-check that the radical-stroke order covers exactly the implicit set.
internal sealed class HanOrder
{
    private readonly List<int> _ranges = [];

    public UnicodeSet Set { get; } = new();

    public bool Done { get; set; }

    public void AddRange(int start, int end)
    {
        if (_ranges.Count > 0 && _ranges[^1] + 1 == start)
        {
            // The previous range ends just before this one: merge them.
            _ranges[^1] = end;
        }
        else
        {
            _ranges.Add(start);
            _ranges.Add(end);
        }
        Set.Add(start, end);
    }

    public void Apply(CollationBaseDataBuilder builder)
    {
        builder.InitHanRanges([.. _ranges]);
        Done = true;
    }
}
