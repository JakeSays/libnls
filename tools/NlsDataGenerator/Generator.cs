using NlsDataGenerator.Case;
using NlsDataGenerator.Codepage;
using NlsDataGenerator.Collation;
using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Locale;
using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator;

// Orchestrates generation of the NLS data files ESE requires, in dependency order. Each output
// has its own generator class; this type owns only the wiring.
internal sealed class Generator
{
    private readonly CommandLineOptions _options;

    public Generator(CommandLineOptions options)
    {
        _options = options;
    }

    public int Run()
    {
        Directory.CreateDirectory(_options.OutputDirectory);

        // The ICU-format items are also packaged into the cldr-<version>.dat common-data file (the
        // tool's final deliverable); each is collected here and the package is written last.
        var packageName = IcuDataPackageWriter.PackageNameForCldrVersion(_options.CldrVersion);
        var package = new IcuDataPackageWriter(packageName);

        // Self-contained fixed tables.
        Windows1252Generator.Write(_options.OutputDirectory);
        LcidTableGenerator.Write(_options.LcidMapPath, _options.OutputDirectory);

        // Case mapping (ucase.icu) from the UCD.
        var caseData = new CaseGenerator(new CaseInputs(_options.UcdDirectory)).Generate();
        var casePath = Path.Combine(_options.OutputDirectory, "ucase.icu");
        File.WriteAllBytes(casePath, caseData);
        package.Add("ucase.icu", caseData);
        Console.WriteLine($"wrote {casePath} ({caseData.Length} bytes)");

        // Normalization (nfc.nrm) from the UCD.
        var normalizationBuilder = new NormalizationDataBuilder();
        normalizationBuilder.LoadUcd(_options.UcdDirectory);
        var normalizationData = normalizationBuilder.Generate();
        var nfcPath = Path.Combine(_options.OutputDirectory, "nfc.nrm");
        File.WriteAllBytes(nfcPath, normalizationData);
        package.Add("nfc.nrm", normalizationData);
        Console.WriteLine($"wrote {nfcPath} ({normalizationData.Length} bytes)");

        // Collation root (ucadata.icu) from CLDR FractionalUCA.txt + the vendored uscript.h.
        var repoRoot = Directory.GetParent(_options.UcdDirectory)!.Parent!.FullName;
        var uscriptHeaderPath = Path.Combine(repoRoot, "icu", "common", "unicode", "uscript.h");
        var fractionalUcaPath =
            Path.Combine(_options.CldrDirectory, "common", "uca", "FractionalUCA.txt");
        var decimalDigits = DecimalDigitReader.Read(_options.UcdDirectory);
        var rootData = CollationRootGenerator.Generate(
            fractionalUcaPath,
            uscriptHeaderPath,
            normalizationBuilder.GetFcd16,
            decimalDigits,
            HanOrderKind.RadicalStroke);
        var ucadataPath = Path.Combine(_options.OutputDirectory, "ucadata.icu");
        File.WriteAllBytes(ucadataPath, rootData);
        package.Add("ucadata.icu", rootData);
        Console.WriteLine($"wrote {ucadataPath} ({rootData.Length} bytes)");

        // Collation tailorings (coll/*.res) from the CLDR collation XML. Guard against a stale version
        // pin: the major of --cldr-version must match the cldrVersion the extracted data declares.
        var dtdMajor = CldrDtdVersionReader.ReadMajor(_options.CldrDirectory);
        var versionMajor = _options.CldrVersion.Split('.')[0];
        if (versionMajor != dtdMajor)
        {
            throw new InvalidOperationException(
                $"--cldr-version {_options.CldrVersion} (major {versionMajor}) does not match the "
                + $"extracted CLDR data: ldml.dtd declares cldrVersion {dtdMajor}");
        }

        var rootCollationData = CollationRootGenerator.BuildRootData(
            fractionalUcaPath,
            uscriptHeaderPath,
            normalizationBuilder.GetFcd16,
            decimalDigits,
            HanOrderKind.RadicalStroke);
        var collationDirectory = Path.Combine(_options.CldrDirectory, "common", "collation");
        var ucaRulesPath = Path.Combine(_options.CldrDirectory, "common", "uca", "UCA_Rules.txt");
        var ucaRules = File.Exists(ucaRulesPath) ? File.ReadAllText(ucaRulesPath) : null;
        var reader = new CldrCollationReader(collationDirectory);
        var resourceBuilder = new CollationResourceBuilder(
            rootCollationData,
            normalizationBuilder.CreateNormalizer(),
            normalizationBuilder.CreateCanonicalClosureData(),
            normalizationBuilder.GetFcd16,
            decimalDigits,
            new CldrCollationImporter(reader),
            _options.CldrVersion,
            ucaRules);
        var collationOutput = Path.Combine(_options.OutputDirectory, "coll");
        Directory.CreateDirectory(collationOutput);
        // With no explicit --locales, generate the full set: every collation XML in the CLDR data.
        var locales = _options.Locales.Count > 0
            ? _options.Locales
            : Directory.GetFiles(collationDirectory, "*.xml")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()!;
        foreach (var locale in locales)
        {
            var resourceBytes = resourceBuilder.Build(reader.Read(locale));
            if (resourceBytes is null)
            {
                Console.WriteLine($"  {locale}: inherits root, no .res emitted");
                continue;
            }
            var resourcePath = Path.Combine(collationOutput, locale + ".res");
            File.WriteAllBytes(resourcePath, resourceBytes);
            package.Add("coll/" + locale + ".res", resourceBytes);
            Console.WriteLine($"wrote {resourcePath} ({resourceBytes.Length} bytes)");
        }

        // Final deliverable: the ICU common-data package. The custom cp1252.nlsdata /
        // lcid-locales.nlsdata tables are not ICU DataHeader items, so they stay as sidecar
        // files rather than going into the package.
        var packageBytes = package.Build();
        var packagePath = Path.Combine(_options.OutputDirectory, packageName + ".dat");
        File.WriteAllBytes(packagePath, packageBytes);
        Console.WriteLine($"wrote {packagePath} ({packageBytes.Length} bytes, {package.Count} items)");
        return 0;
    }
}
