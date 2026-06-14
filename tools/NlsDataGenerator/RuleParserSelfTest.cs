using System.Text;
using NlsDataGenerator.Collation;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator;

// Smoke test for CollationRuleParser: parses representative rule strings with a recording sink and
// checks the reset/relation callbacks. There is no standalone ICU oracle for the parser (ICU's parser
// needs the collation root data and only drives the internal builder), so this confirms the text
// processing — strengths, reset-before, starred ranges, context, and [import] inlining — against
// hand-derived expectations. The full byte-level check comes from the end-to-end %%CollationBin diff.
internal static class RuleParserSelfTest
{
    private sealed class RecordingSink : CollationRuleSink
    {
        public readonly List<string> Calls = [];

        public override void AddReset(int strength, string str)
        {
            Calls.Add($"reset {Strength(strength)} {Hex(str)}");
        }

        public override void AddRelation(int strength, string prefix, string str, string extension)
        {
            Calls.Add($"rel {Strength(strength)} {Hex(prefix)}|{Hex(str)}|{Hex(extension)}");
        }
    }

    private sealed class StubImporter : ICollationImporter
    {
        public string GetRules(string localeId, string collationType)
        {
            return $"&[last regular]<́";
        }
    }

    public static int Run(string ucdDirectory)
    {
        var builder = new NormalizationDataBuilder();
        builder.LoadUcd(ucdDirectory);
        builder.Generate();
        var normalizer = builder.CreateNormalizer();

        var failures = 0;

        var phonebook = Parse(normalizer, "&AE<<ä<<<Ä\n&OE<<ö<<<Ö\n&UE<<ü<<<Ü", null);
        string[] expectedPhonebook =
        [
            "reset i 0041.0045",
            "rel s |00e4|",
            "rel t |00c4|",
            "reset i 004f.0045",
            "rel s |00f6|",
            "rel t |00d6|",
            "reset i 0055.0045",
            "rel s |00fc|",
            "rel t |00dc|",
        ];
        failures += Check("phonebook", phonebook, expectedPhonebook);

        var before = Parse(normalizer, "&[before 2]a<<b", null);
        failures += Check("reset-before", before, ["reset s 0061", "rel s |0062|"]);

        var starred = Parse(normalizer, "&a<*bcd", null);
        failures += Check("starred", starred,
            ["reset i 0061", "rel p |0062|", "rel p |0063|", "rel p |0064|"]);

        var range = Parse(normalizer, "&a=*b-d", null);
        failures += Check("starred-range", range,
            ["reset i 0061", "rel i |0062|", "rel i |0063|", "rel i |0064|"]);

        var context = Parse(normalizer, "&a<b|c/d", null);
        failures += Check("context", context, ["reset i 0061", "rel p 0062|0063|0064"]);

        var import = Parse(normalizer, "&x<y[import und-u-co-search]", new StubImporter());
        failures += Check("import", import,
            ["reset i 0078", "rel p |0079|", "reset i fffe.2809", "rel p |0301|"]);

        Console.WriteLine(failures == 0 ? "RULE PARSER OK" : $"RULE PARSER FAILURES: {failures}");
        return failures == 0 ? 0 : 1;
    }

    private static List<string> Parse(BuildTimeNormalizer normalizer, string rules, ICollationImporter? importer)
    {
        var sink = new RecordingSink();
        var parser = new CollationRuleParser(null!, normalizer);
        parser.Parse(rules, new CollationSettings(), sink, importer);
        return sink.Calls;
    }

    private static int Check(string name, List<string> actual, string[] expected)
    {
        if (actual.Count == expected.Length && actual.SequenceEqual(expected))
        {
            Console.WriteLine($"  {name}: OK ({actual.Count} calls)");
            return 0;
        }
        Console.WriteLine($"  {name}: MISMATCH");
        Console.WriteLine($"    expected: {string.Join(" / ", expected)}");
        Console.WriteLine($"    actual:   {string.Join(" / ", actual)}");
        return 1;
    }

    private static string Strength(int strength)
    {
        return strength switch
        {
            Ucol.Primary => "p",
            Ucol.Secondary => "s",
            Ucol.Tertiary => "t",
            Ucol.Quaternary => "q",
            Ucol.Identical => "i",
            _ => strength.ToString(),
        };
    }

    private static string Hex(string s)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            parts.Add(c.ToString("x4"));
        }
        return string.Join('.', parts);
    }
}
