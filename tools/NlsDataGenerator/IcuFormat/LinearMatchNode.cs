namespace NlsDataGenerator.IcuFormat;

// A node that matches a fixed sequence of units before continuing, ported from StringTrieBuilder's
// LinearMatchNode together with UCharsTrieBuilder::UCTLinearMatchNode (this port is char16-only, so
// the two are merged). Holds the literal units and the next node.
internal sealed class LinearMatchNode : ValueNode
{
    private readonly char[] _units;
    private readonly int _length;
    private readonly TrieNode _next;

    public LinearMatchNode(char[] units, int length, TrieNode next)
        : base(unchecked((int)((0x333333u * 37u + (uint)length) * 37u + (uint)HashOf(next))))
    {
        _units = units;
        _length = length;
        _next = next;
        Hash = unchecked((int)((uint)Hash * 37u + (uint)HashUnits(units, length)));
    }

    private static int HashUnits(char[] s, int length)
    {
        var h = 0u;
        for (var i = 0; i < length; ++i)
        {
            h = h * 37u + s[i];
        }
        return (int)h;
    }

    protected override bool NodeEquals(TrieNode other)
    {
        if (!base.NodeEquals(other))
        {
            return false;
        }
        var o = (LinearMatchNode)other;
        if (_length != o._length || !ReferenceEquals(_next, o._next))
        {
            return false;
        }
        for (var i = 0; i < _length; ++i)
        {
            if (_units[i] != o._units[i])
            {
                return false;
            }
        }
        return true;
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
        builder.WriteUnits(_units, _length);
        Offset = builder.WriteValueAndType(HasValue, Value, builder.MinLinearMatch + _length - 1);
    }
}
