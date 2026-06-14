using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// The getCEs path of CollationDataBuilder: computes the collation elements of a string from the
// in-progress builder data, via a DataBuilderCollationIterator. The tailoring builder uses it to learn
// the CEs of reset/relation/closure strings. This also exposes the internal accessors that iterator
// needs (the trie/array snapshots and the builder-data CE32 resolver).
internal partial class CollationDataBuilder
{
    private DataBuilderCollationIterator? _collIter;

    // Whether any mappings have been added (so a tailoring needs its own data vs. inheriting the base).
    public bool HasMappings => Modified;

    // Prepares this builder to tailor the given base: opens a trie whose default falls back to the
    // base, preallocates the Latin-1 letter block (for sort-key locality), marks Hangul syllables with
    // the HANGUL_TAG (they are tailorable only via Jamo), and copies the base's unsafe-backward set.
    public void InitForTailoring(CollationData baseData)
    {
        Base = baseData;
        Trie = new Trie2Builder(Collator.FallbackCe32, Collator.FffdCe32);
        for (var c = 0xc0; c <= 0xff; ++c)
        {
            Trie.Set(c, Collator.FallbackCe32);
        }
        var hangulCe32 = Collator.MakeCe32FromTagAndIndex(Collator.HangulTag, 0);
        Trie.SetRange(Hangul.HangulBase, Hangul.HangulEnd, hangulCe32, true);
        UnsafeBackwardSet.AddAll(baseData.UnsafeBackwardSet!);
    }

    // Serializes the builder's data into a CollationData. The tailoring path copies the base's
    // numeric/compressible/script tables; CollationBaseDataBuilder overrides this for the root, which
    // supplies those itself.
    public virtual void Build(CollationData data)
    {
        BuildMappings(data);
        if (Base is not null)
        {
            data.NumericPrimary = Base.NumericPrimary;
            data.CompressibleBytes = Base.CompressibleBytes;
            data.NumScripts = Base.NumScripts;
            data.ScriptsIndex = Base.ScriptsIndex;
            data.ScriptStarts = Base.ScriptStarts;
            data.ScriptStartsLength = Base.ScriptStartsLength;
        }
        BuildFastLatinTable(data);
    }

    public int GetCEs(string s, long[] ces, int cesLength)
    {
        return GetCEs(s, 0, ces, cesLength);
    }

    public int GetCEs(string prefix, string s, long[] ces, int cesLength)
    {
        if (prefix.Length == 0)
        {
            return GetCEs(s, 0, ces, cesLength);
        }
        return GetCEs(prefix + s, prefix.Length, ces, cesLength);
    }

    private int GetCEs(string s, int start, long[] ces, int cesLength)
    {
        _collIter ??= new DataBuilderCollationIterator(this);
        return _collIter.FetchCEs(s, start, ces, cesLength);
    }

    internal uint TrieGet(int c)
    {
        return Trie.Get(c);
    }

    internal CollationData? BuilderBase => Base;

    internal Func<int, int> Fcd16 => _getFcd16;

    internal Trie2Builder TrieRef => Trie;

    internal uint[] Ce32Snapshot()
    {
        return [.. Ce32s];
    }

    internal long[] Ce64Snapshot()
    {
        return [.. Ce64s];
    }

    internal char[] ContextsSnapshot()
    {
        return [.. Contexts];
    }

    // Resolves a BUILDER_DATA_TAG CE32 to its current runtime CE32, building the context if needed.
    internal uint GetCe32FromBuilderData(uint ce32)
    {
        if ((ce32 & IsBuilderJamoCe32) != 0)
        {
            var jamo = Collator.IndexFromCe32(ce32);
            return Trie.Get(jamo);
        }
        var cond = GetConditionalCe32ForCe32(ce32);
        if (cond.BuiltCe32 == Collator.NoCe32 || cond.Era != _contextsEra)
        {
            // Build the context-sensitive mappings into runtime form and cache the result. On overflow,
            // discard the abandoned intermediate contexts (ClearContexts bumps the era, invalidating
            // every cached builtCE32) and rebuild from an empty contexts array.
            if (!TryBuildContext(cond, out var built))
            {
                ClearContexts();
                if (!TryBuildContext(cond, out built))
                {
                    throw new InvalidOperationException("collation context index overflow");
                }
            }
            cond.BuiltCe32 = built;
            cond.Era = _contextsEra;
        }
        return cond.BuiltCe32;
    }
}
