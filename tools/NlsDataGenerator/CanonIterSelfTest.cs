using System.Text;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator;

// Validates CanonicalClosureData and CanonicalIterator against ICU. Per "interesting" code point it
// dumps the canonical start set (sorted) and whether the code point is a canonical segment starter;
// per test string it dumps the full set of canonically-equivalent strings (sorted, since iteration
// order is implementation-defined). The C++ canon-iter oracle emits the same from ICU's own
// Normalizer2Impl + CanonicalIterator; the two are diffed.
internal static class CanonIterSelfTest
{
    public static int Run(string ucdDirectory, string stringsPath)
    {
        var builder = new NormalizationDataBuilder();
        builder.LoadUcd(ucdDirectory);
        builder.Generate();
        var normalizer = builder.CreateNormalizer();
        var canon = builder.CreateCanonicalClosureData();

        var output = new StringBuilder();
        var startSet = new SortedSet<int>();
        for (var c = 0; c <= Unicode.MaxCodePoint; ++c)
        {
            if (c >= Unicode.LeadSurrogateMin && c <= Unicode.TrailSurrogateMax)
            {
                continue;
            }
            var hasSet = canon.GetCanonStartSet(c, startSet);
            var starter = canon.IsCanonSegmentStarter(c);
            if (!hasSet && starter)
            {
                continue;
            }
            output.Append("C ")
                .Append(c.ToString("x4")).Append(' ')
                .Append(starter ? 1 : 0).Append(' ')
                .Append(hasSet ? string.Join('.', startSet.Select(x => x.ToString("x4"))) : "-")
                .Append('\n');
        }

        foreach (var line in File.ReadLines(stringsPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var input = FromHexCodePoints(trimmed);
            var iterator = new CanonicalIterator(normalizer, canon, input);
            var results = new SortedSet<string>(StringComparer.Ordinal);
            for (var s = iterator.Next(); s is not null; s = iterator.Next())
            {
                results.Add(s);
            }
            output.Append("S ")
                .Append(HexCodePoints(input)).Append(' ')
                .Append(results.Count).Append(' ')
                .Append(string.Join('|', results.Select(HexCodePoints)))
                .Append('\n');
        }

        Console.Out.Write(output.ToString());
        return 0;
    }

    private static string HexCodePoints(string s)
    {
        if (s.Length == 0)
        {
            return "_";
        }
        var parts = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            parts.Add(c.ToString("x4"));
        }
        return string.Join('.', parts);
    }

    private static string FromHexCodePoints(string line)
    {
        var builder = new StringBuilder();
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = int.Parse(token, System.Globalization.NumberStyles.HexNumber);
            builder.Append(char.ConvertFromUtf32(c));
        }
        return builder.ToString();
    }
}
