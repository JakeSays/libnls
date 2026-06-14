using System.Text;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// The canonical-closure half of CollationBuilder, ported from collationbuilder.cpp. After a relation
// maps an NFD string to CEs, the closure adds the same mapping for every canonically-equivalent
// spelling (addOnlyClosure via CanonicalIterator) and for strings that merge a following combining
// mark into a composite (addTailComposites). closeOverComposites then maps every precomposed character
// so it collates like its decomposition. addIfDifferent only writes a mapping when the CEs differ from
// what the data already produces.
internal sealed partial class CollationBuilder
{
    private const int ClosureLoopLimit = 3000;

    private uint AddWithClosure(string nfdPrefix, string nfdString, long[] newCEs, int newCEsLength,
        uint ce32)
    {
        ce32 = AddIfDifferent(nfdPrefix, nfdString, newCEs, newCEsLength, ce32);
        ce32 = AddOnlyClosure(nfdPrefix, nfdString, newCEs, newCEsLength, ce32);
        AddTailComposites(nfdPrefix, nfdString);
        return ce32;
    }

    private uint AddOnlyClosure(string nfdPrefix, string nfdString, long[] newCEs, int newCEsLength,
        uint ce32)
    {
        var loop = 0;
        if (nfdPrefix.Length == 0)
        {
            var stringIter = new CanonicalIterator(_nfd, _canon, nfdString);
            for (;;)
            {
                var str = stringIter.Next();
                if (str is null)
                {
                    break;
                }
                if (loop++ > ClosureLoopLimit)
                {
                    throw new NotSupportedException("canonical closure too large");
                }
                if (IgnoreString(str) || str == nfdString)
                {
                    continue;
                }
                ce32 = AddIfDifferent("", str, newCEs, newCEsLength, ce32);
            }
        }
        else
        {
            var prefixIter = new CanonicalIterator(_nfd, _canon, nfdPrefix);
            var stringIter = new CanonicalIterator(_nfd, _canon, nfdString);
            for (;;)
            {
                var prefix = prefixIter.Next();
                if (prefix is null)
                {
                    break;
                }
                if (IgnorePrefix(prefix))
                {
                    continue;
                }
                var samePrefix = prefix == nfdPrefix;
                for (;;)
                {
                    var str = stringIter.Next();
                    if (str is null)
                    {
                        break;
                    }
                    if (loop++ > ClosureLoopLimit)
                    {
                        throw new NotSupportedException("canonical closure too large");
                    }
                    if (IgnoreString(str) || (samePrefix && str == nfdString))
                    {
                        continue;
                    }
                    ce32 = AddIfDifferent(prefix, str, newCEs, newCEsLength, ce32);
                }
                stringIter.Reset();
            }
        }
        return ce32;
    }

    private void AddTailComposites(string nfdPrefix, string nfdString)
    {
        int lastStarter;
        var indexAfterLastStarter = nfdString.Length;
        for (;;)
        {
            if (indexAfterLastStarter == 0)
            {
                return;
            }
            lastStarter = char.ConvertToUtf32(nfdString,
                indexAfterLastStarter - (char.IsLowSurrogate(nfdString[indexAfterLastStarter - 1]) ? 2 : 1));
            if (_nfd.GetCombiningClass(lastStarter) == 0)
            {
                break;
            }
            indexAfterLastStarter -= lastStarter > 0xFFFF ? 2 : 1;
        }
        if (Hangul.IsJamoL(lastStarter))
        {
            return;
        }

        var composites = new SortedSet<int>();
        if (!_canon.GetCanonStartSet(lastStarter, composites))
        {
            return;
        }

        var newCEs = new long[Collator.MaxExpansionLength];
        foreach (var composite in composites)
        {
            var decomp = _nfd.GetDecomposition(composite)!;
            if (!MergeCompositeIntoString(nfdString, indexAfterLastStarter, composite, decomp,
                out var newNfdString, out var newString))
            {
                continue;
            }
            var newCEsLength = _dataBuilder.GetCEs(nfdPrefix, newNfdString, newCEs, 0);
            if (newCEsLength > Collator.MaxExpansionLength)
            {
                continue;
            }
            var ce32 = AddIfDifferent(nfdPrefix, newString, newCEs, newCEsLength, Collator.UnassignedCe32);
            if (ce32 != Collator.UnassignedCe32)
            {
                AddOnlyClosure(nfdPrefix, newNfdString, newCEs, newCEsLength, ce32);
            }
        }
    }

    private bool MergeCompositeIntoString(string nfdString, int indexAfterLastStarter, int composite,
        string decomp, out string newNfdString, out string newString)
    {
        newNfdString = "";
        newString = "";
        var lastStarterLength = decomp[0] > 0xFFFF || char.IsHighSurrogate(decomp[0]) ? 2 : 1;
        if (lastStarterLength == decomp.Length)
        {
            return false;
        }
        if (string.CompareOrdinal(nfdString[indexAfterLastStarter..], decomp[lastStarterLength..]) == 0)
        {
            return false;
        }

        var nfd = new StringBuilder(nfdString[..indexAfterLastStarter]);
        var withComposite = new StringBuilder(
            nfdString[..(indexAfterLastStarter - lastStarterLength)]);
        withComposite.Append(char.ConvertFromUtf32(composite));

        var sourceIndex = indexAfterLastStarter;
        var decompIndex = lastStarterLength;
        var sourceChar = -1;
        var sourceCc = 0;
        var decompCc = 0;
        for (;;)
        {
            if (sourceChar < 0)
            {
                if (sourceIndex >= nfdString.Length)
                {
                    break;
                }
                sourceChar = char.ConvertToUtf32(nfdString, sourceIndex);
                sourceCc = _nfd.GetCombiningClass(sourceChar);
            }
            if (decompIndex >= decomp.Length)
            {
                break;
            }
            var decompChar = char.ConvertToUtf32(decomp, decompIndex);
            decompCc = _nfd.GetCombiningClass(decompChar);
            if (decompCc == 0)
            {
                return false;
            }
            if (sourceCc < decompCc)
            {
                return false;
            }
            if (decompCc < sourceCc)
            {
                nfd.Append(char.ConvertFromUtf32(decompChar));
                decompIndex += decompChar > 0xFFFF ? 2 : 1;
            }
            else if (decompChar != sourceChar)
            {
                return false;
            }
            else
            {
                nfd.Append(char.ConvertFromUtf32(decompChar));
                decompIndex += decompChar > 0xFFFF ? 2 : 1;
                sourceIndex += decompChar > 0xFFFF ? 2 : 1;
                sourceChar = -1;
            }
        }
        if (sourceChar >= 0)
        {
            if (sourceCc < decompCc)
            {
                return false;
            }
            nfd.Append(nfdString[sourceIndex..]);
            withComposite.Append(nfdString[sourceIndex..]);
        }
        else if (decompIndex < decomp.Length)
        {
            nfd.Append(decomp[decompIndex..]);
        }
        newNfdString = nfd.ToString();
        newString = withComposite.ToString();
        return true;
    }

    private bool IgnorePrefix(string s)
    {
        return !IsFcd(s);
    }

    private bool IgnoreString(string s)
    {
        return !IsFcd(s) || Hangul.IsHangul(char.ConvertToUtf32(s, 0));
    }

    private bool IsFcd(string s)
    {
        return _nfd.IsFcdNormalized(s);
    }

    private void CloseOverComposites()
    {
        var ces = new long[Collator.MaxExpansionLength];
        for (var c = 0; c <= Unicode.MaxCodePoint; ++c)
        {
            if (c >= Unicode.LeadSurrogateMin && c <= Unicode.TrailSurrogateMax)
            {
                continue;
            }
            if (Hangul.IsHangul(c))
            {
                continue;
            }
            var nfdString = _nfd.GetDecomposition(c);
            if (nfdString is null)
            {
                continue;
            }
            _cesLength = _dataBuilder.GetCEs(nfdString, ces, 0);
            if (_cesLength > Collator.MaxExpansionLength)
            {
                continue;
            }
            AddIfDifferent("", char.ConvertFromUtf32(c), ces, _cesLength, Collator.UnassignedCe32);
        }
    }

    private uint AddIfDifferent(string prefix, string str, long[] newCEs, int newCEsLength, uint ce32)
    {
        var oldCEs = new long[Collator.MaxExpansionLength];
        var oldCEsLength = _dataBuilder.GetCEs(prefix, str, oldCEs, 0);
        if (!SameCEs(newCEs, newCEsLength, oldCEs, oldCEsLength))
        {
            if (ce32 == Collator.UnassignedCe32)
            {
                ce32 = _dataBuilder.EncodeCEs(newCEs, newCEsLength);
            }
            _dataBuilder.AddCe32(prefix, str, ce32);
        }
        return ce32;
    }

    private static bool SameCEs(long[] ces1, int ces1Length, long[] ces2, int ces2Length)
    {
        if (ces1Length != ces2Length)
        {
            return false;
        }
        for (var i = 0; i < ces1Length; ++i)
        {
            if (ces1[i] != ces2[i])
            {
                return false;
            }
        }
        return true;
    }
}
