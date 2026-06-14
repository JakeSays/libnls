using NlsDataGenerator.Collation;
using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;
using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator;

// End-to-end test of the collation tailoring builder: builds the de phonebook %%CollationBin from its
// rules and diffs it against the blob genrb embedded in de.res. This exercises the whole chain — root
// data, rule parser, weight-graph builder, CE engine, canonical closure, and writeTailoring.
internal static class DeBlobSelfTest
{
    public static int Run(string ucdDirectory, string fractionalUcaPath, string uscriptHeaderPath,
        string deResPath)
    {
        var normalizationBuilder = new NormalizationDataBuilder();
        normalizationBuilder.LoadUcd(ucdDirectory);
        normalizationBuilder.Generate();
        var normalizer = normalizationBuilder.CreateNormalizer();
        var canon = normalizationBuilder.CreateCanonicalClosureData();
        var decimalDigits = DecimalDigitReader.Read(ucdDirectory);
        Func<int, int> getFcd16 = normalizationBuilder.GetFcd16;

        var rootData = CollationRootGenerator.BuildRootData(
            fractionalUcaPath, uscriptHeaderPath, getFcd16, decimalDigits, HanOrderKind.RadicalStroke);

        var builder = new CollationBuilder(rootData, normalizer, canon, getFcd16, decimalDigits);
        var settings = new CollationSettings();
        const string rules = "&AE<<ä<<<Ä&OE<<ö<<<Ö&UE<<ü<<<Ü";
        var tailoringData = builder.ParseAndBuild(rules, settings, null);

        byte[] rulesVersion = [48, 0, 0, 0];
        var produced = CollationDataWriter.WriteTailoring(tailoringData, settings, rulesVersion);

        var reader = new ResourceBundleReader(File.ReadAllBytes(deResPath));
        var collations = reader.ReadTable(reader.RootWord)["collations"];
        var phonebook = reader.ReadTable(reader.ReadTable(collations)["phonebook"]);
        var reference = reader.ReadBinary(phonebook["%%CollationBin"]);

        if (produced.Length != reference.Length)
        {
            Console.WriteLine($"SIZE DIFFER: produced {produced.Length}, reference {reference.Length}");
        }
        var limit = Math.Min(produced.Length, reference.Length);
        for (var i = 0; i < limit; ++i)
        {
            if (produced[i] != reference[i])
            {
                Console.WriteLine($"BYTE DIFFER at offset {i} (0x{i:x}): "
                    + $"produced 0x{produced[i]:x2}, reference 0x{reference[i]:x2}");
                return 1;
            }
        }
        if (produced.Length != reference.Length)
        {
            return 1;
        }
        Console.WriteLine($"DE PHONEBOOK BLOB BYTE-IDENTICAL ({produced.Length} bytes)");
        return 0;
    }
}
