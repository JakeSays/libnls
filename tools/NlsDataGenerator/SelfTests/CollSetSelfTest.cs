using NlsDataGenerator.Collation;
using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;
using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator.SelfTests;

// Validates the whole collation locale set: builds each coll/<locale>.res from CLDR (folding the
// extractor) and diffs every %%CollationBin blob against the reference little-endian .res extracted
// from icudt78l.dat. ICU's container order differs (phonebook-first), so blobs are compared per type.
internal static class CollSetSelfTest
{
    // Locales whose icudt78 reference is built from a smaller dataset than libese ships, so a byte
    // comparison against it is not meaningful. zh: libese ships CLDR's COMPREHENSIVE collation coverage
    // (every CJK extension gets an explicit pinyin/stroke position); the reference uses the MODERN-
    // coverage production data — cldr-to-icu consumes CLDR's GenerateProductionData output, which trims
    // rare CJK (pinyin ~196 vs ~17712 supplementary kanji). The full data is the deliberate choice for a
    // storage engine (correct, stable index ordering for all stored characters), so this is a reference-
    // dataset mismatch, not a generator defect. See /p/ese/nls-data-gen-handoff.md.
    private static readonly HashSet<string> ExemptedLocales = new(StringComparer.Ordinal) { "zh" };

    public static int Run(string ucdDirectory, string fractionalUcaPath, string uscriptHeaderPath,
        string cldrCollationDirectory, string ucaRulesPath, string referenceCollDirectory)
    {
        var normalizationBuilder = new NormalizationDataBuilder();
        normalizationBuilder.LoadUcd(ucdDirectory);
        normalizationBuilder.Generate();
        var decimalDigits = DecimalDigitReader.Read(ucdDirectory);
        var rootData = CollationRootGenerator.BuildRootData(
            fractionalUcaPath, uscriptHeaderPath, normalizationBuilder.GetFcd16, decimalDigits,
            HanOrderKind.RadicalStroke);
        var reader = new CldrCollationReader(cldrCollationDirectory);
        var ucaRules = File.Exists(ucaRulesPath) ? File.ReadAllText(ucaRulesPath) : null;
        var resourceBuilder = new CollationResourceBuilder(
            rootData, normalizationBuilder.CreateNormalizer(),
            normalizationBuilder.CreateCanonicalClosureData(), normalizationBuilder.GetFcd16,
            decimalDigits, new CldrCollationImporter(reader), "48", ucaRules);

        var pass = 0;
        var fail = 0;
        var skip = 0;
        var threw = 0;
        var exempt = 0;
        var failures = new List<string>();
        foreach (var referenceFile in Directory.GetFiles(referenceCollDirectory, "*.res").OrderBy(f => f))
        {
            var locale = Path.GetFileNameWithoutExtension(referenceFile);
            if (ExemptedLocales.Contains(locale))
            {
                ++exempt;
                continue;
            }
            if (!File.Exists(Path.Combine(cldrCollationDirectory, $"{locale}.xml")))
            {
                ++skip;
                continue;
            }
            byte[]? produced;
            try
            {
                produced = resourceBuilder.Build(reader.Read(locale));
            }
            catch (Exception error)
            {
                ++threw;
                failures.Add($"{locale}: THREW {error.GetType().Name}: {error.Message}");
                continue;
            }
            if (produced is null)
            {
                // The locale defines no own collation types (it inherits root), so there is nothing to
                // build. That is only legitimate if the reference also has no %%CollationBin; a reference
                // blob here would mean mine is failing to produce real collation data (a gap, not a skip).
                var referenceBlobCount = 0;
                try
                {
                    referenceBlobCount = ReadBins(File.ReadAllBytes(referenceFile)).Count;
                }
                catch
                {
                    // Unreadable reference; treated as a skip below.
                }
                if (referenceBlobCount > 0)
                {
                    ++fail;
                    failures.Add($"{locale}: GAP — mine produced no blob but the reference has {referenceBlobCount} %%CollationBin");
                    continue;
                }
                ++skip;
                continue;
            }

            Dictionary<string, byte[]> referenceBins;
            try
            {
                referenceBins = ReadBins(File.ReadAllBytes(referenceFile));
            }
            catch (Exception error)
            {
                ++skip;
                failures.Add($"{locale}: reference unreadable ({error.Message})");
                continue;
            }
            Dictionary<string, byte[]> producedBins;
            try
            {
                producedBins = ReadBins(produced);
            }
            catch (Exception error)
            {
                ++fail;
                failures.Add($"{locale}: produced unreadable ({error.GetType().Name}: {error.Message})");
                continue;
            }
            var localeFailed = false;
            foreach (var (type, producedBin) in producedBins)
            {
                if (!referenceBins.TryGetValue(type, out var referenceBin))
                {
                    localeFailed = true;
                    failures.Add($"{locale}/{type}: absent in reference");
                }
                else if (!producedBin.AsSpan().SequenceEqual(referenceBin))
                {
                    localeFailed = true;
                    failures.Add($"{locale}/{type}: DIFFER (mine {producedBin.Length}, ref {referenceBin.Length}) {DiffIndexes(producedBin, referenceBin)}");
                }
            }
            // Reverse direction: a collation type the reference has but mine never produced is a gap,
            // not a pass — the per-produced-type loop above would otherwise never see it.
            foreach (var type in referenceBins.Keys)
            {
                if (!producedBins.ContainsKey(type))
                {
                    localeFailed = true;
                    failures.Add($"{locale}/{type}: MISSING — reference has this type, mine produced none");
                }
            }
            if (localeFailed)
            {
                ++fail;
            }
            else
            {
                ++pass;
            }
        }

        Console.WriteLine(
            $"=== collation locale set: PASS={pass} FAIL={fail} THREW={threw} SKIP={skip} EXEMPT={exempt} ===");
        if (exempt > 0)
        {
            Console.WriteLine($"  exempt (reference uses smaller dataset): {string.Join(", ", ExemptedLocales)}");
        }
        foreach (var failure in failures.Take(60))
        {
            Console.WriteLine($"  {failure}");
        }
        if (failures.Count > 60)
        {
            Console.WriteLine($"  … and {failures.Count - 60} more");
        }
        return fail + threw == 0 ? 0 : 1;
    }

    // The %%CollationBin index array (CollationDataReader): a uint16 header size, then int32 indexes.
    // Indexes 5..19 are cumulative byte offsets, so index[i]-index[i-1] is the byte size of the
    // section starting at index[i-1]. Reports the sections whose size differs (mine - ref).
    private static string DiffIndexes(byte[] mine, byte[] reference)
    {
        // Named by the section that STARTS at each offset index.
        var names = new Dictionary<int, string>
        {
            [5] = "reorderCodes", [6] = "reorderTable", [7] = "trie", [8] = "reserved8", [9] = "ces",
            [10] = "reserved10", [11] = "ce32s", [12] = "rootElements", [13] = "contexts",
            [14] = "unsafeBwd", [15] = "fastLatin", [16] = "scripts", [17] = "compressibleBytes",
            [18] = "reserved18",
        };
        int Index(byte[] blob, int i)
        {
            var headerSize = blob[0] | (blob[1] << 8);
            var o = headerSize + i * 4;
            return blob[o] | (blob[o + 1] << 8) | (blob[o + 2] << 16) | (blob[o + 3] << 24);
        }
        var length = Math.Min(Index(mine, 0), Index(reference, 0));
        var parts = new List<string>();
        for (var i = 6; i < length; ++i)
        {
            var mineSpan = Index(mine, i) - Index(mine, i - 1);
            var refSpan = Index(reference, i) - Index(reference, i - 1);
            if (mineSpan != refSpan)
            {
                parts.Add($"{(names.TryGetValue(i - 1, out var n) ? n : (i - 1).ToString())}:{mineSpan - refSpan:+0;-0}");
            }
        }
        return parts.Count == 0 ? "(section sizes equal; reorder ahead of indexes?)" : string.Join(" ", parts);
    }

    private static Dictionary<string, byte[]> ReadBins(byte[] resBytes)
    {
        var reader = new ResourceBundleReader(resBytes);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var root = reader.ReadTable(reader.RootWord);
        if (!root.TryGetValue("collations", out var collationsWord))
        {
            return result;
        }
        var collations = reader.ReadTable(collationsWord);
        foreach (var (key, word) in collations)
        {
            if (key == "default")
            {
                continue;
            }
            var typeTable = reader.ReadTable(word);
            if (typeTable.TryGetValue("%%CollationBin", out var binWord))
            {
                result[key] = reader.ReadBinary(binWord);
            }
        }
        return result;
    }
}
