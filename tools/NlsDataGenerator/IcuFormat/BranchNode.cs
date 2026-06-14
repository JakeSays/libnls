namespace NlsDataGenerator.IcuFormat;

// Base class for the two branch node kinds (list branch, split branch), ported from
// StringTrieBuilder::BranchNode. Remembers the first edge number assigned during the
// right-edge-numbering pass.
internal abstract class BranchNode : TrieNode
{
    protected int FirstEdgeNumber;

    protected BranchNode(int initialHash)
        : base(initialHash)
    {
    }
}
