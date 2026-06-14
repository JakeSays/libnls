using System.Globalization;

namespace NlsDataGenerator.Parsing;

// Parses SpecialCasing.txt: "code; lower; title; upper; (condition;)? # comment". A non-empty
// condition field marks a conditional row (flag only); otherwise the row's full mappings are
// recorded.
internal sealed class SpecialCasingReader
{
    public static SpecialCasing Read(string path)
    {
        var casing = new SpecialCasing();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line.Substring(0, hash);
            }
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(';');
            if (fields.Length < 4)
            {
                continue;
            }

            var codePoint = int.Parse(fields[0].Trim(), NumberStyles.HexNumber);
            var hasCondition = fields.Length >= 5 && fields[4].Trim().Length != 0;
            if (hasCondition)
            {
                casing.HasConditional.Add(codePoint);
                continue;
            }

            casing.FullLower[codePoint] = ParseScalars(fields[1]);
            casing.FullTitle[codePoint] = ParseScalars(fields[2]);
            casing.FullUpper[codePoint] = ParseScalars(fields[3]);
        }

        return casing;
    }

    private static int[] ParseScalars(string field)
    {
        var tokens = field.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scalars = new int[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            scalars[i] = int.Parse(tokens[i], NumberStyles.HexNumber);
        }

        return scalars;
    }
}
