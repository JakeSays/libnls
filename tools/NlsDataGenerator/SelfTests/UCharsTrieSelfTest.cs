using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.SelfTests;

// Dev self-test for the UCharsTrie builder: builds a trie from a fixed set of (string, value) pairs
// chosen to exercise every serialization path — final values vs. node values in 1/2/3-unit forms,
// list and split branches, linear-match chunking past 16 units, and suffix fusion — and writes the
// char16 serialization as little-endian bytes so the ICU UCharsTrieBuilder oracle's output can be
// byte-compared against it.
internal static class UCharsTrieSelfTest
{
    // Must be kept identical to the pairs in the C++ oracle.
    private static readonly (string S, int Value)[] Pairs =
    {
        ("a", 1),
        ("ab", 0x123456),
        ("abc", 5),
        ("abd", 6),
        ("b", 2),
        ("c", 3),
        ("d", 4),
        ("e", 5),
        ("f", 6),
        ("g", 7),
        ("ax", 100),
        ("bx", 100),
        ("mnopqrstuvwxyzABCDEF", 99),
        ("z", 0x3FFF0000),
        ("zz", 0x4000),
    };

    public static int Run(string outputPath)
    {
        var builder = new UCharsTrieBuilder();
        foreach (var pair in Pairs)
        {
            builder.Add(pair.S, pair.Value);
        }
        var units = builder.Build();

        var bytes = new byte[units.Length * 2];
        for (var i = 0; i < units.Length; ++i)
        {
            bytes[2 * i] = (byte)(units[i] & 0xFF);
            bytes[2 * i + 1] = (byte)(units[i] >> 8);
        }
        File.WriteAllBytes(outputPath, bytes);
        Console.Error.WriteLine($"wrote {outputPath} ({bytes.Length} bytes, {units.Length} units)");
        return 0;
    }
}
