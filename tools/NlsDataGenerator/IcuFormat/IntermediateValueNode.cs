namespace NlsDataGenerator.IcuFormat;

// A value attached in front of another node, ported from StringTrieBuilder::IntermediateValueNode.
// Used when the trie type does not allow match nodes to carry values directly.
internal sealed class IntermediateValueNode : ValueNode
{
    private readonly TrieNode _next;

    public IntermediateValueNode(int value, TrieNode next)
        : base(unchecked((int)(0x222222u * 37u + (uint)HashOf(next))))
    {
        _next = next;
        SetValue(value);
    }

    protected override bool NodeEquals(TrieNode other)
    {
        return base.NodeEquals(other) && ReferenceEquals(_next, ((IntermediateValueNode)other)._next);
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
        Offset = builder.WriteValueAndFinal(Value, false);
    }
}
