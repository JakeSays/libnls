namespace NlsDataGenerator.IcuFormat;

// A binary split of a wide branch on a middle unit, ported from StringTrieBuilder::SplitBranchNode.
// Built recursively over a branch with more than MaxBranchLinearSubNodeLength units.
internal sealed class SplitBranchNode : BranchNode
{
    private readonly char _unit;
    private readonly TrieNode _lessThan;
    private readonly TrieNode _greaterOrEqual;

    public SplitBranchNode(char middleUnit, TrieNode lessThan, TrieNode greaterOrEqual)
        : base(unchecked((int)(((0x555555u * 37u + middleUnit) * 37u + (uint)HashOf(lessThan)) * 37u
            + (uint)HashOf(greaterOrEqual))))
    {
        _unit = middleUnit;
        _lessThan = lessThan;
        _greaterOrEqual = greaterOrEqual;
    }

    protected override bool NodeEquals(TrieNode other)
    {
        var o = (SplitBranchNode)other;
        return _unit == o._unit
            && ReferenceEquals(_lessThan, o._lessThan)
            && ReferenceEquals(_greaterOrEqual, o._greaterOrEqual);
    }

    public override int MarkRightEdgesFirst(int edgeNumber)
    {
        if (Offset == 0)
        {
            FirstEdgeNumber = edgeNumber;
            edgeNumber = _greaterOrEqual.MarkRightEdgesFirst(edgeNumber);
            edgeNumber = _lessThan.MarkRightEdgesFirst(edgeNumber - 1);
            Offset = edgeNumber;
        }
        return edgeNumber;
    }

    public override void Write(UCharsTrieBuilder builder)
    {
        // Encode the less-than branch first, the greater-or-equal branch last (no jump for it).
        _lessThan.WriteUnlessInsideRightEdge(FirstEdgeNumber, _greaterOrEqual.OffsetValue, builder);
        _greaterOrEqual.Write(builder);
        builder.WriteDeltaTo(_lessThan.OffsetValue);
        Offset = builder.WriteUnit(_unit);
    }
}
