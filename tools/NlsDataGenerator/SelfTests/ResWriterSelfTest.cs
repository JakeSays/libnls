using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator.SelfTests;

// Validates ResourceBundleWriter against a genrb-built reference. It reads de.res, pulls out the two
// genrb-built %%CollationBin blobs plus the Sequence/Version strings, rebuilds the same tree in
// genrb's construction order (extractor order: search before phonebook, children Sequence/Version/
// %%CollationBin), re-emits it, and checks the bytes match. This isolates the .res container format
// from the collation tailoring builder (which supplies those blobs once it is ported).
internal static class ResWriterSelfTest
{
    public static int Run(string referencePath)
    {
        var reference = File.ReadAllBytes(referencePath);
        var reader = new ResourceBundleReader(reference);

        var collations = reader.ReadTable(reader.RootWord)["collations"];
        var types = reader.ReadTable(collations);
        var searchTable = reader.ReadTable(types["search"]);
        var phonebookTable = reader.ReadTable(types["phonebook"]);

        var searchSequence = reader.ReadString(searchTable["Sequence"]);
        var searchVersion = reader.ReadString(searchTable["Version"]);
        var searchBin = reader.ReadBinary(searchTable["%%CollationBin"]);
        var phonebookSequence = reader.ReadString(phonebookTable["Sequence"]);
        var phonebookVersion = reader.ReadString(phonebookTable["Version"]);
        var phonebookBin = reader.ReadBinary(phonebookTable["%%CollationBin"]);

        var writer = new ResourceBundleWriter();
        var collationsOut = writer.NewTable("collations");

        // search is built first — the extractor emits it before phonebook (CLDR XML order).
        var searchOut = writer.NewTable("search");
        var searchSequenceOut = writer.NewString("Sequence", searchSequence);
        var searchVersionOut = writer.NewString("Version", searchVersion);
        var searchBinOut = writer.NewBinary("%%CollationBin", searchBin);
        searchOut.Add(searchSequenceOut, writer);
        searchOut.Add(searchVersionOut, writer);
        searchOut.Add(searchBinOut, writer);

        var phonebookOut = writer.NewTable("phonebook");
        var phonebookSequenceOut = writer.NewString("Sequence", phonebookSequence);
        var phonebookVersionOut = writer.NewString("Version", phonebookVersion);
        var phonebookBinOut = writer.NewBinary("%%CollationBin", phonebookBin);
        phonebookOut.Add(phonebookSequenceOut, writer);
        phonebookOut.Add(phonebookVersionOut, writer);
        phonebookOut.Add(phonebookBinOut, writer);

        collationsOut.Add(searchOut, writer);
        collationsOut.Add(phonebookOut, writer);
        writer.Root.Add(collationsOut, writer);

        var produced = writer.Write();

        if (produced.Length != reference.Length)
        {
            Console.WriteLine($"SIZE DIFFER: produced {produced.Length}, reference {reference.Length}");
        }
        var limit = Math.Min(produced.Length, reference.Length);
        for (var i = 0; i < limit; ++i)
        {
            if (produced[i] != reference[i])
            {
                Console.WriteLine(
                    $"BYTE DIFFER at offset {i} (0x{i:x}): produced 0x{produced[i]:x2}, reference 0x{reference[i]:x2}");
                return 1;
            }
        }
        if (produced.Length != reference.Length)
        {
            return 1;
        }
        Console.WriteLine($"BYTE-IDENTICAL ({produced.Length} bytes)");
        return 0;
    }
}
