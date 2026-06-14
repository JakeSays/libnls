namespace NlsDataGenerator.Parsing;

// A set of code points stored as a list of inclusive ranges, built from UCD property files and
// queried by Contains during per-code-point value computation. A linear scan is adequate: the
// case-relevant properties hold a few thousand ranges and are queried only for code points that
// already carry case data.
internal sealed class CodePointSet
{
    private readonly List<(int Start, int End)> _ranges = [];

    public void Add(int start, int end)
    {
        _ranges.Add((start, end));
    }

    public bool Contains(int codePoint)
    {
        foreach (var range in _ranges)
        {
            if (codePoint >= range.Start && codePoint <= range.End)
            {
                return true;
            }
        }
        return false;
    }

    public IEnumerable<int> Members
    {
        get
        {
            foreach (var range in _ranges)
            {
                for (var codePoint = range.Start; codePoint <= range.End; codePoint++)
                {
                    yield return codePoint;
                }
            }
        }
    }
}
