namespace NlsDataGenerator.Parsing;

// Parses UnicodeData.txt (the core UCD file) into records. Handles the "First>"/"Last>" range
// pairs by collapsing each pair into a single range record.
internal sealed class UnicodeDataReader
{
    private readonly string _ucdDirectory;

    public UnicodeDataReader(string ucdDirectory)
    {
        _ucdDirectory = ucdDirectory;
    }

    public List<UnicodeDataRecord> Read()
    {
        var path = Path.Combine(_ucdDirectory, "UnicodeData.txt");
        var records = new List<UnicodeDataRecord>();
        int? pendingRangeStart = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split(';');
            var codePoint = int.Parse(fields[0], System.Globalization.NumberStyles.HexNumber);
            var name = fields[1];

            // A range is two consecutive rows whose names end in ", First>" and ", Last>".
            if (name.EndsWith(", First>", StringComparison.Ordinal))
            {
                pendingRangeStart = codePoint;
                continue;
            }

            if (name.EndsWith(", Last>", StringComparison.Ordinal) && pendingRangeStart is int rangeStart)
            {
                records.Add(ParseRow(fields, rangeStart, codePoint));
                pendingRangeStart = null;
                continue;
            }

            records.Add(ParseRow(fields, codePoint, codePoint));
        }

        return records;
    }

    private static UnicodeDataRecord ParseRow(string[] fields, int codePoint, int rangeLast)
    {
        var generalCategory = fields[2];
        var combiningClass = int.Parse(fields[3]);
        var (decompositionTag, decompositionMapping) = ParseDecomposition(fields[5]);
        var simpleUppercase = ParseOptionalScalar(fields[12]);
        var simpleLowercase = ParseOptionalScalar(fields[13]);
        var simpleTitlecase = ParseOptionalScalar(fields[14]);

        return new UnicodeDataRecord(
            codePoint,
            rangeLast,
            generalCategory,
            combiningClass,
            decompositionTag,
            decompositionMapping,
            simpleUppercase,
            simpleLowercase,
            simpleTitlecase);
    }

    // Field 5 is "<tag> XXXX YYYY" for compatibility mappings, "XXXX YYYY" for canonical, or empty.
    private static (string? Tag, int[] Mapping) ParseDecomposition(string field)
    {
        if (field.Length == 0)
        {
            return (null, Array.Empty<int>());
        }

        string? tag = null;
        var tokens = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scalars = new List<int>();
        foreach (var token in tokens)
        {
            if (token.StartsWith('<'))
            {
                tag = token.Trim('<', '>');
                continue;
            }

            scalars.Add(int.Parse(token, System.Globalization.NumberStyles.HexNumber));
        }

        return (tag, scalars.ToArray());
    }

    private static int? ParseOptionalScalar(string field)
    {
        if (field.Length == 0)
        {
            return null;
        }

        return int.Parse(field, System.Globalization.NumberStyles.HexNumber);
    }
}
