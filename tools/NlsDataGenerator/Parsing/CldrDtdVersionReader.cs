using System.Text.RegularExpressions;

namespace NlsDataGenerator.Parsing;

// Reads the CLDR major version the extracted data declares, used to cross-check the --cldr-version the
// build passes. CLDR stamps its major release into common/dtd/ldml.dtd as a fixed attribute default
// (<!ATTLIST version cldrVersion CDATA #FIXED "48" >). This is only the major (the DTD has no dot
// release), so it validates the major of --cldr-version (e.g. "48.2" -> "48"), catching a data/version
// pin mismatch — bumping the CLDR tarball without updating the version the build passes, or vice versa.
internal static partial class CldrDtdVersionReader
{
    [GeneratedRegex("cldrVersion\\s+CDATA\\s+#FIXED\\s+\"([^\"]+)\"")]
    private static partial Regex FixedVersion();

    // cldrDirectory is the directory containing common/ (the same root the other CLDR inputs use).
    public static string ReadMajor(string cldrDirectory)
    {
        var dtdPath = Path.Combine(cldrDirectory, "common", "dtd", "ldml.dtd");
        var match = FixedVersion().Match(File.ReadAllText(dtdPath));
        if (!match.Success)
        {
            throw new FormatException($"no cldrVersion #FIXED attribute in {dtdPath}");
        }
        return match.Groups[1].Value;
    }
}
