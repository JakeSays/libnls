using NlsDataGenerator.Collation;
using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.SelfTests;

// End-to-end test of the full coll/<locale>.res path: read the CLDR collation XML (folding the
// extractor), build each collation type's %%CollationBin against the in-memory root, assemble the
// resource bundle, and diff the %%CollationBin blobs against a reference de.res (the little-endian one
// extracted from icudt78l.dat). ICU's coll/de.txt is phonebook-first while the CLDR XML / our output is
// search-first, so the container key pool differs; the blobs are order-independent binaries and are
// compared per collation type. On a search mismatch the ce32s/contexts arrays are diffed to localize it.
internal static class DeResSelfTest
{
    public static int Run(string ucdDirectory, string fractionalUcaPath, string uscriptHeaderPath,
        string cldrCollationDirectory, string deResPath)
    {
        var normalizationBuilder = new NormalizationDataBuilder();
        normalizationBuilder.LoadUcd(ucdDirectory);
        normalizationBuilder.Generate();
        var decimalDigits = DecimalDigitReader.Read(ucdDirectory);

        var rootData = CollationRootGenerator.BuildRootData(
            fractionalUcaPath, uscriptHeaderPath, normalizationBuilder.GetFcd16, decimalDigits,
            HanOrderKind.RadicalStroke);

        var reader = new CldrCollationReader(cldrCollationDirectory);
        var normalizer = normalizationBuilder.CreateNormalizer();
        var canon = normalizationBuilder.CreateCanonicalClosureData();
        var importer = new CldrCollationImporter(reader);
        var resourceBuilder = new CollationResourceBuilder(
            rootData, normalizer, canon, normalizationBuilder.GetFcd16, decimalDigits, importer, "48");

        // Build de search's CollationData directly so the in-memory ce32s/contexts arrays can be diffed
        // against the reference blob when the search %%CollationBin does not match.
        var searchBuilder = new CollationBuilder(
            rootData, normalizer, canon, normalizationBuilder.GetFcd16, decimalDigits);
        searchBuilder.DisableFastLatin();
        var searchData = searchBuilder.ParseAndBuild(reader.Read("de").RulesFor("search")!,
            new CollationSettings(), importer);

        var produced = resourceBuilder.Build(reader.Read("de"));
        if (produced is null)
        {
            Console.WriteLine("de produced no .res");
            return 1;
        }
        var reference = File.ReadAllBytes(deResPath);

        var producedBins = ReadBins(produced);
        var referenceBins = ReadBins(reference);
        var failed = false;
        foreach (var (type, producedBin) in producedBins)
        {
            if (!referenceBins.TryGetValue(type, out var referenceBin))
            {
                Console.WriteLine($"{type}: present in produced, absent in reference");
                failed = true;
                continue;
            }
            var differs = DiffBlob(type, producedBin, referenceBin);
            if (differs && type == "search")
            {
                DebugSearchArrays(searchData, referenceBin);
            }
            failed |= differs;
        }
        return failed ? 1 : 0;
    }

    private static bool DiffBlob(string type, byte[] produced, byte[] reference)
    {
        var limit = Math.Min(produced.Length, reference.Length);
        for (var i = 0; i < limit; ++i)
        {
            if (produced[i] != reference[i])
            {
                Console.WriteLine(
                    $"{type}: BYTE DIFFER at offset {i} (0x{i:x}): produced {produced.Length}B 0x{produced[i]:x2}, reference {reference.Length}B 0x{reference[i]:x2}");
                return true;
            }
        }
        if (produced.Length != reference.Length)
        {
            Console.WriteLine($"{type}: SIZE DIFFER produced {produced.Length}, reference {reference.Length} (common prefix matches)");
            return true;
        }
        Console.WriteLine($"{type}: %%CollationBin BYTE-IDENTICAL ({produced.Length} bytes)");
        return false;
    }

    // Diffs the in-memory de search ce32s/contexts against the reference blob's arrays to localize a
    // search divergence (the offsets 11/12/13/14 are CE32S/ROOT_ELEMENTS/CONTEXTS/UNSAFE_BWD).
    private static void DebugSearchArrays(CollationData data, byte[] referenceBlob)
    {
        var headerSize = referenceBlob[0] | (referenceBlob[1] << 8);
        int Index(int i)
        {
            var o = headerSize + i * 4;
            return referenceBlob[o] | (referenceBlob[o + 1] << 8)
                | (referenceBlob[o + 2] << 16) | (referenceBlob[o + 3] << 24);
        }
        var ce32sStart = headerSize + Index(11);
        var ce32sCount = (Index(12) - Index(11)) / 4;
        var refCe32s = new uint[ce32sCount];
        for (var i = 0; i < ce32sCount; ++i)
        {
            var o = ce32sStart + i * 4;
            refCe32s[i] = (uint)(referenceBlob[o] | (referenceBlob[o + 1] << 8)
                | (referenceBlob[o + 2] << 16) | (referenceBlob[o + 3] << 24));
        }
        var contextsStart = headerSize + Index(13);
        var contextsCount = (Index(14) - Index(13)) / 2;
        var refContexts = new char[contextsCount];
        for (var i = 0; i < contextsCount; ++i)
        {
            var o = contextsStart + i * 2;
            refContexts[i] = (char)(referenceBlob[o] | (referenceBlob[o + 1] << 8));
        }

        Console.WriteLine($"  ce32s: mine={data.Ce32sLength} ref={ce32sCount}");
        var ce32Limit = Math.Min(data.Ce32sLength, ce32sCount);
        for (var i = 0; i < ce32Limit; ++i)
        {
            if (data.Ce32s[i] != refCe32s[i])
            {
                Console.WriteLine($"  ce32s first differ at [{i}]: mine=0x{data.Ce32s[i]:x8} ref=0x{refCe32s[i]:x8}");
                break;
            }
        }
        Console.WriteLine($"  contexts: mine={data.ContextsLength} ref={contextsCount}");
        var ctxLimit = Math.Min(data.ContextsLength, contextsCount);
        for (var i = 0; i < ctxLimit; ++i)
        {
            if (data.Contexts[i] != refContexts[i])
            {
                Console.WriteLine($"  contexts first differ at [{i}]: mine=0x{(int)data.Contexts[i]:x4} ref=0x{(int)refContexts[i]:x4}");
                break;
            }
        }
    }

    private static Dictionary<string, byte[]> ReadBins(byte[] resBytes)
    {
        var reader = new ResourceBundle.ResourceBundleReader(resBytes);
        var collations = reader.ReadTable(reader.ReadTable(reader.RootWord)["collations"]);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (key, word) in collations)
        {
            var typeTable = reader.ReadTable(word);
            if (typeTable.TryGetValue("%%CollationBin", out var binWord))
            {
                result[key] = reader.ReadBinary(binWord);
            }
        }
        return result;
    }
}
