namespace NlsDataGenerator.Normalization;

// One code point's normalization data (gennorm2's Norm). Populated from the UCD (canonical
// mappings + ccc), then classified and encoded into norm16 by the builder.
internal sealed class CodePointData
{
    // The decomposition mapping (UTF-16); null when there is no mapping.
    public string? Mapping { get; set; }

    // The pre-further-decomposition mapping, if the mapping was decomposed further.
    public string? RawMapping { get; set; }

    // >= 0 when the mapping is to a single code point.
    public int MappingCp { get; set; } = -1;

    public int MappingPhase { get; set; }

    public MappingKind Kind { get; set; } = MappingKind.None;

    // (trail, composite) composition pairs, or null when this does not combine forward.
    public List<CompositionPair>? Compositions { get; set; }

    public byte Cc { get; set; }

    public byte LeadCc { get; set; }

    public byte TrailCc { get; set; }

    public bool CombinesBack { get; set; }

    public bool HasCompBoundaryBefore { get; set; }

    public bool HasCompBoundaryAfter { get; set; }

    public NormType Type { get; set; } = NormType.Unknown;

    // Offset into the type's extra data, or the algorithmic-mapping delta.
    public int Offset { get; set; }

    public int Norm16 { get; set; }

    public string? Error { get; set; }

    public bool HasMapping()
    {
        return Kind > MappingKind.Removed;
    }

    public bool CombinesFwd()
    {
        return Compositions is not null;
    }

    public void SetMappingCp()
    {
        if (Mapping is not null && Mapping.Length != 0 && Mapping.Length == Utf16Length(Mapping, 0))
        {
            MappingCp = char.ConvertToUtf32(Mapping, 0);
        }
        else
        {
            MappingCp = -1;
        }
    }

    // The composite that c combines with the given trailing character to form, or -1.
    public int Combine(int trail)
    {
        if (Compositions is null)
        {
            return -1;
        }
        foreach (var pair in Compositions)
        {
            if (pair.Trail == trail)
            {
                return pair.Composite;
            }
            // Compositions are sorted by trail, so a larger trail means no match.
            if (trail < pair.Trail)
            {
                break;
            }
        }
        return -1;
    }

    private static int Utf16Length(string s, int index)
    {
        return char.IsHighSurrogate(s[index]) ? 2 : 1;
    }
}
