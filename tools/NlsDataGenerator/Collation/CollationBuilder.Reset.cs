namespace NlsDataGenerator.Collation;

// The reset half of CollationBuilder (addReset and its helpers), ported from collationbuilder.cpp.
// A reset positions the insertion point in the weight graph: &str computes str's CEs and finds/creates
// the node for the last CE; &[before n]str inserts a node just before the reset position; &[position]
// maps a special boundary to a root CE. The resulting position is stored as a temporary CE in ces[].
internal sealed partial class CollationBuilder
{
    // USCRIPT_HAN, for the [last regular] reset position.
    private const int ScriptHan = 17;

    public override void AddReset(int strength, string str)
    {
        if (str[0] == (char)0xFFFE)
        {
            _ces[0] = GetSpecialResetPosition(str);
            _cesLength = 1;
        }
        else
        {
            var nfdString = _nfd.Normalize(str);
            _cesLength = _dataBuilder.GetCEs(nfdString, _ces, 0);
            if (_cesLength > Collator.MaxExpansionLength)
            {
                throw new NotSupportedException(
                    "reset position maps to too many collation elements (more than 31)");
            }
        }
        if (strength == Ucol.Identical)
        {
            return;
        }

        var index = FindOrInsertNodeForCEs(strength);
        var node = _nodes[index];
        while (StrengthFromNode(node) > strength)
        {
            index = PreviousIndexFromNode(node);
            node = _nodes[index];
        }

        if (StrengthFromNode(node) == strength && IsTailoredNode(node))
        {
            index = PreviousIndexFromNode(node);
        }
        else if (strength == Ucol.Primary)
        {
            var p = Weight32FromNode(node);
            if (p == 0)
            {
                throw new NotSupportedException("reset primary-before ignorable not possible");
            }
            if (p <= _rootElements.GetFirstPrimary())
            {
                throw new NotSupportedException("reset primary-before first non-ignorable not supported");
            }
            if (p == Collator.FirstTrailingPrimary)
            {
                throw new NotSupportedException("reset primary-before [first trailing] not supported");
            }
            p = _rootElements.GetPrimaryBefore(p, _baseData.IsCompressiblePrimary(p));
            index = FindOrInsertNodeForPrimary(p);
            for (;;)
            {
                node = _nodes[index];
                var nextIndex = NextIndexFromNode(node);
                if (nextIndex == 0)
                {
                    break;
                }
                index = nextIndex;
            }
        }
        else
        {
            index = FindCommonNode(index, Ucol.Secondary);
            if (strength >= Ucol.Tertiary)
            {
                index = FindCommonNode(index, Ucol.Tertiary);
            }
            node = _nodes[index];
            if (StrengthFromNode(node) == strength)
            {
                var weight16 = Weight16FromNode(node);
                if (weight16 == 0)
                {
                    throw new NotSupportedException(strength == Ucol.Secondary
                        ? "reset secondary-before secondary ignorable not possible"
                        : "reset tertiary-before completely ignorable not possible");
                }
                weight16 = GetWeight16Before(index, node, strength);
                var previousIndex = PreviousIndexFromNode(node);
                uint previousWeight16;
                var i = previousIndex;
                for (;;)
                {
                    node = _nodes[i];
                    var previousStrength = StrengthFromNode(node);
                    if (previousStrength < strength)
                    {
                        previousWeight16 = Collator.CommonWeight16;
                        break;
                    }
                    if (previousStrength == strength && !IsTailoredNode(node))
                    {
                        previousWeight16 = Weight16FromNode(node);
                        break;
                    }
                    i = PreviousIndexFromNode(node);
                }
                if (previousWeight16 == weight16)
                {
                    index = previousIndex;
                }
                else
                {
                    node = NodeFromWeight16(weight16) | NodeFromStrength(strength);
                    index = InsertNodeBetween(previousIndex, index, node);
                }
            }
            else
            {
                var weight16 = GetWeight16Before(index, node, strength);
                index = FindOrInsertWeakNode(index, weight16, strength);
            }
            strength = CeStrength(_ces[_cesLength - 1]);
        }
        _ces[_cesLength - 1] = TempCeFromIndexAndStrength(index, strength);
    }

    private uint GetWeight16Before(int index, long node, int level)
    {
        uint t;
        if (StrengthFromNode(node) == Ucol.Tertiary)
        {
            t = Weight16FromNode(node);
        }
        else
        {
            t = Collator.CommonWeight16;
        }
        while (StrengthFromNode(node) > Ucol.Secondary)
        {
            index = PreviousIndexFromNode(node);
            node = _nodes[index];
        }
        if (IsTailoredNode(node))
        {
            return Collator.BeforeWeight16;
        }
        uint s;
        if (StrengthFromNode(node) == Ucol.Secondary)
        {
            s = Weight16FromNode(node);
        }
        else
        {
            s = Collator.CommonWeight16;
        }
        while (StrengthFromNode(node) > Ucol.Primary)
        {
            index = PreviousIndexFromNode(node);
            node = _nodes[index];
        }
        if (IsTailoredNode(node))
        {
            return Collator.BeforeWeight16;
        }
        var p = Weight32FromNode(node);
        if (level == Ucol.Secondary)
        {
            return _rootElements.GetSecondaryBefore(p, s);
        }
        return _rootElements.GetTertiaryBefore(p, s, t);
    }

    private long GetSpecialResetPosition(string str)
    {
        long ce;
        var strength = Ucol.Primary;
        var isBoundary = false;
        var pos = str[1] - 0x2800;
        switch ((ResetPosition)pos)
        {
            case ResetPosition.FirstTertiaryIgnorable:
            case ResetPosition.LastTertiaryIgnorable:
                return 0;
            case ResetPosition.FirstSecondaryIgnorable:
            {
                var index = FindOrInsertNodeForRootCE(0, Ucol.Tertiary);
                var node = _nodes[index];
                if ((index = NextIndexFromNode(node)) != 0)
                {
                    node = _nodes[index];
                    if (IsTailoredNode(node) && StrengthFromNode(node) == Ucol.Tertiary)
                    {
                        return TempCeFromIndexAndStrength(index, Ucol.Tertiary);
                    }
                }
                return _rootElements.GetFirstTertiaryCE();
            }
            case ResetPosition.LastSecondaryIgnorable:
                ce = _rootElements.GetLastTertiaryCE();
                strength = Ucol.Tertiary;
                break;
            case ResetPosition.FirstPrimaryIgnorable:
            {
                var index = FindOrInsertNodeForRootCE(0, Ucol.Secondary);
                var node = _nodes[index];
                while ((index = NextIndexFromNode(node)) != 0)
                {
                    node = _nodes[index];
                    strength = StrengthFromNode(node);
                    if (strength < Ucol.Secondary)
                    {
                        break;
                    }
                    if (strength == Ucol.Secondary)
                    {
                        if (IsTailoredNode(node))
                        {
                            if (NodeHasBefore3(node))
                            {
                                index = NextIndexFromNode(_nodes[NextIndexFromNode(node)]);
                            }
                            return TempCeFromIndexAndStrength(index, Ucol.Secondary);
                        }
                        break;
                    }
                }
                ce = _rootElements.GetFirstSecondaryCE();
                strength = Ucol.Secondary;
                break;
            }
            case ResetPosition.LastPrimaryIgnorable:
                ce = _rootElements.GetLastSecondaryCE();
                strength = Ucol.Secondary;
                break;
            case ResetPosition.FirstVariable:
                ce = _rootElements.GetFirstPrimaryCE();
                isBoundary = true;
                break;
            case ResetPosition.LastVariable:
                ce = _rootElements.LastCEWithPrimaryBefore(_variableTop + 1);
                break;
            case ResetPosition.FirstRegular:
                ce = _rootElements.FirstCEWithPrimaryAtLeast(_variableTop + 1);
                isBoundary = true;
                break;
            case ResetPosition.LastRegular:
                ce = _rootElements.FirstCEWithPrimaryAtLeast(_baseData.GetFirstPrimaryForGroup(ScriptHan));
                break;
            case ResetPosition.FirstImplicit:
                ce = _baseData.GetSingleCe(0x4e00);
                break;
            case ResetPosition.LastImplicit:
                throw new NotSupportedException("reset to [last implicit] not supported");
            case ResetPosition.FirstTrailing:
                ce = Collator.MakeCe(Collator.FirstTrailingPrimary);
                isBoundary = true;
                break;
            case ResetPosition.LastTrailing:
                throw new NotSupportedException("LDML forbids tailoring to U+FFFF");
            default:
                throw new InvalidOperationException("unreachable reset position");
        }

        var rootIndex = FindOrInsertNodeForRootCE(ce, strength);
        var rootNode = _nodes[rootIndex];
        if ((pos & 1) == 0)
        {
            if (!NodeHasAnyBefore(rootNode) && isBoundary)
            {
                int nextIndex;
                if ((nextIndex = NextIndexFromNode(rootNode)) != 0)
                {
                    rootNode = _nodes[nextIndex];
                    ce = TempCeFromIndexAndStrength(nextIndex, strength);
                }
                else
                {
                    var p = (uint)(ce >> 32);
                    var pIndex = _rootElements.FindPrimary(p);
                    var compressible = _baseData.IsCompressiblePrimary(p);
                    p = _rootElements.GetPrimaryAfter(p, pIndex, compressible);
                    ce = Collator.MakeCe(p);
                    rootIndex = FindOrInsertNodeForRootCE(ce, Ucol.Primary);
                    rootNode = _nodes[rootIndex];
                }
            }
            if (NodeHasAnyBefore(rootNode))
            {
                if (NodeHasBefore2(rootNode))
                {
                    rootIndex = NextIndexFromNode(_nodes[NextIndexFromNode(rootNode)]);
                    rootNode = _nodes[rootIndex];
                }
                if (NodeHasBefore3(rootNode))
                {
                    rootIndex = NextIndexFromNode(_nodes[NextIndexFromNode(rootNode)]);
                }
                ce = TempCeFromIndexAndStrength(rootIndex, strength);
            }
        }
        else
        {
            for (;;)
            {
                var nextIndex = NextIndexFromNode(rootNode);
                if (nextIndex == 0)
                {
                    break;
                }
                var nextNode = _nodes[nextIndex];
                if (StrengthFromNode(nextNode) < strength)
                {
                    break;
                }
                rootIndex = nextIndex;
                rootNode = nextNode;
            }
            if (IsTailoredNode(rootNode))
            {
                ce = TempCeFromIndexAndStrength(rootIndex, strength);
            }
        }
        return ce;
    }
}
