namespace NlsDataGenerator.Collation;

// The CEModifier used by CollationBuilder.finalizeCEs (ported from collationbuilder.cpp's CEFinalizer):
// it replaces a temporary CE32/CE — which encodes a weight-graph node index — with the final CE that
// makeTailoredCEs computed for that node, preserving the case bits. Non-temporary values are left
// unchanged (Collation.NoCe).
internal sealed class CeFinalizer : ICeModifier
{
    private readonly long[] _finalCEs;

    public CeFinalizer(long[] finalCEs)
    {
        _finalCEs = finalCEs;
    }

    public long ModifyCe32(uint ce32)
    {
        if (CollationBuilder.IsTempCe32(ce32))
        {
            return _finalCEs[CollationBuilder.IndexFromTempCe32(ce32)] | ((long)(ce32 & 0xc0) << 8);
        }
        return Collator.NoCe;
    }

    public long ModifyCe(long ce)
    {
        if (CollationBuilder.IsTempCe(ce))
        {
            return _finalCEs[CollationBuilder.IndexFromTempCe(ce)] | (ce & 0xc000);
        }
        return Collator.NoCe;
    }
}
