using System.Text;

namespace NlsDataGenerator.Normalization;

// Fully decomposes each mapping, ported from ICU's Decomposer (norms.cpp). Run repeatedly as an
// EnumRanges handler until DidDecompose stays false: each pass replaces any character in a mapping
// that itself has a mapping (or is a Hangul syllable) with its decomposition, validating the
// Unicode normalization invariants along the way and preserving the pre-expansion form as
// RawMapping.
internal sealed class Decomposer
{
    private readonly CodePointDataTable _norms;

    public Decomposer(CodePointDataTable norms)
    {
        _norms = norms;
    }

    public bool DidDecompose { get; set; }

    public void RangeHandler(int start, int end, CodePointData norm)
    {
        if (!norm.HasMapping())
        {
            return;
        }
        var m = norm.Mapping!;
        StringBuilder? decomposed = null;
        var length = m.Length;
        var i = 0;
        while (i < length)
        {
            var prev = i;
            var c = Utf16.CodePointAt(m, i);
            i += Utf16.CharCount(c);
            if (start <= c && c <= end)
            {
                throw new InvalidOperationException(
                    $"U+{c:X4} maps to itself directly or indirectly");
            }
            var cNorm = _norms.GetNormRef(c);
            if (norm.Kind == MappingKind.RoundTrip && prev == 0
                && !norm.CombinesBack && cNorm.CombinesBack)
            {
                // If a two-way mapping starts with an NFC_QC=Maybe character, mark the composite as
                // NFC_QC=Maybe too, so decomposition and recomposition are triggered.
                norm.CombinesBack = true;
                DidDecompose = true;
            }
            if (cNorm.HasMapping())
            {
                if (norm.Kind == MappingKind.RoundTrip)
                {
                    if (prev == 0)
                    {
                        if (cNorm.Kind != MappingKind.RoundTrip)
                        {
                            throw new InvalidOperationException(
                                $"U+{start:X4}'s round-trip mapping's starter U+{c:X4} "
                                + "one-way-decomposes, not possible in Unicode normalization");
                        }
                        var myTrailCc = _norms.GetCc(Utf16.CodePointAt(m, i));
                        var cTrailChar = Utf16.LastCodePoint(cNorm.Mapping!);
                        var cTrailCc = _norms.GetCc(cTrailChar);
                        if (cTrailCc > myTrailCc)
                        {
                            throw new InvalidOperationException(
                                $"U+{start:X4}'s round-trip mapping's starter U+{c:X4} decomposes "
                                + $"and the inner/earlier tccc={cTrailCc} > outer/following "
                                + $"tccc={myTrailCc}, not possible in Unicode normalization");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"U+{start:X4}'s round-trip mapping's non-starter U+{c:X4} decomposes, "
                            + "not possible in Unicode normalization");
                    }
                }
                decomposed ??= new StringBuilder(m[..prev]);
                decomposed.Append(cNorm.Mapping);
            }
            else if (Hangul.IsHangul(c))
            {
                var buffer = new char[3];
                var hangulLength = Hangul.Decompose(c, buffer);
                if (norm.Kind == MappingKind.RoundTrip && prev != 0)
                {
                    throw new InvalidOperationException(
                        $"U+{start:X4}'s round-trip mapping's non-starter U+{c:X4} decomposes, "
                        + "not possible in Unicode normalization");
                }
                decomposed ??= new StringBuilder(m[..prev]);
                decomposed.Append(buffer, 0, hangulLength);
            }
            else if (decomposed is not null)
            {
                decomposed.Append(m, prev, i - prev);
            }
        }
        if (decomposed is not null)
        {
            if (norm.RawMapping is null)
            {
                // Remember the original mapping when decomposing recursively.
                norm.RawMapping = norm.Mapping;
            }
            norm.Mapping = decomposed.ToString();
            // Not SetMappingCp(): the original mapping is most likely encodable as a delta.
            DidDecompose = true;
        }
    }
}
