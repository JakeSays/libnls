namespace NlsDataGenerator.IcuFormat;

// Base node of the UCharsTrie builder's DAG, ported from StringTrieBuilder::Node
// (stringtriebuilder.cpp). Nodes are deduplicated by value-equality during the SMALL build; each
// remembers its serialized offset. The two-pass write first numbers right-branch edges
// (MarkRightEdgesFirst), then serializes (Write), writing rightmost edges last because trie units
// are written back-to-front and the rightmost edge continues without a jump.
internal abstract class TrieNode
{
    protected int Hash;
    protected int Offset;

    protected TrieNode(int initialHash)
    {
        Hash = initialHash;
    }

    public int OffsetValue => Offset;

    public static int HashOf(TrieNode? node)
    {
        return node?.Hash ?? 0;
    }

    public override int GetHashCode()
    {
        return Hash;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        return obj is TrieNode other && GetType() == other.GetType() && NodeEquals(other);
    }

    // Compares the type-specific fields; the caller has already checked the runtime type.
    protected abstract bool NodeEquals(TrieNode other);

    public virtual int MarkRightEdgesFirst(int edgeNumber)
    {
        if (Offset == 0)
        {
            Offset = edgeNumber;
        }
        return edgeNumber;
    }

    public abstract void Write(UCharsTrieBuilder builder);

    public void WriteUnlessInsideRightEdge(int firstRight, int lastRight, UCharsTrieBuilder builder)
    {
        // Edge numbers are negative, lastRight <= firstRight. Skip if already written (offset>0) or
        // if this node is part of the not-yet-written right branch edge.
        if (Offset < 0 && (Offset < lastRight || firstRight < Offset))
        {
            Write(builder);
        }
    }
}
