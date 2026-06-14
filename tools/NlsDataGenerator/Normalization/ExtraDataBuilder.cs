namespace NlsDataGenerator.Normalization;

// Lays out the Normalizer2 "extra data" — the mappings and composition lists that do not fit in
// the trie value itself — ported from ICU's ExtraData (extradata.cpp). Run as an EnumRanges
// handler: each code point's data is written into the segment for its type, and norm.Offset is set
// to the unit offset of its "first unit" within that segment. processData later concatenates the
// segments and turns those per-segment offsets into final norm16 values.
internal sealed class ExtraDataBuilder
{
    private readonly CodePointDataTable _norms;
    private readonly bool _optimizeFast;

    // The ten per-type segments, in their own coordinate spaces (units = uint16).
    public List<ushort> MaybeNoMappingsOnly { get; } = [];
    public List<ushort> MaybeNoMappingsAndCompositions { get; } = [];
    public List<ushort> MaybeYesCompositions { get; } = [];
    public List<ushort> YesYesCompositions { get; } = [];
    public List<ushort> YesNoMappingsAndCompositions { get; } = [];
    public List<ushort> YesNoMappingsOnly { get; } = [];
    public List<ushort> NoNoMappingsCompYes { get; } = [];
    public List<ushort> NoNoMappingsCompBoundaryBefore { get; } = [];
    public List<ushort> NoNoMappingsCompNoMaybeCc { get; } = [];
    public List<ushort> NoNoMappingsEmpty { get; } = [];

    // Dedup tables for the no-no segments: mapping unit sequence -> offset.
    private readonly Dictionary<string, int> _previousNoNoMappingsCompYes = [];
    private readonly Dictionary<string, int> _previousNoNoMappingsCompBoundaryBefore = [];
    private readonly Dictionary<string, int> _previousNoNoMappingsCompNoMaybeCc = [];
    private readonly Dictionary<string, int> _previousNoNoMappingsEmpty = [];

    public ExtraDataBuilder(CodePointDataTable norms, bool optimizeFast)
    {
        _norms = norms;
        _optimizeFast = optimizeFast;

        // 0=inert, 1=Jamo L, 2=start of compositions. The first two units are filler.
        YesYesCompositions.Add(0xFFFF);
        YesYesCompositions.Add(0xFFFF);
        // Hangul LV's first unit: algorithmically decomposes to two Jamo. Harmlessly read.
        YesNoMappingsAndCompositions.Add(2);
        // Hangul LVT's first unit: algorithmically decomposes to three Jamo. Harmlessly read.
        YesNoMappingsOnly.Add(3);
    }

    public void RangeHandler(int start, int end, CodePointData norm)
    {
        if (start != end)
        {
            throw new InvalidOperationException(
                $"unexpected shared data for multiple code points U+{start:X4}..U+{end:X4}");
        }
        if (norm.Error is not null)
        {
            throw new InvalidOperationException($"U+{start:X4} {norm.Error}");
        }
        WriteExtraData(start, norm);
    }

    private void WriteExtraData(int c, CodePointData norm)
    {
        switch (norm.Type)
        {
            case NormType.Inert:
                break;
            case NormType.YesYesCombinesFwd:
                norm.Offset = YesYesCompositions.Count;
                WriteCompositions(c, norm, YesYesCompositions);
                break;
            case NormType.YesNoCombinesFwd:
                norm.Offset = YesNoMappingsAndCompositions.Count
                    + WriteMapping(c, norm, YesNoMappingsAndCompositions);
                WriteCompositions(c, norm, YesNoMappingsAndCompositions);
                break;
            case NormType.YesNoMappingOnly:
                norm.Offset = YesNoMappingsOnly.Count + WriteMapping(c, norm, YesNoMappingsOnly);
                break;
            case NormType.NoNoCompYes:
                if (!_optimizeFast && SetNoNoDelta(c, norm))
                {
                    break;
                }
                norm.Offset = WriteNoNoMapping(c, norm, NoNoMappingsCompYes, _previousNoNoMappingsCompYes);
                break;
            case NormType.NoNoCompBoundaryBefore:
                if (!_optimizeFast && SetNoNoDelta(c, norm))
                {
                    break;
                }
                norm.Offset = WriteNoNoMapping(
                    c, norm, NoNoMappingsCompBoundaryBefore, _previousNoNoMappingsCompBoundaryBefore);
                break;
            case NormType.NoNoCompNoMaybeCc:
                norm.Offset = WriteNoNoMapping(
                    c, norm, NoNoMappingsCompNoMaybeCc, _previousNoNoMappingsCompNoMaybeCc);
                break;
            case NormType.NoNoEmpty:
                // Multiple entries can map to the empty string if their raw mappings differ.
                norm.Offset = WriteNoNoMapping(c, norm, NoNoMappingsEmpty, _previousNoNoMappingsEmpty);
                break;
            case NormType.MaybeNoMappingOnly:
                norm.Offset = MaybeNoMappingsOnly.Count + WriteMapping(c, norm, MaybeNoMappingsOnly);
                break;
            case NormType.MaybeNoCombinesFwd:
                norm.Offset = MaybeNoMappingsAndCompositions.Count
                    + WriteMapping(c, norm, MaybeNoMappingsAndCompositions);
                WriteCompositions(c, norm, MaybeNoMappingsAndCompositions);
                break;
            case NormType.MaybeYesCombinesFwd:
                norm.Offset = MaybeYesCompositions.Count;
                WriteCompositions(c, norm, MaybeYesCompositions);
                break;
            case NormType.MaybeYesSimple:
                break;
            case NormType.YesYesWithCc:
                break;
            default:
                throw new InvalidOperationException($"unexpected norm type for U+{c:X4}");
        }
    }

    // Writes the mapping (and optional raw-mapping / ccc-lccc prefix) into dataString. Returns the
    // offset of the "first unit" from the start of this code point's extra data (= the length of
    // the optional prefix).
    private int WriteMapping(int c, CodePointData norm, List<ushort> dataString)
    {
        var m = norm.Mapping!;
        var length = m.Length;
        var firstUnit = length | (norm.TrailCc << 8);
        var preMappingLength = 0;
        if (norm.RawMapping is not null)
        {
            var rm = norm.RawMapping;
            var rmLength = rm.Length;
            if (rmLength > Norm16Constants.MappingLengthMask)
            {
                throw new InvalidOperationException(
                    $"raw mapping for U+{c:X4} longer than maximum of "
                    + $"{Norm16Constants.MappingLengthMask}");
            }
            var rm0 = rm[0];
            if (rmLength == length - 1
                && string.CompareOrdinal(rm[1..], m[2..]) == 0
                && rm0 > Norm16Constants.MappingLengthMask)
            {
                // Compression: the raw mapping equals the final mapping with its first two units
                // replaced by rm0, so store only rm0.
                dataString.Add(rm0);
                preMappingLength = 1;
            }
            else
            {
                // Store the raw mapping with its length.
                AppendUnits(dataString, rm);
                dataString.Add((ushort)rmLength);
                preMappingLength = rmLength + 1;
            }
            firstUnit |= Norm16Constants.MappingHasRawMapping;
        }
        var cccLccc = norm.Cc | (norm.LeadCc << 8);
        if (cccLccc != 0)
        {
            dataString.Add((ushort)cccLccc);
            ++preMappingLength;
            firstUnit |= Norm16Constants.MappingHasCccLcccWord;
        }
        dataString.Add((ushort)firstUnit);
        AppendUnits(dataString, m);
        return preMappingLength;
    }

    // Writes a no-no mapping, deduplicating against previously written identical mappings. Returns
    // the full offset of the "first unit" within dataString.
    private int WriteNoNoMapping(int c, CodePointData norm, List<ushort> dataString,
        Dictionary<string, int> previousMappings)
    {
        var newMapping = new List<ushort>();
        var offset = WriteMapping(c, norm, newMapping);
        var key = UnitsToString(newMapping);
        if (previousMappings.TryGetValue(key, out var previousOffset))
        {
            // Duplicate: point at the identical mapping already stored.
            offset = previousOffset;
        }
        else
        {
            offset = dataString.Count + offset;
            dataString.AddRange(newMapping);
            previousMappings[key] = offset;
        }
        return offset;
    }

    // Tries a compact algorithmic encoding to a single comp-yes-and-zero-cc code point. Mutates
    // norm to NO_NO_DELTA and returns true on success.
    private bool SetNoNoDelta(int c, CodePointData norm)
    {
        if (norm.MappingCp >= 0
            && !(c <= 0x7F && norm.MappingCp > 0x7F)
            && (int)_norms.GetNormRef(norm.MappingCp).Type < (int)NormType.NoNoCompYes)
        {
            var delta = norm.MappingCp - c;
            if (-Norm16Constants.MaxDelta <= delta && delta <= Norm16Constants.MaxDelta)
            {
                norm.Type = NormType.NoNoDelta;
                norm.Offset = delta;
                return true;
            }
        }
        return false;
    }

    private void WriteCompositions(int c, CodePointData norm, List<ushort> dataString)
    {
        if (norm.Cc != 0)
        {
            throw new InvalidOperationException(
                $"U+{c:X4} combines-forward and has ccc!=0, not possible in Unicode normalization");
        }
        var pairs = norm.Compositions!;
        var length = pairs.Count;
        for (var i = 0; i < length; ++i)
        {
            var pair = pairs[i];
            // 22 bits for the composite and whether it also combines forward.
            var compositeAndFwd = pair.Composite << 1;
            if (_norms.GetNormRef(pair.Composite).CombinesFwd())
            {
                compositeAndFwd |= 1;
            }
            // Most pairs encode in two units, some in three.
            int firstUnit;
            int secondUnit;
            var thirdUnit = -1;
            if (pair.Trail < Norm16Constants.Comp1TrailLimit)
            {
                if (compositeAndFwd <= 0xFFFF)
                {
                    firstUnit = pair.Trail << 1;
                    secondUnit = compositeAndFwd;
                }
                else
                {
                    firstUnit = (pair.Trail << 1) | Norm16Constants.Comp1Triple;
                    secondUnit = compositeAndFwd >> 16;
                    thirdUnit = compositeAndFwd;
                }
            }
            else
            {
                firstUnit = (Norm16Constants.Comp1TrailLimit
                    + (pair.Trail >> Norm16Constants.Comp1TrailShift))
                    | Norm16Constants.Comp1Triple;
                secondUnit = (pair.Trail << Norm16Constants.Comp2TrailShift)
                    | (compositeAndFwd >> 16);
                thirdUnit = compositeAndFwd;
            }
            // The high bit of the first unit marks the last composition pair.
            if (i == length - 1)
            {
                firstUnit |= Norm16Constants.Comp1LastTuple;
            }
            dataString.Add((ushort)firstUnit);
            dataString.Add((ushort)secondUnit);
            if (thirdUnit >= 0)
            {
                dataString.Add((ushort)thirdUnit);
            }
        }
    }

    private static void AppendUnits(List<ushort> segment, string s)
    {
        foreach (var ch in s)
        {
            segment.Add(ch);
        }
    }

    private static string UnitsToString(List<ushort> units)
    {
        var chars = new char[units.Count];
        for (var i = 0; i < units.Count; ++i)
        {
            chars[i] = (char)units[i];
        }
        return new string(chars);
    }
}
