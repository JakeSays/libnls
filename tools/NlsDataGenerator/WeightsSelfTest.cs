using NlsDataGenerator.Collation;

namespace NlsDataGenerator;

// Runs CollationWeights over a list of cases and prints the allocated weight sequence in the same
// format as the C++ weights oracle, so the two can be diffed. Each input line is
// "<level p|s|t> <compressible 0|1> <lowerHex> <upperHex> <n>".
internal static class WeightsSelfTest
{
    public static int Run(string casesPath)
    {
        foreach (var line in File.ReadLines(casesPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var level = fields[0][0];
            var compressible = fields[1] != "0";
            var lower = uint.Parse(fields[2], System.Globalization.NumberStyles.HexNumber);
            var upper = uint.Parse(fields[3], System.Globalization.NumberStyles.HexNumber);
            var n = int.Parse(fields[4]);

            Console.WriteLine($"=== {level} {(compressible ? 1 : 0)} {lower:x8} {upper:x8} {n} ===");
            var weights = new CollationWeights();
            if (level == 'p')
            {
                weights.InitForPrimary(compressible);
            }
            else if (level == 's')
            {
                weights.InitForSecondary();
            }
            else
            {
                weights.InitForTertiary();
            }
            if (!weights.AllocWeights(lower, upper, n))
            {
                Console.WriteLine("FAIL");
                continue;
            }
            for (var i = 0; i < n; ++i)
            {
                Console.WriteLine($"{weights.NextWeight():x8}");
            }
        }
        return 0;
    }
}
