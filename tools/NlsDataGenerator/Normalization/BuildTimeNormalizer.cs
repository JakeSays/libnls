using System.Text;

namespace NlsDataGenerator.Normalization;

// A build-time Normalizer2 over the in-memory normalization data, providing the surface the collation
// tailoring builder and rule parser need: NFD normalize/isNormalized/getCombiningClass/getDecomposition/
// isInert, FCD isNormalized, and NFC hasBoundaryBefore. ICU's runtime reads these from nfc.nrm's norm16
// trie; we answer them directly from the same source data (CodePointDataTable) gennorm2 was built from,
// so the results match without re-reading the binary. Bind one to a table after Generate() has run
// (post-process must have populated LeadCc/TrailCc and the composition boundaries).
internal sealed class BuildTimeNormalizer
{
    private readonly CodePointDataTable _norms;

    public BuildTimeNormalizer(CodePointDataTable norms)
    {
        _norms = norms;
    }

    public int GetCombiningClass(int c)
    {
        return _norms.GetCc(c);
    }

    // The FCD16 value (lead ccc << 8 | trail ccc), as ICU's getFCD16 returns.
    public int GetFcd16(int c)
    {
        var norm = _norms.GetNorm(c);
        return norm is null ? 0 : (norm.LeadCc << 8) | norm.TrailCc;
    }

    public bool HasCompBoundaryBefore(int c)
    {
        var norm = _norms.GetNorm(c);
        if (norm is not null)
        {
            return norm.HasCompBoundaryBefore;
        }
        // No stored data: the code point is inert and so has a boundary before it, unless it is a
        // Hangul vowel/trailing jamo, which composes onto a preceding jamo.
        return !(Hangul.IsJamoV(c) || Hangul.IsJamoT(c));
    }

    // NFD decompose-inert: c has no canonical decomposition and zero combining class, so it neither
    // decomposes nor reorders.
    public bool IsInert(int c)
    {
        if (Hangul.IsHangul(c))
        {
            return false;
        }
        var norm = _norms.GetNorm(c);
        if (norm is null)
        {
            return true;
        }
        return !norm.HasMapping() && norm.Cc == 0;
    }

    // The full canonical (NFD) decomposition of c, or null when c has none.
    public string? GetDecomposition(int c)
    {
        if (Hangul.IsHangul(c))
        {
            var buffer = new char[3];
            var length = Hangul.Decompose(c, buffer);
            return new string(buffer, 0, length);
        }
        var norm = _norms.GetNorm(c);
        if (norm is not null && norm.HasMapping())
        {
            // Stored mappings are already fully decomposed and canonically ordered.
            return norm.Mapping;
        }
        return null;
    }

    public string Normalize(string s)
    {
        var builder = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            var decomposition = GetDecomposition(c);
            if (decomposition is null)
            {
                builder.Append(char.ConvertFromUtf32(c));
            }
            else
            {
                builder.Append(decomposition);
            }
        }
        return CanonicalOrder(builder.ToString());
    }

    public bool IsNormalized(string s)
    {
        return Normalize(s) == s;
    }

    // Whether s is in FCD (Fast C or D): no combining mark sorts before a preceding one of higher
    // combining class. Uses the lead/trail combining classes encoded in FCD16.
    public bool IsFcdNormalized(string s)
    {
        var prevTrailCc = 0;
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            var fcd16 = GetFcd16(c);
            if (fcd16 != 0)
            {
                var leadCc = fcd16 >> 8;
                if (leadCc != 0 && prevTrailCc > leadCc)
                {
                    return false;
                }
                prevTrailCc = fcd16 & 0xff;
            }
            else
            {
                prevTrailCc = 0;
            }
        }
        return true;
    }

    // Canonical ordering: a stable sort of each run of combining marks by combining class, with
    // starters (ccc 0) as fixed points.
    private string CanonicalOrder(string s)
    {
        var codePoints = new List<int>(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += char.IsHighSurrogate(s[i]) ? 2 : 1;
            codePoints.Add(c);
        }

        for (var a = 1; a < codePoints.Count; ++a)
        {
            var current = codePoints[a];
            var cc = GetCombiningClass(current);
            if (cc == 0)
            {
                continue;
            }
            var b = a;
            while (b > 0 && GetCombiningClass(codePoints[b - 1]) > cc)
            {
                codePoints[b] = codePoints[b - 1];
                --b;
            }
            codePoints[b] = current;
        }

        var builder = new StringBuilder(s.Length);
        foreach (var c in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(c));
        }
        return builder.ToString();
    }
}
