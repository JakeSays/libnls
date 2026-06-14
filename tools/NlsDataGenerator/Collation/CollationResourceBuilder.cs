using NlsDataGenerator.Normalization;
using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator.Collation;

// Assembles one locale's coll/<locale>.res, replacing genrb's parseCollationElements/addCollation.
// For each collation type it stores the Sequence rules and Version, and (unless the type is empty or
// an import-only "private-*" rule string) builds the %%CollationBin tailoring blob from the rules
// against the root. Children are added in genrb's construction order — Sequence, Version, then the
// %%CollationBin appended last — because the resource bundle key pool follows construction order, and
// the .res is only byte-identical to genrb's when that order matches.
internal sealed class CollationResourceBuilder
{
    private readonly CollationData _rootData;
    private readonly BuildTimeNormalizer _normalizer;
    private readonly CanonicalClosureData _canon;
    private readonly Func<int, int> _getFcd16;
    private readonly IReadOnlyList<(int CodePoint, int DigitValue)> _decimalDigits;
    private readonly ICollationImporter _importer;
    private readonly string _version;
    private readonly byte[] _rulesVersion;
    private readonly string? _ucaRules;

    public CollationResourceBuilder(
        CollationData rootData,
        BuildTimeNormalizer normalizer,
        CanonicalClosureData canon,
        Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits,
        ICollationImporter importer,
        string version,
        string? ucaRules = null)
    {
        _rootData = rootData;
        _normalizer = normalizer;
        _canon = canon;
        _getFcd16 = getFcd16;
        _decimalDigits = decimalDigits;
        _importer = importer;
        _version = version;
        _rulesVersion = ParseVersion(version);
        _ucaRules = ucaRules;
    }

    // Builds the .res bytes for a locale, or null when the locale defines no collation types (it
    // inherits the root collator and genrb emits no file).
    public byte[]? Build(LocaleCollations locale)
    {
        if (locale.Types.Count == 0)
        {
            return null;
        }

        var writer = new ResourceBundleWriter();
        // The root bundle carries the processed UCA rules and a top-level version before its collations
        // (genrb's root.txt order: UCARules, Version, collations). The key pool follows this order.
        if (locale.IsRoot && _ucaRules is not null)
        {
            writer.Root.Add(writer.NewString("UCARules", UcaRulesProcessor.Process(_ucaRules)), writer);
            writer.Root.Add(writer.NewString("Version", _version), writer);
        }

        var collations = writer.NewTable("collations");
        if (locale.DefaultType is not null)
        {
            collations.Add(writer.NewString("default", locale.DefaultType), writer);
        }

        foreach (var type in locale.Types)
        {
            var typeTable = writer.NewTable(type.Type);
            // Every collation type carries its Sequence (the rule string, empty for a type like root's
            // "standard" that is the root itself) and Version, then the %%CollationBin built from the
            // rules against the root. Empty rules yield a settings-only blob, not an absent one.
            typeTable.Add(writer.NewString("Sequence", type.Rules), writer);
            typeTable.Add(writer.NewString("Version", _version), writer);
            // Import-only "private-*" rule strings exist to be imported by others; genrb does
            // not build a binary for them (CLDR ticket #3949).
            if (!type.Type.StartsWith("private-", StringComparison.Ordinal))
            {
                typeTable.Add(writer.NewBinary("%%CollationBin", BuildBlob(type)), writer);
            }
            collations.Add(typeTable, writer);
        }

        writer.Root.Add(collations, writer);
        return writer.Write();
    }

    private byte[] BuildBlob(CollationTypeRules type)
    {
        var settings = new CollationSettings();
        var builder = new CollationBuilder(
            _rootData, _normalizer, _canon, _getFcd16, _decimalDigits);
        // genrb builds a fast-Latin table for every tailoring except search collators.
        if (type.Type.StartsWith("search", StringComparison.Ordinal))
        {
            builder.DisableFastLatin();
        }
        var data = builder.ParseAndBuild(type.Rules, settings, _importer);
        return CollationDataWriter.WriteTailoring(data, settings, _rulesVersion);
    }

    // ICU's u_versionFromString: up to four dot-separated bytes, missing fields zero ("48" -> 48.0.0.0).
    private static byte[] ParseVersion(string version)
    {
        var parts = version.Split('.');
        var result = new byte[4];
        for (var i = 0; i < result.Length && i < parts.Length; ++i)
        {
            byte.TryParse(parts[i], out result[i]);
        }
        return result;
    }
}
