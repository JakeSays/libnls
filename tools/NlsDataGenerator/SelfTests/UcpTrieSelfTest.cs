using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.SelfTests;

// Dev self-test for the UCPTrie serializer: builds a trie with a known value map, writes the
// serialized bytes, and prints each probe code point's expected value so the C++ ucptrie-oracle's
// ICU readback can be diffed against it.
internal static class UcpTrieSelfTest
{
    private static readonly (int CodePoint, uint Value)[] Probes =
    {
        (0x41, 100),
        (0x42, 0),
        (0x61, 200),
        (0x100, 300),
        (0x150, 300),
        (0x180, 0),
        (0x4E00, 400),
        (0x4E01, 0),
        (0x1F600, 500),
        (0x1F64F, 500),
        (0x1F650, 0),
        (0x10FFFF, 0),
    };

    public static int Run(string outputPath)
    {
        var builder = new CodePointTrieBuilder(0, 0xFFFF);
        builder.Set(0x41, 100);
        builder.Set(0x61, 200);
        builder.SetRange(0x100, 0x17F, 300);
        builder.Set(0x4E00, 400);
        builder.SetRange(0x1F600, 0x1F64F, 500);

        var bytes = builder.Build();
        File.WriteAllBytes(outputPath, bytes);

        foreach (var probe in Probes)
        {
            Console.WriteLine($"{probe.CodePoint:X4}={probe.Value}");
        }
        Console.Error.WriteLine($"wrote {outputPath} ({bytes.Length} bytes)");
        return 0;
    }
}
