namespace NlsDataGenerator.Collation;

// Thrown when a collation rule string is malformed, replacing ICU's UErrorCode + errorReason
// threading. Carries the reason and the rule offset where parsing failed, with the surrounding
// context, mirroring ICU's UParseError pre/post-context.
internal sealed class CollationParseException : Exception
{
    public int Offset { get; }
    public string PreContext { get; }
    public string PostContext { get; }

    public CollationParseException(string reason, int offset, string preContext, string postContext)
        : base($"{reason} at offset {offset}: ...{preContext}!{postContext}...")
    {
        Offset = offset;
        PreContext = preContext;
        PostContext = postContext;
    }
}
