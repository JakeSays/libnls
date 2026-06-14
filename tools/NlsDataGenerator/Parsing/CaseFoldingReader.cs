using System.Globalization;

namespace NlsDataGenerator.Parsing;

// Parses CaseFolding.txt: "code; status; mapping;" rows where status is C/S/F/T.
internal sealed class CaseFoldingReader
{
    public static CaseFolding Read(string path)
    {
        var folding = new CaseFolding();
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
            if (fields.Length < 3)
            {
                continue;
            }

            var codePoint = int.Parse(fields[0].Trim(), NumberStyles.HexNumber);
            var status = fields[1].Trim();
            var mapping = ParseScalars(fields[2]);

            switch (status)
            {
                case "C":
                    folding.SimpleFold[codePoint] = mapping[0];
                    folding.FullFold[codePoint] = mapping;
                    break;

                case "S":
                    folding.SimpleFold[codePoint] = mapping[0];
                    break;

                case "F":
                    folding.FullFold[codePoint] = mapping;
                    break;

                case "T":
                    folding.TurkicFold[codePoint] = mapping;
                    break;
            }
        }

        return folding;
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
