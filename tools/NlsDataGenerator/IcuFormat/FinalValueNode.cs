namespace NlsDataGenerator.IcuFormat;

// A leaf node holding a final value, ported from StringTrieBuilder::FinalValueNode.
internal sealed class FinalValueNode : TrieNode
{
    private readonly int _value;

    public FinalValueNode(int value)
        : base(unchecked((int)(0x111111u * 37u + (uint)value)))
    {
        _value = value;
    }

    protected override bool NodeEquals(TrieNode other)
    {
        return _value == ((FinalValueNode)other)._value;
    }

    public override void Write(UCharsTrieBuilder builder)
    {
        Offset = builder.WriteValueAndFinal(_value, true);
    }
}
