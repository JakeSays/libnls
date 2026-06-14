using System.Globalization;

namespace NlsDataGenerator.Parsing;

// Reads the decimal-digit code points (general category Nd) and their 0..9 values from
// UnicodeData.txt, in ascending order, for the collation DIGIT_TAG setup (genuca's setDigitTags
// iterates [:Nd:] with u_charDigitValue). Nd characters are listed individually, never in ranges.
internal static class DecimalDigitReader
{
    public static List<(int CodePoint, int DigitValue)> Read(string ucdDirectory)
    {
        var path = Path.Combine(ucdDirectory, "UnicodeData.txt");
        var digits = new List<(int CodePoint, int DigitValue)>();
        foreach (var line in File.ReadLines(path))
        {
            var fields = line.Split(';');
            if (fields.Length < 7 || fields[2] != "Nd" || fields[6].Length == 0)
            {
                continue;
            }
            var codePoint = int.Parse(fields[0], NumberStyles.HexNumber);
            digits.Add((codePoint, int.Parse(fields[6], CultureInfo.InvariantCulture)));
        }
        return digits;
    }
}
