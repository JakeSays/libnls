namespace NlsDataGenerator.Collation;

// Adding (prefix, string) -> CEs mappings, ported from collationdatabuilder.cpp's add/addCE32
// (non-ICU4X path). A mapping with no context sets the code point's trie value directly; a mapping
// with a prefix and/or contraction suffix is recorded as a sorted linked list of ConditionalCE32,
// later compiled into runtime prefix/contraction tries by buildContext.
internal partial class CollationDataBuilder
{
    public void Add(string prefix, string s, long[] ces, int cesLength)
    {
        var ce32 = EncodeCEs(ces, cesLength);
        AddCe32(prefix, s, ce32);
    }

    public void AddCe32(string prefix, string s, uint ce32)
    {
        if (string.IsNullOrEmpty(s))
        {
            throw new ArgumentException("empty mapping string", nameof(s));
        }
        var c = char.ConvertToUtf32(s, 0);
        var cLength = c >= 0x10000 ? 2 : 1;
        var oldCe32 = Trie.Get(c);
        var hasContext = prefix.Length != 0 || s.Length > cLength;

        if (oldCe32 == Collator.FallbackCe32)
        {
            // First tailoring for c: if c has contextual base mappings, or we are adding a contextual
            // mapping, copy the base mappings first so they are preserved; otherwise just override.
            var baseCe32 = Base!.GetFinalCe32(Base.GetCe32(c));
            if (hasContext || Collator.Ce32HasContext(baseCe32))
            {
                oldCe32 = CopyFromBaseCe32(c, baseCe32, true);
                Trie.Set(c, oldCe32);
            }
        }

        if (!hasContext)
        {
            // No prefix, no contraction.
            if (!IsBuilderContextCe32(oldCe32))
            {
                Trie.Set(c, ce32);
            }
            else
            {
                var cond = GetConditionalCe32ForCe32(oldCe32);
                cond.BuiltCe32 = Collator.NoCe32;
                cond.Ce32 = ce32;
            }
        }
        else
        {
            ConditionalCE32 cond;
            if (!IsBuilderContextCe32(oldCe32))
            {
                // Replace the simple oldCe32 with a builder-context CE32 pointing at a new list head.
                var index = AddConditionalCe32("\0", oldCe32);
                var contextCe32 = MakeBuilderContextCe32(index);
                Trie.Set(c, contextCe32);
                ContextChars.Add(c);
                cond = GetConditionalCe32(index);
            }
            else
            {
                cond = GetConditionalCe32ForCe32(oldCe32);
                cond.BuiltCe32 = Collator.NoCe32;
            }
            var suffix = s[cLength..];
            var context = (char)prefix.Length + prefix + suffix;
            UnsafeBackwardSet.AddAll(suffix);
            for (;;)
            {
                // Invariant: context > cond.Context.
                var next = cond.Next;
                if (next < 0)
                {
                    // Append a new ConditionalCE32 after cond.
                    cond.Next = AddConditionalCe32(context, ce32);
                    break;
                }
                var nextCond = GetConditionalCe32(next);
                var cmp = string.CompareOrdinal(context, nextCond.Context);
                if (cmp < 0)
                {
                    // Insert a new ConditionalCE32 between cond and nextCond.
                    var index = AddConditionalCe32(context, ce32);
                    cond.Next = index;
                    GetConditionalCe32(index).Next = next;
                    break;
                }
                if (cmp == 0)
                {
                    // Same context as before; overwrite its ce32.
                    nextCond.Ce32 = ce32;
                    break;
                }
                cond = nextCond;
            }
        }
        Modified = true;
    }
}
