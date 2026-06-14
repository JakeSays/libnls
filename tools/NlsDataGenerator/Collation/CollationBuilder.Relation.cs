using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// The relation half of CollationBuilder (addRelation and the node find/insert helpers), ported from
// collationbuilder.cpp. addRelation inserts a new tailored node at the strength of the relation after
// the current reset position, computes the case bits from the base CEs, and maps the string (and its
// canonical closure) to a temporary CE pointing at that node.
internal sealed partial class CollationBuilder
{
    public override void AddRelation(int strength, string prefix, string str, string extension)
    {
        var nfdPrefix = prefix.Length == 0 ? "" : _nfd.Normalize(prefix);
        var nfdString = _nfd.Normalize(str);

        var nfdLength = nfdString.Length;
        if (nfdLength >= 2)
        {
            var c = nfdString[0];
            if (Hangul.IsJamoL(c) || Hangul.IsJamoV(c))
            {
                throw new NotSupportedException(
                    "contractions starting with conjoining Jamo L or V not supported");
            }
            c = nfdString[nfdLength - 1];
            if (Hangul.IsJamoL(c)
                || (Hangul.IsJamoV(c) && Hangul.IsJamoL(nfdString[nfdLength - 2])))
            {
                throw new NotSupportedException(
                    "contractions ending with conjoining Jamo L or L+V not supported");
            }
        }

        if (strength != Ucol.Identical)
        {
            var idx = FindOrInsertNodeForCEs(strength);
            var ce = _ces[_cesLength - 1];
            if (strength == Ucol.Primary && !IsTempCe(ce) && (uint)(ce >> 32) == 0)
            {
                throw new NotSupportedException("tailoring primary after ignorables not supported");
            }
            if (strength == Ucol.Quaternary && ce == 0)
            {
                throw new NotSupportedException(
                    "tailoring quaternary after tertiary ignorables not supported");
            }
            idx = InsertTailoredNodeAfter(idx, strength);
            var tempStrength = CeStrength(ce);
            if (strength < tempStrength)
            {
                tempStrength = strength;
            }
            _ces[_cesLength - 1] = TempCeFromIndexAndStrength(idx, tempStrength);
        }

        SetCaseBits(nfdString);

        var cesLengthBeforeExtension = _cesLength;
        if (extension.Length != 0)
        {
            var nfdExtension = _nfd.Normalize(extension);
            _cesLength = _dataBuilder.GetCEs(nfdExtension, _ces, _cesLength);
            if (_cesLength > Collator.MaxExpansionLength)
            {
                throw new NotSupportedException(
                    "extension string adds too many collation elements (more than 31 total)");
            }
        }
        var ce32 = Collator.UnassignedCe32;
        if ((prefix != nfdPrefix || str != nfdString)
            && !IgnorePrefix(prefix) && !IgnoreString(str))
        {
            ce32 = AddIfDifferent(prefix, str, _ces, _cesLength, ce32);
        }
        AddWithClosure(nfdPrefix, nfdString, _ces, _cesLength, ce32);
        _cesLength = cesLengthBeforeExtension;
    }

    private int FindOrInsertNodeForCEs(int strength)
    {
        long ce;
        for (;; --_cesLength)
        {
            if (_cesLength == 0)
            {
                ce = _ces[0] = 0;
                _cesLength = 1;
                break;
            }
            ce = _ces[_cesLength - 1];
            if (CeStrength(ce) <= strength)
            {
                break;
            }
        }

        if (IsTempCe(ce))
        {
            return IndexFromTempCe(ce);
        }
        if ((byte)(ce >> 56) == Collator.UnassignedImplicitByte)
        {
            throw new NotSupportedException(
                "tailoring relative to an unassigned code point not supported");
        }
        return FindOrInsertNodeForRootCE(ce, strength);
    }

    private int FindOrInsertNodeForRootCE(long ce, int strength)
    {
        var index = FindOrInsertNodeForPrimary((uint)(ce >> 32));
        if (strength >= Ucol.Secondary)
        {
            var lower32 = (uint)ce;
            index = FindOrInsertWeakNode(index, lower32 >> 16, Ucol.Secondary);
            if (strength >= Ucol.Tertiary)
            {
                index = FindOrInsertWeakNode(index, lower32 & Collator.OnlyTertiaryMask, Ucol.Tertiary);
            }
        }
        return index;
    }

    private int BinarySearchForRootPrimaryNode(uint p)
    {
        var length = _rootPrimaryIndexes.Count;
        if (length == 0)
        {
            return ~0;
        }
        var start = 0;
        var limit = length;
        for (;;)
        {
            var i = (start + limit) / 2;
            var node = _nodes[_rootPrimaryIndexes[i]];
            var nodePrimary = Weight32FromNode(node);
            if (p == nodePrimary)
            {
                return i;
            }
            if (p < nodePrimary)
            {
                if (i == start)
                {
                    return ~start;
                }
                limit = i;
            }
            else
            {
                if (i == start)
                {
                    return ~(start + 1);
                }
                start = i;
            }
        }
    }

    private int FindOrInsertNodeForPrimary(uint p)
    {
        var rootIndex = BinarySearchForRootPrimaryNode(p);
        if (rootIndex >= 0)
        {
            return _rootPrimaryIndexes[rootIndex];
        }
        var index = _nodes.Count;
        _nodes.Add(NodeFromWeight32(p));
        _rootPrimaryIndexes.Insert(~rootIndex, index);
        return index;
    }

    private int FindOrInsertWeakNode(int index, uint weight16, int level)
    {
        if (weight16 == Collator.CommonWeight16)
        {
            return FindCommonNode(index, level);
        }

        var node = _nodes[index];
        if (weight16 != 0 && weight16 < Collator.CommonWeight16)
        {
            var hasThisLevelBefore = level == Ucol.Secondary ? HasBefore2 : HasBefore3;
            if ((node & hasThisLevelBefore) == 0)
            {
                var commonNode = NodeFromWeight16(Collator.CommonWeight16) | NodeFromStrength(level);
                if (level == Ucol.Secondary)
                {
                    commonNode |= node & HasBefore3;
                    node &= ~(long)HasBefore3;
                }
                _nodes[index] = node | (long)hasThisLevelBefore;
                var nextIdx = NextIndexFromNode(node);
                node = NodeFromWeight16(weight16) | NodeFromStrength(level);
                index = InsertNodeBetween(index, nextIdx, node);
                InsertNodeBetween(index, nextIdx, commonNode);
                return index;
            }
        }

        int nextIndex;
        while ((nextIndex = NextIndexFromNode(node)) != 0)
        {
            node = _nodes[nextIndex];
            var nextStrength = StrengthFromNode(node);
            if (nextStrength <= level)
            {
                if (nextStrength < level)
                {
                    break;
                }
                if (!IsTailoredNode(node))
                {
                    var nextWeight16 = Weight16FromNode(node);
                    if (nextWeight16 == weight16)
                    {
                        return nextIndex;
                    }
                    if (nextWeight16 > weight16)
                    {
                        break;
                    }
                }
            }
            index = nextIndex;
        }
        var newNode = NodeFromWeight16(weight16) | NodeFromStrength(level);
        return InsertNodeBetween(index, nextIndex, newNode);
    }

    private int InsertTailoredNodeAfter(int index, int strength)
    {
        if (strength >= Ucol.Secondary)
        {
            index = FindCommonNode(index, Ucol.Secondary);
            if (strength >= Ucol.Tertiary)
            {
                index = FindCommonNode(index, Ucol.Tertiary);
            }
        }
        var node = _nodes[index];
        int nextIndex;
        while ((nextIndex = NextIndexFromNode(node)) != 0)
        {
            node = _nodes[nextIndex];
            if (StrengthFromNode(node) <= strength)
            {
                break;
            }
            index = nextIndex;
        }
        var newNode = IsTailored | NodeFromStrength(strength);
        return InsertNodeBetween(index, nextIndex, newNode);
    }

    private int InsertNodeBetween(int index, int nextIndex, long node)
    {
        var newIndex = _nodes.Count;
        node |= NodeFromPreviousIndex(index) | NodeFromNextIndex(nextIndex);
        _nodes.Add(node);
        _nodes[index] = ChangeNodeNextIndex(_nodes[index], newIndex);
        if (nextIndex != 0)
        {
            _nodes[nextIndex] = ChangeNodePreviousIndex(_nodes[nextIndex], newIndex);
        }
        return newIndex;
    }

    private int FindCommonNode(int index, int strength)
    {
        var node = _nodes[index];
        if (StrengthFromNode(node) >= strength)
        {
            return index;
        }
        if (strength == Ucol.Secondary ? !NodeHasBefore2(node) : !NodeHasBefore3(node))
        {
            return index;
        }
        index = NextIndexFromNode(node);
        node = _nodes[index];
        do
        {
            index = NextIndexFromNode(node);
            node = _nodes[index];
        }
        while (IsTailoredNode(node) || StrengthFromNode(node) > strength
            || Weight16FromNode(node) < Collator.CommonWeight16);
        return index;
    }

    private void SetCaseBits(string nfdString)
    {
        var numTailoredPrimaries = 0;
        for (var i = 0; i < _cesLength; ++i)
        {
            if (CeStrength(_ces[i]) == Ucol.Primary)
            {
                ++numTailoredPrimaries;
            }
        }

        long cases = 0;
        if (numTailoredPrimaries > 0)
        {
            var baseCEs = new Utf16CollationIterator(_baseData, false, nfdString, 0, nfdString.Length);
            var baseCEsLength = baseCEs.FetchCEs() - 1;

            uint lastCase = 0;
            var numBasePrimaries = 0;
            for (var i = 0; i < baseCEsLength; ++i)
            {
                var ce = baseCEs.GetCe(i);
                if ((ce >> 32) != 0)
                {
                    ++numBasePrimaries;
                    var c = ((uint)ce >> 14) & 3;
                    if (numBasePrimaries < numTailoredPrimaries)
                    {
                        cases |= (long)c << ((numBasePrimaries - 1) * 2);
                    }
                    else if (numBasePrimaries == numTailoredPrimaries)
                    {
                        lastCase = c;
                    }
                    else if (c != lastCase)
                    {
                        lastCase = 1;
                        break;
                    }
                }
            }
            if (numBasePrimaries >= numTailoredPrimaries)
            {
                cases |= (long)lastCase << ((numTailoredPrimaries - 1) * 2);
            }
        }

        for (var i = 0; i < _cesLength; ++i)
        {
            var ce = _ces[i] & unchecked((long)0xffffffffffff3fff);
            var strength = CeStrength(ce);
            if (strength == Ucol.Primary)
            {
                ce |= (cases & 3) << 14;
                cases >>= 2;
            }
            else if (strength == Ucol.Tertiary)
            {
                ce |= 0x8000;
            }
            _ces[i] = ce;
        }
    }
}
