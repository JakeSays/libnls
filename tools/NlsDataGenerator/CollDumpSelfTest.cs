using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator;

// Dumps each collation type's %%CollationBin index table from a .res, for diagnosing size differences
// (e.g. the contexts section length that overflows the context index for ja).
internal static class CollDumpSelfTest
{
    public static int Run(string resPath)
    {
        var reader = new ResourceBundleReader(File.ReadAllBytes(resPath));
        var root = reader.ReadTable(reader.RootWord);
        if (!root.TryGetValue("collations", out var collationsWord))
        {
            Console.WriteLine("no collations");
            return 0;
        }
        var collations = reader.ReadTable(collationsWord);
        foreach (var (key, word) in collations)
        {
            Console.WriteLine($"-- {key}: word=0x{word:x8} type={ResourceBundle.ResourceType.GetType(word)} offset={ResourceBundle.ResourceType.GetOffset(word)}");
            if (key == "default")
            {
                continue;
            }
            var typeTable = reader.ReadTable(word);
            Console.WriteLine($"   {key} keys: {string.Join(", ", typeTable.Keys)}");
            if (!typeTable.TryGetValue("%%CollationBin", out var binWord))
            {
                Console.WriteLine($"{key}: (no %%CollationBin)");
                continue;
            }
            Console.WriteLine($"   {key}/%%CollationBin: word=0x{binWord:x8} type={ResourceBundle.ResourceType.GetType(binWord)} offset={ResourceBundle.ResourceType.GetOffset(binWord)}");
            var blob = reader.ReadBinary(binWord);
            var headerSize = blob[0] | (blob[1] << 8);
            int Index(int i)
            {
                var o = headerSize + i * 4;
                return blob[o] | (blob[o + 1] << 8) | (blob[o + 2] << 16) | (blob[o + 3] << 24);
            }
            var len = Index(0);
            // TRIE=7, RESERVED8=8, CES=9, RESERVED10=10, CE32S=11, ROOT_ELEM=12, CONTEXTS=13, UNSAFE=14.
            var trie = len > 8 ? Index(8) - Index(7) : 0;
            var ces = len > 10 ? (Index(10) - Index(9)) / 8 : 0;
            var ce32s = len > 12 ? (Index(12) - Index(11)) / 4 : 0;
            var contexts = len > 14 ? (Index(14) - Index(13)) / 2 : 0;
            Console.WriteLine($"{key}: total={blob.Length} trie={trie} ces={ces} ce32s={ce32s} contexts={contexts}");
        }
        return 0;
    }
}
