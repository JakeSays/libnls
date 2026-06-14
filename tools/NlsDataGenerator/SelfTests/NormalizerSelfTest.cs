using System.Text;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.SelfTests;

// Validates BuildTimeNormalizer against ICU. It builds the normalization data from the UCD, then
// dumps every "interesting" code point's normalization properties (combining class, decompose-inert,
// FCD16, NFC boundary-before, canonical decomposition) plus the NFD/isNormalized/isFCD results for a
// list of test strings. The C++ normalizer oracle dumps the same format from ICU's own Normalizer2;
// the two are diffed.
internal static class NormalizerSelfTest
{
    public static int Run(string ucdDirectory, string stringsPath)
    {
        var builder = new NormalizationDataBuilder();
        builder.LoadUcd(ucdDirectory);
        builder.Generate();
        var normalizer = builder.CreateNormalizer();

        var output = new StringBuilder();
        for (var c = 0; c <= Unicode.MaxCodePoint; ++c)
        {
            if (c >= Unicode.LeadSurrogateMin && c <= Unicode.TrailSurrogateMax)
            {
                continue;
            }
            var cc = normalizer.GetCombiningClass(c);
            var fcd16 = normalizer.GetFcd16(c);
            var hasBoundaryBefore = normalizer.HasCompBoundaryBefore(c);
            var decomposition = normalizer.GetDecomposition(c);
            if (cc == 0 && fcd16 == 0 && hasBoundaryBefore && decomposition is null)
            {
                continue;
            }
            var inert = normalizer.IsInert(c) ? 1 : 0;
            output.Append("C ")
                .Append(c.ToString("x4")).Append(' ')
                .Append(cc).Append(' ')
                .Append(inert).Append(' ')
                .Append(fcd16.ToString("x4")).Append(' ')
                .Append(hasBoundaryBefore ? 1 : 0).Append(' ')
                .Append(HexCodePoints(decomposition)).Append('\n');
        }

        foreach (var line in File.ReadLines(stringsPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var input = FromHexCodePoints(trimmed);
            var nfd = normalizer.Normalize(input);
            output.Append("S ")
                .Append(HexCodePoints(input)).Append(' ')
                .Append(HexCodePoints(nfd)).Append(' ')
                .Append(normalizer.IsNormalized(input) ? 1 : 0).Append(' ')
                .Append(normalizer.IsFcdNormalized(input) ? 1 : 0).Append('\n');
        }

        Console.Out.Write(output.ToString());
        return 0;
    }

    private static string HexCodePoints(string? s)
    {
        if (s is null || s.Length == 0)
        {
            return "-";
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
