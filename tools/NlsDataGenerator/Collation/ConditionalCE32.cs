namespace NlsDataGenerator.Collation;

// Build-time context and CE32 for a code point, ported from ICU's ConditionalCE32
// (collationdatabuilder.cpp). A code point with contextual mappings stores its default (no-context)
// mapping and all conditional mappings as a singly linked list of these, sorted by context string.
// Context strings sort by prefix length, then prefix, then contraction suffix; they are unique and
// ascending.
internal sealed class ConditionalCE32
{
    public ConditionalCE32(string context, uint ce32)
    {
        Context = context;
        Ce32 = ce32;
    }

    // "\0" for the first (default) entry; otherwise one unit holding the prefix length, then the
    // prefix string, then the contraction suffix.
    public string Context { get; set; }

    // CE32 for the code point and its context. May be special (e.g. an expansion) but not contextual.
    public uint Ce32 { get; set; }

    // Default CE32 for all contexts with this same prefix; set only while building runtime data.
    public uint DefaultCe32 { get; set; } = Collator.NoCe32;

    // CE32 for the built contexts, cached in the list head; reset when contexts are modified.
    public uint BuiltCe32 { get; set; } = Collator.NoCe32;

    // The build "era" when BuiltCe32 was set; clearContexts() bumps the era to invalidate caches.
    public int Era { get; set; } = -1;

    // Index of the next ConditionalCE32, or negative at the end of the list.
    public int Next { get; set; } = -1;

    public bool HasContext()
    {
        return Context.Length > 1;
    }

    public int PrefixLength()
    {
        return Context[0];
    }
}
