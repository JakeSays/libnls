namespace NlsDataGenerator;

// Parsed command line: the three input/output directories and the locale selection.
internal sealed class CommandLineOptions
{
    public string CldrDirectory { get; }

    public string UcdDirectory { get; }

    public string OutputDirectory { get; }

    // The pinned CLDR release (e.g. "48.2"), supplied by the build from the tarball name it unpacks.
    // It is stamped into each collation's Version string and folded into the tailoring data version.
    public string CldrVersion { get; }

    // Path to data/lcidmap.txt (the "locale-name:0xLCID" table backing the LCID APIs).
    public string LcidMapPath { get; }

    public IReadOnlyList<string> Locales { get; }

    private CommandLineOptions(string cldrDirectory, string ucdDirectory, string outputDirectory, string cldrVersion, string lcidMapPath, IReadOnlyList<string> locales)
    {
        CldrDirectory = cldrDirectory;
        UcdDirectory = ucdDirectory;
        OutputDirectory = outputDirectory;
        CldrVersion = cldrVersion;
        LcidMapPath = lcidMapPath;
        Locales = locales;
    }

    // Returns null when required arguments are missing; the caller prints usage and exits.
    public static CommandLineOptions? Parse(string[] args)
    {
        string? cldr = null;
        string? ucd = null;
        string? output = null;
        string? cldrVersion = null;
        string? lcidMap = null;
        var locales = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var hasValue = (i + 1) < args.Length;
            switch (arg)
            {
                case "--cldr":
                    if (!hasValue)
                    {
                        return null;
                    }
                    cldr = args[++i];
                    break;

                case "--ucd":
                    if (!hasValue)
                    {
                        return null;
                    }
                    ucd = args[++i];
                    break;

                case "--out":
                    if (!hasValue)
                    {
                        return null;
                    }
                    output = args[++i];
                    break;

                case "--cldr-version":
                    if (!hasValue)
                    {
                        return null;
                    }
                    cldrVersion = args[++i];
                    break;

                case "--lcidmap":
                    if (!hasValue)
                    {
                        return null;
                    }
                    lcidMap = args[++i];
                    break;

                case "--locales":
                    if (!hasValue)
                    {
                        return null;
                    }
                    foreach (var locale in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        locales.Add(locale);
                    }
                    break;

                default:
                    Console.Error.WriteLine($"unknown argument: {arg}");
                    return null;
            }
        }

        if (cldr is null || ucd is null || output is null || cldrVersion is null || lcidMap is null)
        {
            return null;
        }

        return new CommandLineOptions(cldr, ucd, output, cldrVersion, lcidMap, locales);
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("usage: nls-data-gen --cldr <dir> --ucd <dir> --out <dir> --cldr-version <ver> --lcidmap <file> [--locales de,sv,ja]");
    }
}
