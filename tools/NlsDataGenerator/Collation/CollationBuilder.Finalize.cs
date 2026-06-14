namespace NlsDataGenerator.Collation;

// The finalize half of CollationBuilder, ported from collationbuilder.cpp. makeTailoredCEs walks each
// root-primary list in the weight graph and allocates the actual primary/secondary/tertiary weights
// for the tailored nodes (using CollationWeights), overwriting each tailored node with its final CE.
// finalizeCEs then rebuilds the data with a fresh builder, copying every mapping while replacing the
// temporary CEs (which pointed at nodes) with those final CEs.
internal sealed partial class CollationBuilder
{
    private void MakeTailoredCEs()
    {
        var primaries = new CollationWeights();
        var secondaries = new CollationWeights();
        var tertiaries = new CollationWeights();

        for (var rpi = 0; rpi < _rootPrimaryIndexes.Count; ++rpi)
        {
            var i = _rootPrimaryIndexes[rpi];
            var node = _nodes[i];
            var p = Weight32FromNode(node);
            var s = p == 0 ? 0u : Collator.CommonWeight16;
            var t = s;
            uint q = 0;
            var pIsTailored = false;
            var sIsTailored = false;
            var tIsTailored = false;
            var pIndex = p == 0 ? 0 : _rootElements.FindPrimary(p);
            var nextIndex = NextIndexFromNode(node);
            while (nextIndex != 0)
            {
                i = nextIndex;
                node = _nodes[i];
                nextIndex = NextIndexFromNode(node);
                var strength = StrengthFromNode(node);
                if (strength == Ucol.Quaternary)
                {
                    if (q == 3)
                    {
                        throw new InvalidOperationException("quaternary tailoring gap too small");
                    }
                    ++q;
                }
                else
                {
                    if (strength == Ucol.Tertiary)
                    {
                        if (IsTailoredNode(node))
                        {
                            if (!tIsTailored)
                            {
                                var tCount = CountTailoredNodes(_nodes, nextIndex, Ucol.Tertiary) + 1;
                                uint tLimit;
                                if (t == 0)
                                {
                                    t = _rootElements.GetTertiaryBoundary() - 0x100;
                                    tLimit = (uint)_rootElements.GetFirstTertiaryCE() & Collator.OnlyTertiaryMask;
                                }
                                else if (!pIsTailored && !sIsTailored)
                                {
                                    tLimit = _rootElements.GetTertiaryAfter(pIndex, s, t);
                                }
                                else if (t == Collator.BeforeWeight16)
                                {
                                    tLimit = Collator.CommonWeight16;
                                }
                                else
                                {
                                    tLimit = _rootElements.GetTertiaryBoundary();
                                }
                                tertiaries.InitForTertiary();
                                if (!tertiaries.AllocWeights(t, tLimit, tCount))
                                {
                                    throw new InvalidOperationException("tertiary tailoring gap too small");
                                }
                                tIsTailored = true;
                            }
                            t = tertiaries.NextWeight();
                        }
                        else
                        {
                            t = Weight16FromNode(node);
                            tIsTailored = false;
                        }
                    }
                    else
                    {
                        if (strength == Ucol.Secondary)
                        {
                            if (IsTailoredNode(node))
                            {
                                if (!sIsTailored)
                                {
                                    var sCount = CountTailoredNodes(_nodes, nextIndex, Ucol.Secondary) + 1;
                                    uint sLimit;
                                    if (s == 0)
                                    {
                                        s = _rootElements.GetSecondaryBoundary() - 0x100;
                                        sLimit = (uint)_rootElements.GetFirstSecondaryCE() >> 16;
                                    }
                                    else if (!pIsTailored)
                                    {
                                        sLimit = _rootElements.GetSecondaryAfter(pIndex, s);
                                    }
                                    else if (s == Collator.BeforeWeight16)
                                    {
                                        sLimit = Collator.CommonWeight16;
                                    }
                                    else
                                    {
                                        sLimit = _rootElements.GetSecondaryBoundary();
                                    }
                                    if (s == Collator.CommonWeight16)
                                    {
                                        s = _rootElements.GetLastCommonSecondary();
                                    }
                                    secondaries.InitForSecondary();
                                    if (!secondaries.AllocWeights(s, sLimit, sCount))
                                    {
                                        throw new InvalidOperationException("secondary tailoring gap too small");
                                    }
                                    sIsTailored = true;
                                }
                                s = secondaries.NextWeight();
                            }
                            else
                            {
                                s = Weight16FromNode(node);
                                sIsTailored = false;
                            }
                        }
                        else
                        {
                            if (!pIsTailored)
                            {
                                var pCount = CountTailoredNodes(_nodes, nextIndex, Ucol.Primary) + 1;
                                var isCompressible = _baseData.IsCompressiblePrimary(p);
                                var pLimit = _rootElements.GetPrimaryAfter(p, pIndex, isCompressible);
                                primaries.InitForPrimary(isCompressible);
                                if (!primaries.AllocWeights(p, pLimit, pCount))
                                {
                                    throw new InvalidOperationException("primary tailoring gap too small");
                                }
                                pIsTailored = true;
                            }
                            p = primaries.NextWeight();
                            s = Collator.CommonWeight16;
                            sIsTailored = false;
                        }
                        t = s == 0 ? 0u : Collator.CommonWeight16;
                        tIsTailored = false;
                    }
                    q = 0;
                }
                if (IsTailoredNode(node))
                {
                    _nodes[i] = Collator.MakeCe(p, s, t, q);
                }
            }
        }
    }

    private static int CountTailoredNodes(List<long> nodes, int i, int strength)
    {
        var count = 0;
        for (;;)
        {
            if (i == 0)
            {
                break;
            }
            var node = nodes[i];
            if (StrengthFromNode(node) < strength)
            {
                break;
            }
            if (StrengthFromNode(node) == strength)
            {
                if (IsTailoredNode(node))
                {
                    ++count;
                }
                else
                {
                    break;
                }
            }
            i = NextIndexFromNode(node);
        }
        return count;
    }

    private void FinalizeCEs()
    {
        var newBuilder = new CollationDataBuilder(_getFcd16, _decimalDigits);
        newBuilder.InitForTailoring(_baseData);
        var finalizer = new CeFinalizer([.. _nodes]);
        newBuilder.CopyFrom(_dataBuilder, finalizer);
        _dataBuilder = newBuilder;
    }
}
