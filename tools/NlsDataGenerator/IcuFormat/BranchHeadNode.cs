namespace NlsDataGenerator.IcuFormat;

// The head unit of a branch node, ported from StringTrieBuilder::BranchHeadNode. Writes the node
// lead unit (with the branch length and any attached value) in front of the branch sub-node.
internal sealed class BranchHeadNode : ValueNode
{
    private readonly int _length;
    private readonly TrieNode _next;

    public BranchHeadNode(int length, TrieNode next)
        : base(unchecked((int)((0x666666u * 37u + (uint)length) * 37u + (uint)HashOf(next))))
    {
        _length = length;
        _next = next;
    }

    protected override bool NodeEquals(TrieNode other)
    {
        if (!base.NodeEquals(other))
        {
            return false;
        }
        var o = (BranchHeadNode)other;
        return _length == o._length && ReferenceEquals(_next, o._next);
    }

    public override int MarkRightEdgesFirst(int edgeNumber)
    {
        if (Offset == 0)
        {
            edgeNumber = _next.MarkRightEdgesFirst(edgeNumber);
            Offset = edgeNumber;
        }
        return edgeNumber;
    }

    public override void Write(UCharsTrieBuilder builder)
    {
        _next.Write(builder);
        if (_length <= builder.MinLinearMatch)
        {
            Offset = builder.WriteValueAndType(HasValue, Value, _length - 1);
        }
        else
        {
            builder.WriteUnit(_length - 1);
            Offset = builder.WriteValueAndType(HasValue, Value, 0);
        }
    }
}
