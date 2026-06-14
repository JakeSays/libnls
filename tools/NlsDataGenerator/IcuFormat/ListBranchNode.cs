namespace NlsDataGenerator.IcuFormat;

// A small branch listing up to MaxBranchLinearSubNodeLength units, each leading either to a final
// value or to a sub-node, ported from StringTrieBuilder::ListBranchNode.
internal sealed class ListBranchNode : BranchNode
{
    private readonly char[] _units = new char[UCharsTrieFormat.MaxBranchLinearSubNodeLength];
    private readonly int[] _values = new int[UCharsTrieFormat.MaxBranchLinearSubNodeLength];
    private readonly TrieNode?[] _equal = new TrieNode?[UCharsTrieFormat.MaxBranchLinearSubNodeLength];
    private int _length;

    public ListBranchNode()
        : base(0x444444)
    {
    }

    // Adds a unit with a final value.
    public void Add(int c, int value)
    {
        _units[_length] = (char)c;
        _equal[_length] = null;
        _values[_length] = value;
        ++_length;
        Hash = unchecked((int)(((uint)Hash * 37u + (uint)c) * 37u + (uint)value));
    }

    // Adds a unit which leads to another match node.
    public void Add(int c, TrieNode node)
    {
        _units[_length] = (char)c;
        _equal[_length] = node;
        _values[_length] = 0;
        ++_length;
        Hash = unchecked((int)(((uint)Hash * 37u + (uint)c) * 37u + (uint)HashOf(node)));
    }

    protected override bool NodeEquals(TrieNode other)
    {
        var o = (ListBranchNode)other;
        if (_length != o._length)
        {
            return false;
        }
        for (var i = 0; i < _length; ++i)
        {
            if (_units[i] != o._units[i] || _values[i] != o._values[i]
                || !ReferenceEquals(_equal[i], o._equal[i]))
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
            FirstEdgeNumber = edgeNumber;
            var step = 0;
            var i = _length;
            do
            {
                var edge = _equal[--i];
                if (edge is not null)
                {
                    edgeNumber = edge.MarkRightEdgesFirst(edgeNumber - step);
                }
                // For all but the rightmost edge, decrement the edge number.
                step = 1;
            }
            while (i > 0);
            Offset = edgeNumber;
        }
        return edgeNumber;
    }

    public override void Write(UCharsTrieBuilder builder)
    {
        // Write sub-nodes in reverse order so the minUnit sub-node (written last) has a short delta.
        var unitNumber = _length - 1;
        var rightEdge = _equal[unitNumber];
        var rightEdgeNumber = rightEdge is null ? FirstEdgeNumber : rightEdge.OffsetValue;
        do
        {
            --unitNumber;
            _equal[unitNumber]?.WriteUnlessInsideRightEdge(FirstEdgeNumber, rightEdgeNumber, builder);
        }
        while (unitNumber > 0);
        // The maxUnit sub-node is written last because we do not jump for it at all.
        unitNumber = _length - 1;
        if (rightEdge is null)
        {
            builder.WriteValueAndFinal(_values[unitNumber], true);
        }
        else
        {
            rightEdge.Write(builder);
        }
        Offset = builder.WriteUnit(_units[unitNumber]);
        // Write the rest of this node's unit-value pairs.
        while (--unitNumber >= 0)
        {
            int value;
            bool isFinal;
            if (_equal[unitNumber] is null)
            {
                value = _values[unitNumber];
                isFinal = true;
            }
            else
            {
                value = Offset - _equal[unitNumber]!.OffsetValue;
                isFinal = false;
            }
            builder.WriteValueAndFinal(value, isFinal);
            Offset = builder.WriteUnit(_units[unitNumber]);
        }
    }
}
