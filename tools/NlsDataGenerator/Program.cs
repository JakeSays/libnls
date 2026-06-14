using NlsDataGenerator.SelfTests;

namespace NlsDataGenerator;

// Entry point only. Parses the command line and hands off to Generator; no real work lives here.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--test-trie")
        {
            return TrieSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--test-ucptrie")
        {
            return UcpTrieSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--test-ucharstrie")
        {
            return UCharsTrieSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--test-res-writer")
        {
            return ResWriterSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--test-weights")
        {
            return WeightsSelfTest.Run(args[1]);
        }
        if (args.Length == 3 && args[0] == "--test-normalizer")
        {
            return NormalizerSelfTest.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "--test-caniter")
        {
            return CanonIterSelfTest.Run(args[1], args[2]);
        }
        if (args.Length == 2 && args[0] == "--test-ruleparser")
        {
            return RuleParserSelfTest.Run(args[1]);
        }
        if (args.Length == 5 && args[0] == "--test-deblob")
        {
            return DeBlobSelfTest.Run(args[1], args[2], args[3], args[4]);
        }
        if (args.Length == 6 && args[0] == "--test-deres")
        {
            return DeResSelfTest.Run(args[1], args[2], args[3], args[4], args[5]);
        }
        if (args.Length == 7 && args[0] == "--test-collset")
        {
            return CollSetSelfTest.Run(args[1], args[2], args[3], args[4], args[5], args[6]);
        }
        if (args.Length == 2 && args[0] == "--dump-coll")
        {
            return CollDumpSelfTest.Run(args[1]);
        }
        if (args.Length == 3 && args[0] == "--diff-blobs")
        {
            return DiffBlobsSelfTest.Run(args[1], args[2]);
        }
        if (args.Length == 3 && args[0] == "--decode-trie")
        {
            return TrieDecodeSelfTest.Run(args[1], args[2]);
        }
        if (args.Length == 2 && args[0] == "--dump-res")
        {
            return ResDumpSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--verify-dat")
        {
            return DatPackageSelfTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--test-uscript")
        {
            var scripts = Parsing.UScriptHeaderReader.Read(args[1]);
            Console.WriteLine($"parsed {scripts.Count} entries");
            foreach (var name in new[]
                { "USCRIPT_INVALID_CODE", "USCRIPT_COMMON", "USCRIPT_LATIN", "USCRIPT_GREEK",
                  "USCRIPT_VITHKUQI", "USCRIPT_KIRAT_RAI", "USCRIPT_CODE_LIMIT",
                  "USCRIPT_MEROITIC", "USCRIPT_SINDHI", "USCRIPT_UCAS", "USCRIPT_MEROITIC_HIEROGLYPHS" })
            {
                Console.WriteLine($"{name} = {(scripts.TryGetValue(name, out var v) ? v.ToString() : "MISSING")}");
            }
            return 0;
        }

        var options = CommandLineOptions.Parse(args);
        if (options is null)
        {
            CommandLineOptions.PrintUsage();
            return 1;
        }

        var generator = new Generator(options);
        return generator.Run();
    }
}
