using System.Text;

namespace NlsDataGenerator.Normalization;

// Enumerates the strings canonically equivalent to a source string, ported from ICU's
// CanonicalIterator (caniter.cpp). The collation tailoring builder uses it in closeOverComposites to
// find every spelling of a tailored string. The source is normalized to NFD and split into canonical
// segments (each a starter plus following combining marks); each segment's equivalent forms are
// computed, and Next() walks the cartesian product across segments. The set of results is well
// defined; ICU yields them in hash-table order, so within each segment we sort for determinism.
internal sealed class CanonicalIterator
{
    private const int PermuteDepthLimit = 8;
    private const int ResultLimit = 4096;

    private readonly BuildTimeNormalizer _nfd;
    private readonly CanonicalClosureData _canon;

    private string _source = "";
    private string[][] _pieces = [];
    private int[] _current = [];
    private bool _done;

    public CanonicalIterator(BuildTimeNormalizer nfd, CanonicalClosureData canon, string source)
    {
        _nfd = nfd;
        _canon = canon;
        SetSource(source);
    }

    // The NFD form of the source.
    public string Source => _source;

    public void Reset()
    {
        _done = false;
        for (var i = 0; i < _current.Length; ++i)
        {
            _current[i] = 0;
        }
    }

    // The next canonically-equivalent string, or null when the iteration is done.
    public string? Next()
    {
        if (_done)
        {
            return null;
        }
        var builder = new StringBuilder();
        for (var i = 0; i < _pieces.Length; ++i)
        {
            builder.Append(_pieces[i][_current[i]]);
        }
        for (var i = _current.Length - 1; ; --i)
        {
            if (i < 0)
            {
                _done = true;
                break;
            }
            ++_current[i];
            if (_current[i] < _pieces[i].Length)
            {
                break;
            }
            _current[i] = 0;
        }
        return builder.ToString();
    }

    private void SetSource(string newSource)
    {
        _source = _nfd.Normalize(newSource);
        _done = false;
        if (newSource.Length == 0)
        {
            _pieces = [[""]];
            _current = [0];
            return;
        }

        var segments = new List<string>();
        var start = 0;
        // The first code point always belongs to the first segment; splits happen at later starters.
        var i = Utf16Length(_source, 0);
        while (i < _source.Length)
        {
            var c = char.ConvertToUtf32(_source, i);
            if (_canon.IsCanonSegmentStarter(c))
            {
                segments.Add(_source.Substring(start, i - start));
                start = i;
            }
            i += Utf16Length(_source, i);
        }
        segments.Add(_source.Substring(start));

        _pieces = new string[segments.Count][];
        _current = new int[segments.Count];
        for (var s = 0; s < segments.Count; ++s)
        {
            _pieces[s] = GetEquivalents(segments[s]);
        }
    }

    private string[] GetEquivalents(string segment)
    {
        var result = new HashSet<string>();
        var basic = new HashSet<string>();
        GetEquivalents2(basic, segment);

        foreach (var item in basic)
        {
            var permutations = new HashSet<string>();
            Permute(item, true, permutations, 0);
            foreach (var possible in permutations)
            {
                if (_nfd.Normalize(possible) == segment)
                {
                    result.Add(possible);
                }
            }
        }

        var array = new string[result.Count];
        result.CopyTo(array);
        Array.Sort(array, StringComparer.Ordinal);
        return array;
    }

    private void GetEquivalents2(HashSet<string> fillin, string segment)
    {
        fillin.Add(segment);
        var starts = new SortedSet<int>();
        var i = 0;
        while (i < segment.Length)
        {
            var c = char.ConvertToUtf32(segment, i);
            if (_canon.GetCanonStartSet(c, starts))
            {
                foreach (var composite in starts)
                {
                    var remainder = new HashSet<string>();
                    if (!Extract(remainder, composite, segment, i))
                    {
                        continue;
                    }
                    var prefix = segment.Substring(0, i) + char.ConvertFromUtf32(composite);
                    foreach (var item in remainder)
                    {
                        fillin.Add(prefix + item);
                    }
                    if (fillin.Count > ResultLimit)
                    {
                        return;
                    }
                }
            }
            i += Utf16Length(segment, i);
        }
    }

    // Whether the decomposition of composite matches the segment starting at segmentPos (allowing
    // canonical rearrangement); if so, fills remainder with the equivalents of what follows.
    private bool Extract(HashSet<string> fillin, int composite, string segment, int segmentPos)
    {
        var compositeString = char.ConvertFromUtf32(composite);
        var inputLength = compositeString.Length;
        var temp = new StringBuilder(compositeString);

        var decomp = _nfd.Normalize(compositeString);
        var decompPos = 0;
        var decompCp = char.ConvertToUtf32(decomp, decompPos);
        decompPos += Utf16Length(decomp, decompPos);

        var ok = false;
        var i = segmentPos;
        while (i < segment.Length)
        {
            var c = char.ConvertToUtf32(segment, i);
            i += Utf16Length(segment, i);
            if (c == decompCp)
            {
                if (decompPos == decomp.Length)
                {
                    temp.Append(segment, i, segment.Length - i);
                    ok = true;
                    break;
                }
                decompCp = char.ConvertToUtf32(decomp, decompPos);
                decompPos += Utf16Length(decomp, decompPos);
            }
            else
            {
                temp.Append(char.ConvertFromUtf32(c));
            }
        }
        if (!ok)
        {
            return false;
        }

        var tempString = temp.ToString();
        if (inputLength == tempString.Length)
        {
            // Matched exactly, no remainder.
            fillin.Add("");
            return true;
        }

        var trial = _nfd.Normalize(tempString);
        if (trial != segment.Substring(segmentPos))
        {
            return false;
        }
        GetEquivalents2(fillin, tempString.Substring(inputLength));
        return true;
    }

    private void Permute(string source, bool skipZeros, HashSet<string> result, int depth)
    {
        if (depth > PermuteDepthLimit)
        {
            return;
        }
        if (source.Length <= 2 && CountCodePoints(source) <= 1)
        {
            result.Add(source);
            return;
        }

        var i = 0;
        while (i < source.Length)
        {
            var c = char.ConvertToUtf32(source, i);
            var length = Utf16Length(source, i);
            if (skipZeros && i != 0 && _nfd.GetCombiningClass(c) == 0)
            {
                i += length;
                continue;
            }
            var rest = source.Remove(i, length);
            var subPermute = new HashSet<string>();
            Permute(rest, skipZeros, subPermute, depth + 1);
            var prefix = char.ConvertFromUtf32(c);
            foreach (var item in subPermute)
            {
                result.Add(prefix + item);
            }
            i += length;
        }
    }

    private static int Utf16Length(string s, int index)
    {
        return char.IsHighSurrogate(s[index]) ? 2 : 1;
    }

    private static int CountCodePoints(string s)
    {
        var count = 0;
        var i = 0;
        while (i < s.Length)
        {
            i += Utf16Length(s, i);
            ++count;
        }
        return count;
    }
}
