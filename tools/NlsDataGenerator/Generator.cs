using NlsDataGenerator.Case;
using NlsDataGenerator.Codepage;
using NlsDataGenerator.Collation;
using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Locale;
using NlsDataGenerator.Metadata;
using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator;

// Orchestrates generation of the NLS data files ESE requires, in dependency order. Each output
// has its own generator class; this type owns only the wiring.
internal sealed class Generator
{
    // The collation package's internal ToC-entry prefix. It must be free of ICU's tree separator
    // ('-') and the type separator ('.'), so it is a fixed name rather than the cldr-<version>
    // file name (which contains both). libnls reads this prefix back from the package's ToC and the
    // CLDR version from a metadata item, so neither is tied to the file name.
    private const string CollationPackagePrefix = "nlsdata";

    private readonly CommandLineOptions _options;
    private readonly IcuDataPackageWriter _cldrPackage;
    private readonly string _packageFileName;

    public Generator(CommandLineOptions options)
    {
        _options = options;

        // The ICU-format items are packaged into the cldr-<version>.dat common-data file (the tool's
        // final deliverable); each is collected here and the package is written last.
        _packageFileName = IcuDataPackageWriter.PackageNameForCldrVersion(_options.CldrVersion);
        _cldrPackage = new IcuDataPackageWriter(CollationPackagePrefix);
    }

    public int Run()
    {
        // Collation tailorings (coll/*.res) from the CLDR collation XML. Guard against a stale version
        // pin: the major of --cldr-version must match the cldrVersion the extracted data declares.
        var dtdMajor = CldrDtdVersionReader.ReadMajor(_options.CldrDirectory);
        var versionMajor = _options.CldrVersion.Split('.')[0];
        if (versionMajor != dtdMajor)
        {
            throw new InvalidOperationException(
                $"--cldr-version {_options.CldrVersion} (major {versionMajor}) does not match the extracted CLDR data: ldml.dtd declares cldrVersion {dtdMajor}");
        }

        Directory.CreateDirectory(_options.OutputDirectory);

        MakeCodePageData();

        PackageCaseMappings();

        var normalizationBuilder = PackageNormalizationData();

        var uscriptHeaderPath = PackageUcaData(normalizationBuilder, out var fractionalUcaPath, out var decimalDigits);

        PackageLocales(fractionalUcaPath, uscriptHeaderPath, normalizationBuilder, decimalDigits);

        // The CLDR version travels inside the package as a metadata item, so libnls reads it on open
        // rather than parsing the file name. It lives under a "metadata" tree so more package
        // metadata can be added later.
        _cldrPackage.Add("metadata/cldrversion.nls", NlsMetadataGenerator.Generate(_options.CldrVersion));

        // Final deliverable: the CLDR-versioned ICU common-data package (collation, normalization,
        // case). The Windows fixed tables live in codepages.dat, written above.
        var packageBytes = _cldrPackage.Build();
        var packagePath = Path.Combine(_options.OutputDirectory, $"{_packageFileName}.dat");
        File.WriteAllBytes(packagePath, packageBytes);
        Console.WriteLine($"wrote {packagePath} ({packageBytes.Length} bytes, {_cldrPackage.Count} items)");
        return 0;
    }

    private void PackageLocales(
        string fractionalUcaPath,
        string uscriptHeaderPath,
        NormalizationDataBuilder normalizationBuilder,
        List<(int CodePoint, int DigitValue)> decimalDigits)
    {
        var collationDirectory = PackageRootCollationData(
            fractionalUcaPath,
            uscriptHeaderPath,
            normalizationBuilder,
            decimalDigits,
            out var reader,
            out var resourceBuilder);

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
            var resourcePath = Path.Combine(collationOutput, $"{locale}.res");
            File.WriteAllBytes(resourcePath, resourceBytes);
            _cldrPackage.Add($"coll/{locale}.res", resourceBytes);
            Console.WriteLine($"wrote {resourcePath} ({resourceBytes.Length} bytes)");
        }
    }

    private string PackageRootCollationData(
        string fractionalUcaPath,
        string uscriptHeaderPath,
        NormalizationDataBuilder normalizationBuilder,
        List<(int CodePoint, int DigitValue)> decimalDigits,
        out CldrCollationReader reader,
        out CollationResourceBuilder resourceBuilder)
    {
        var rootCollationData = CollationRootGenerator.BuildRootData(
            fractionalUcaPath,
            uscriptHeaderPath,
            normalizationBuilder.GetFcd16,
            decimalDigits,
            HanOrderKind.RadicalStroke);
        var collationDirectory = Path.Combine(_options.CldrDirectory, "common", "collation");
        var ucaRulesPath = Path.Combine(_options.CldrDirectory, "common", "uca", "UCA_Rules.txt");
        var ucaRules = File.Exists(ucaRulesPath) ? File.ReadAllText(ucaRulesPath) : null;
        reader = new CldrCollationReader(collationDirectory);
        resourceBuilder = new CollationResourceBuilder(
            rootCollationData,
            normalizationBuilder.CreateNormalizer(),
            normalizationBuilder.CreateCanonicalClosureData(),
            normalizationBuilder.GetFcd16,
            decimalDigits,
            new CldrCollationImporter(reader),
            _options.CldrVersion,
            ucaRules);
        return collationDirectory;
    }

    private string PackageUcaData(
        NormalizationDataBuilder normalizationBuilder,
        out string fractionalUcaPath,
        out List<(int CodePoint, int DigitValue)> decimalDigits)
    {
        // Collation root (ucadata.icu) from CLDR FractionalUCA.txt + the vendored uscript.h.
        var repoRoot = Directory.GetParent(_options.UcdDirectory)!.Parent!.FullName;
        var uscriptHeaderPath = Path.Combine(repoRoot, "icu", "common", "unicode", "uscript.h");
        fractionalUcaPath = Path.Combine(_options.CldrDirectory, "common", "uca", "FractionalUCA.txt");
        decimalDigits = DecimalDigitReader.Read(_options.UcdDirectory);
        var rootData = CollationRootGenerator.Generate(
            fractionalUcaPath,
            uscriptHeaderPath,
            normalizationBuilder.GetFcd16,
            decimalDigits,
            HanOrderKind.RadicalStroke);
        var ucadataPath = Path.Combine(_options.OutputDirectory, "ucadata.icu");
        File.WriteAllBytes(ucadataPath, rootData);
        // ICU's collation root loader looks ucadata up under the "coll" tree
        // (udata path "<package>-coll", name "ucadata"), so it must be packaged
        // as <package>/coll/ucadata.icu, not at the top level.
        _cldrPackage.Add("coll/ucadata.icu", rootData);
        Console.WriteLine($"wrote {ucadataPath} ({rootData.Length} bytes)");
        return uscriptHeaderPath;
    }

    private NormalizationDataBuilder PackageNormalizationData()
    {
        // Normalization (nfc.nrm) from the UCD.
        var normalizationBuilder = new NormalizationDataBuilder();
        normalizationBuilder.LoadUcd(_options.UcdDirectory);
        var normalizationData = normalizationBuilder.Generate();
        var nfcPath = Path.Combine(_options.OutputDirectory, "nfc.nrm");
        File.WriteAllBytes(nfcPath, normalizationData);
        _cldrPackage.Add("nfc.nrm", normalizationData);
        Console.WriteLine($"wrote {nfcPath} ({normalizationData.Length} bytes)");
        return normalizationBuilder;
    }

    private void PackageCaseMappings()
    {
        // Case mapping (ucase.icu) from the UCD.
        var caseData = new CaseGenerator(new CaseInputs(_options.UcdDirectory)).Generate();
        var casePath = Path.Combine(_options.OutputDirectory, "ucase.icu");
        File.WriteAllBytes(casePath, caseData);
        _cldrPackage.Add("ucase.icu", caseData);
        Console.WriteLine($"wrote {casePath} ({caseData.Length} bytes)");
    }

    private void MakeCodePageData()
    {
        // The Windows fixed tables (windows-1252, LCID<->locale) are not CLDR-versioned, so they go
        // in their own standalone codepages.dat rather than the cldr-<version>.dat package.
        var codepages = new IcuDataPackageWriter("codepages");
        codepages.Add("cp1252.nls", Windows1252Generator.Generate());
        codepages.Add("lcid.nls", LcidTableGenerator.Generate(_options.LcidMapPath));
        var codepagesBytes = codepages.Build();
        var codepagesPath = Path.Combine(_options.OutputDirectory, "codepages.dat");
        File.WriteAllBytes(codepagesPath, codepagesBytes);
        Console.WriteLine($"wrote {codepagesPath} ({codepagesBytes.Length} bytes, {codepages.Count} items)");
    }
}
