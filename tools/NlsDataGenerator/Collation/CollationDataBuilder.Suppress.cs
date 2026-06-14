namespace NlsDataGenerator.Collation;

// [suppressContractions [set]] support, ported from collationdatabuilder.cpp's suppressContractions.
// For each code point in the set, replace any contextual (prefix/contraction) mapping with its
// context-free default CE32, so contractions starting with those characters no longer fire. Mappings
// that are still inherited from the base are copied without their context; builder-local contextual
// mappings are collapsed to the default and dropped from contextChars.
internal partial class CollationDataBuilder
{
    public void SuppressContractions(UnicodeSet set)
    {
        if (set.IsEmpty)
        {
            return;
        }
        foreach (var c in set.CodePoints)
        {
            var ce32 = Trie.Get(c);
            if (ce32 == Collator.FallbackCe32)
            {
                ce32 = Base!.GetFinalCe32(Base.GetCe32(c));
                if (Collator.Ce32HasContext(ce32))
                {
                    ce32 = CopyFromBaseCe32(c, ce32, false);
                    Trie.Set(c, ce32);
                }
            }
            else if (IsBuilderContextCe32(ce32))
            {
                ce32 = GetConditionalCe32ForCe32(ce32).Ce32;
                // Abandon the list of ConditionalCE32; copying the builder at the end drops it.
                Trie.Set(c, ce32);
                ContextChars.Remove(c, c);
            }
        }
        Modified = true;
    }
}
