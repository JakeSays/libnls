using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Collation;

// Builds the root collation data file (ucadata.icu), ported from genuca's main +
// buildAndWriteBaseData. It reads the UScriptCode enum and the FractionalUCA text, builds the base
// CollationData, lays out the compact root-elements table with its header indexes, serializes the
// body, and prepends the "UCol" udata header.
internal static class CollationRootGenerator
{
    // The collation data-structure builder version (ICU's UCOL_BUILDER_VERSION).
    private const int BuilderVersion = 9;

    public static byte[] Generate(
        string fractionalUcaPath,
        string uscriptHeaderPath,
        Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits,
        HanOrderKind hanOrderKind)
    {
        var uscript = UScriptHeaderReader.Read(uscriptHeaderPath);
        var sampleCharToScript = ScriptSampleChars.Build(uscript);

        var builder = new CollationBaseDataBuilder(getFcd16, decimalDigits);
        builder.Init();
        var parser = new FractionalUcaParser(builder, sampleCharToScript, uscript, hanOrderKind);
        parser.Parse(fractionalUcaPath);

        builder.EnableFastLatin();
        var data = new CollationData();
        builder.Build(data);

        var settings = new CollationSettings();
        var rootElements = BuildRootElements(builder, parser);
        var body = CollationDataWriter.WriteBase(data, settings, rootElements);

        var uca = parser.UcaVersion;
        byte[] dataVersion =
        [
            BuilderVersion,
            (byte)((uca[0] << 3) + uca[1]),
            (byte)(uca[2] << 6),
            0,
        ];
        var writer = new LittleEndianWriter();
        new IcuDataHeader("UCol", [5, 0, 0, 0], dataVersion).Write(writer);
        writer.WriteBytes(body);
        return writer.ToArray();
    }

    // Builds the root CollationData in memory (for the tailoring builder's base), with the root
    // elements, version, and FCD16 provider attached.
    public static CollationData BuildRootData(
        string fractionalUcaPath,
        string uscriptHeaderPath,
        Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits,
        HanOrderKind hanOrderKind)
    {
        var uscript = UScriptHeaderReader.Read(uscriptHeaderPath);
        var sampleCharToScript = ScriptSampleChars.Build(uscript);

        var builder = new CollationBaseDataBuilder(getFcd16, decimalDigits);
        builder.Init();
        var parser = new FractionalUcaParser(builder, sampleCharToScript, uscript, hanOrderKind);
        parser.Parse(fractionalUcaPath);

        builder.EnableFastLatin();
        var data = new CollationData();
        builder.Build(data);
        AddLoadTimeUnsafeChars(data.UnsafeBackwardSet!, getFcd16);

        var rootElements = BuildRootElements(builder, parser);
        data.RootElements = Array.ConvertAll(rootElements, x => (uint)x);
        data.RootElementsLength = rootElements.Length;
        data.Fcd16Provider = getFcd16;
        data.ScriptCodeByIsoName = UScriptHeaderReader.ReadScriptCodesByIsoName(uscriptHeaderPath);
        var uca = parser.UcaVersion;
        data.Version =
        [
            BuilderVersion,
            (byte)((uca[0] << 3) + uca[1]),
            (byte)(uca[2] << 6),
            0,
        ];
        return data;
    }

    // Replicates the load-time augmentation CollationDataReader::read applies to the root collator's
    // unsafe-backward set (collationdatareader.cpp:288-326): the stored contraction set plus the trail
    // surrogates and every code point with a non-zero leading combining class (addLcccChars), then the
    // lead surrogates whose supplementary block holds an unsafe code point. The root data file stores
    // only the contraction set; the rest is added at load to avoid a UCD dependency in the root build.
    // The tailoring builder clones this loaded set as its base, so it must match the runtime root
    // exactly — otherwise writeTailoring's (tailoring - base) difference picks up phantom chars.
    private static void AddLoadTimeUnsafeChars(UnicodeSet unsafeSet, Func<int, int> getFcd16)
    {
        unsafeSet.Thaw();
        unsafeSet.Add(Unicode.TrailSurrogateMin, Unicode.TrailSurrogateMax);
        for (var c = 0; c <= Unicode.MaxCodePoint; ++c)
        {
            if (c == Unicode.LeadSurrogateMin)
            {
                // Surrogate code points carry no leading combining class; the trail range is added
                // explicitly above and lead surrogates are marked from their blocks below.
                c = Unicode.SurrogateLimit - 1;
                continue;
            }
            if ((getFcd16(c) >> 8) != 0)
            {
                unsafeSet.Add(c);
            }
        }
        var cp = Unicode.SupplementaryMin;
        for (var lead = Unicode.LeadSurrogateMin; lead < Unicode.TrailSurrogateMin;
            ++lead, cp += Unicode.SupplementaryBlockSize)
        {
            if (unsafeSet.ContainsSome(cp, cp + Unicode.SupplementaryBlockMask))
            {
                unsafeSet.Add(lead);
            }
        }
        unsafeSet.Freeze();
    }

    private static int[] BuildRootElements(CollationBaseDataBuilder builder, FractionalUcaParser parser)
    {
        var table = new List<int>();
        for (var i = 0; i < CollationRootElements.IxCount; ++i)
        {
            table.Add(0);
        }
        builder.BuildRootElementsTable(table);

        var index = CollationRootElements.IxCount;
        table[CollationRootElements.IxFirstTertiaryIndex] = index;
        while (((uint)table[index] & 0xFFFF0000) == 0)
        {
            ++index;
        }
        table[CollationRootElements.IxFirstSecondaryIndex] = index;
        while ((table[index] & (int)CollationRootElements.SecTerDeltaFlag) != 0)
        {
            ++index;
        }
        table[CollationRootElements.IxFirstPrimaryIndex] = index;
        table[CollationRootElements.IxCommonSecAndTerCe] = (int)Collator.CommonSecAndTerCe;

        var secTerBoundaries =
            (parser.FixedByte("[fixed last secondary common byte") << 24)
            | (parser.FixedByte("[fixed first ignorable secondary byte") << 16)
            | parser.FixedByte("[fixed first ignorable tertiary byte");
        table[CollationRootElements.IxSecTerBoundaries] = secTerBoundaries;
        return [.. table];
    }
}
