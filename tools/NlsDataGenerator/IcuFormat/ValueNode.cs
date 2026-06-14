namespace NlsDataGenerator.IcuFormat;

// A node that may carry an attached value, ported from StringTrieBuilder::ValueNode. Base class for
// the match nodes (linear-match, branch-head) and the intermediate-value node.
internal abstract class ValueNode : TrieNode
{
    protected bool HasValue;
    protected int Value;

    protected ValueNode(int initialHash)
        : base(initialHash)
    {
    }

    // Called from subclass constructors and, for match nodes, by the builder after construction
    // (the value is not known until then); updating the hash mirrors ICU.
    internal void SetValue(int v)
    {
        HasValue = true;
        Value = v;
        Hash = unchecked((int)((uint)Hash * 37u + (uint)v));
    }

    protected override bool NodeEquals(TrieNode other)
    {
        var o = (ValueNode)other;
        return HasValue == o.HasValue && (!HasValue || Value == o.Value);
    }
}
