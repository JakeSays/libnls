using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Normalization;

// Builds nfc.nrm — the Normalizer2 NFC data ESE's normalization path reads — directly from the
// UCD, replacing ICU's gennorm2. Ported from ICU's Normalizer2DataBuilder (n2builder.cpp). The
// input is populated from UnicodeData.txt (combining classes + canonical decompositions) and
// DerivedNormalizationProps.txt (Full_Composition_Exclusion, which splits two-way from one-way
// mappings); processData then derives the norm16 trie, the extra data, and the small-FCD table.
internal sealed partial class NormalizationDataBuilder
{
    private readonly CodePointDataTable _norms = new();

    private readonly int[] _indexes = new int[NormalizationIndex.Count];
    private readonly byte[] _smallFcd = new byte[0x100];
    private List<ushort> _extraData = [];
    private byte[] _norm16TrieBytes = [];

    // The FCD16 value (lccc<<8 | tccc) for a code point, as ICU's Normalizer2Impl::getFCD16 returns.
    // Valid after Generate() has run postProcess (which sets the lead/trail combining classes). The
    // collation context builder uses it; code points with no normalization data are FCD-inert (0).
    public int GetFcd16(int c)
    {
        var norm = _norms.GetNorm(c);
        return norm is null ? 0 : (norm.LeadCc << 8) | norm.TrailCc;
    }

    // A build-time Normalizer2 (NFD/FCD/NFC queries) over this builder's data. Valid after Generate()
    // has run post-process; the collation tailoring builder and rule parser use it.
    public BuildTimeNormalizer CreateNormalizer()
    {
        return new BuildTimeNormalizer(_norms);
    }

    // The canonical-closure data (canonical start sets + segment starters) the CanonicalIterator
    // needs. Valid after Generate().
    public CanonicalClosureData CreateCanonicalClosureData()
    {
        return new CanonicalClosureData(_norms);
    }

    // Reads the UCD into the per-code-point store: combining classes, and canonical decompositions
    // as round-trip mappings (or one-way for Full_Composition_Exclusion code points).
    public void LoadUcd(string ucdDirectory)
    {
        var records = new UnicodeDataReader(ucdDirectory).Read();
        var properties = PropertyListReader.Read(
            Path.Combine(ucdDirectory, "DerivedNormalizationProps.txt"));
        properties.TryGetValue("Full_Composition_Exclusion", out var fullCompositionExclusion);

        foreach (var record in records)
        {
            if (record.CombiningClass != 0)
            {
                // Range rows never carry a non-zero combining class.
                SetCc(record.CodePoint, (byte)record.CombiningClass);
            }

            if (record.DecompositionTag is not null ||
                record.DecompositionMapping.Length == 0)
            {
                continue;
            }

            var mapping = ScalarsToString(record.DecompositionMapping);
            if (fullCompositionExclusion is not null
                && fullCompositionExclusion.Contains(record.CodePoint))
            {
                SetOneWayMapping(record.CodePoint, mapping);
            }
            else
            {
                SetRoundTripMapping(record.CodePoint, mapping);
            }
        }
    }

    private void SetCc(int c, byte cc)
    {
        _norms.CreateNorm(c).Cc = cc;
    }

    private void SetOneWayMapping(int c, string mapping)
    {
        var p = _norms.CreateNorm(c);
        p.Mapping = mapping;
        p.Kind = MappingKind.OneWay;
        p.SetMappingCp();
    }

    private void SetRoundTripMapping(int c, string mapping)
    {
        if (CountCodePoints(mapping) != 2)
        {
            throw new InvalidOperationException(
                $"round-trip mapping from U+{c:X4} must be exactly 2 code points");
        }
        var p = _norms.CreateNorm(c);
        p.Mapping = mapping;
        p.Kind = MappingKind.RoundTrip;
        p.MappingCp = -1;
    }

    private static string ScalarsToString(int[] scalars)
    {
        var builder = new System.Text.StringBuilder(scalars.Length);
        foreach (var scalar in scalars)
        {
            builder.Append(char.ConvertFromUtf32(scalar));
        }
        return builder.ToString();
    }

    private static int CountCodePoints(string s)
    {
        var count = 0;
        var i = 0;
        while (i < s.Length)
        {
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            ++count;
        }
        return count;
    }
}
