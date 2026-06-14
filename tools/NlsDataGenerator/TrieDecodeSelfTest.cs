using NlsDataGenerator.Collation;
using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator;

// Decodes a collation type's %%CollationBin trie from a .res and tallies the CE32 tag each code point
// maps to across the CJK ranges, to see exactly how the reference encodes tailored Han (OFFSET_TAG
// ranges vs explicit long-primary CE32s) rather than inferring it from section sizes.
internal static class TrieDecodeSelfTest
{
    public static int Run(string resPath, string type)
    {
        var reader = new ResourceBundleReader(File.ReadAllBytes(resPath));
        var collations = reader.ReadTable(reader.ReadTable(reader.RootWord)["collations"]);
        var typeTable = reader.ReadTable(collations[type]);
        if (typeTable.TryGetValue("Sequence", out var seqWord))
        {
            var seq = reader.ReadString(seqWord);
            var bmpCjk = seq.Count(ch => ch is (>= '㐀' and <= '鿿') or (>= '豈' and <= '﫿'));
            var supp = 0;
            for (var i = 0; i < seq.Length; ++i)
            {
                if (char.IsHighSurrogate(seq[i]))
                {
                    ++supp;
                }
            }
            Console.WriteLine($"{type} Sequence: length={seq.Length} bmpCjk={bmpCjk} supplementary={supp}");
        }
        var blob = reader.ReadBinary(typeTable["%%CollationBin"]);

        var headerSize = blob[0] | (blob[1] << 8);
        int Index(int i)
        {
            var o = headerSize + i * 4;
            return blob[o] | (blob[o + 1] << 8) | (blob[o + 2] << 16) | (blob[o + 3] << 24);
        }
        var trieStart = headerSize + Index(7);
        var trieEnd = headerSize + Index(8);
        var trie = new Utrie2Reader(blob[trieStart..trieEnd]);

        var ranges = new (int Start, int End, string Name)[]
        {
            (0x3400, 0x4DBF, "ExtA"),
            (0x4E00, 0x9FFF, "Unified"),
            (0xF900, 0xFAFF, "Compat"),
            (0x20000, 0x2A6DF, "ExtB"),
            (0x2A700, 0x2EBEF, "ExtC-F"),
        };
        Console.WriteLine($"{type}: trieBytes={trieEnd - trieStart}");
        foreach (var (start, end, name) in ranges)
        {
            var tags = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var c = start; c <= end; ++c)
            {
                var ce32 = trie.Get32(c);
                var tag = Collator.IsSpecialCe32(ce32)
                    ? TagName(Collator.TagFromCe32(ce32))
                    : "simple";
                tags.TryGetValue(tag, out var n);
                tags[tag] = n + 1;
            }
            var parts = tags.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}");
            Console.WriteLine($"  {name} [{start:X}-{end:X}]: {string.Join(" ", parts)}");
        }
        return 0;
    }

    private static string TagName(int tag)
    {
        return tag switch
        {
            Collator.LongPrimaryTag => "longPrimary",
            Collator.LongSecondaryTag => "longSecondary",
            Collator.LatinExpansionTag => "latinExpansion",
            Collator.Expansion32Tag => "expansion32",
            Collator.ExpansionTag => "expansion",
            Collator.PrefixTag => "prefix",
            Collator.ContractionTag => "contraction",
            Collator.OffsetTag => "offset",
            Collator.ImplicitTag => "implicit",
            Collator.FallbackTag => "fallback",
            Collator.HangulTag => "hangul",
            _ => $"tag{tag}",
        };
    }
}
