using System.Globalization;

namespace NlsDataGenerator.Parsing;

// Parses a UCD property-list file (DerivedCoreProperties.txt, PropList.txt) whose data lines read
// "CODE ; PropertyName" or "START..END ; PropertyName", into a code-point set per property name.
// Lines with a value (e.g. "; InSC=Consonant") keep the whole "Name=Value" as the key; the case
// build only queries the plain binary names (Lowercase, Uppercase, Cased, Case_Ignorable,
// Soft_Dotted).
internal sealed class PropertyListReader
{
    public static Dictionary<string, CodePointSet> Read(string path)
    {
        var properties = new Dictionary<string, CodePointSet>();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash];
            }
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(';');
            if (fields.Length < 2)
            {
                continue;
            }

            var name = fields[1].Trim();
            var (start, end) = ParseRange(fields[0].Trim());
            if (!properties.TryGetValue(name, out var set))
            {
                set = new CodePointSet();
                properties[name] = set;
            }
            set.Add(start, end);
        }

        return properties;
    }

    private static (int Start, int End) ParseRange(string field)
    {
        var dots = field.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            var single = int.Parse(field, NumberStyles.HexNumber);
            return (single, single);
        }

        var start = int.Parse(field[..dots], NumberStyles.HexNumber);
        var end = int.Parse(field[(dots + 2)..], NumberStyles.HexNumber);
        return (start, end);
    }
}
