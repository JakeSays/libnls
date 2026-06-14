namespace NlsDataGenerator.Normalization;

// The canonical-closure data the CanonicalIterator needs, equivalent to ICU's CanonIterData built by
// Normalizer2Impl::ensureCanonIterData. It answers two questions per code point: whether the code
// point starts a new canonical segment, and the set of code points whose canonical decomposition
// begins with it (its "canonical start set"). ICU derives these from the norm16 trie; we derive them
// directly from the in-memory normalization data.
//
// A code point X is in the start set of c when X's full NFD begins with c. That happens two ways:
//   - X is a two-way (round-trip) composite c∘trail — found in c's forward-composition list at query
//     time (ICU adds these from the compositions list rather than storing them);
//   - X is a one-way canonical mapping (a composition-excluded composite or a singleton like the
//     Angstrom sign) whose full NFD starts with c — precomputed here into _startSets.
// Hangul leading jamo additionally start every syllable in their L-block.
internal sealed class CanonicalClosureData
{
    private readonly CodePointDataTable _norms;
    private readonly Dictionary<int, SortedSet<int>> _startSets = [];
    private readonly HashSet<int> _notSegmentStarter = [];

    public CanonicalClosureData(CodePointDataTable norms)
    {
        _norms = norms;
        foreach (var entry in norms.AllEntries)
        {
            var data = entry.Value;
            // A non-zero combining class, or a "maybe" trail that composes back but has no
            // decomposition of its own, makes the code point a non-starter.
            if (data.Cc != 0 || (data.CombinesBack && !data.HasMapping()))
            {
                _notSegmentStarter.Add(entry.Key);
            }
            if (data.Kind == MappingKind.OneWay && data.Mapping is not null && data.Mapping.Length != 0)
            {
                var first = char.ConvertToUtf32(data.Mapping, 0);
                if (!_startSets.TryGetValue(first, out var set))
                {
                    set = [];
                    _startSets[first] = set;
                }
                set.Add(entry.Key);
                // The remaining code points of a one-way mapping are non-starters even when they have
                // combining class 0 (e.g. the Tibetan subjoined letters in 0F69's decomposition).
                var i = char.IsHighSurrogate(data.Mapping[0]) ? 2 : 1;
                while (i < data.Mapping.Length)
                {
                    var cp = char.ConvertToUtf32(data.Mapping, i);
                    i += char.IsHighSurrogate(data.Mapping[i]) ? 2 : 1;
                    _notSegmentStarter.Add(cp);
                }
            }
        }
        // Hangul vowel and trailing jamo compose back onto a preceding jamo.
        for (var v = Hangul.JamoVBase; v <= Hangul.JamoVEnd; ++v)
        {
            _notSegmentStarter.Add(v);
        }
        for (var t = Hangul.JamoTBase + 1; t <= Hangul.JamoTEnd; ++t)
        {
            _notSegmentStarter.Add(t);
        }
    }

    // Whether c starts a new canonical segment: it is not a combining mark, a composition trail, or a
    // non-first code point of a one-way decomposition.
    public bool IsCanonSegmentStarter(int c)
    {
        return !_notSegmentStarter.Contains(c);
    }

    // Fills set with every code point whose canonical decomposition begins with c. Returns false (and
    // leaves set empty) when there are none.
    public bool GetCanonStartSet(int c, SortedSet<int> set)
    {
        set.Clear();
        if (_startSets.TryGetValue(c, out var oneWay))
        {
            set.UnionWith(oneWay);
        }
        var norm = _norms.GetNorm(c);
        // Only a non-composite starter contributes its compositions here. A round-trip composite
        // that itself composes forward (Ü → Ǖ) is skipped: its composites are reached through the
        // recursion from the original starter's set (Ǖ lands in U's set, not Ü's).
        if (norm?.Compositions is not null && !norm.HasMapping())
        {
            AddComposites(norm, set);
        }
        if (Hangul.IsJamoL(c))
        {
            var syllable = Hangul.HangulBase + (c - Hangul.JamoLBase) * Hangul.JamoVtCount;
            for (var s = syllable; s < syllable + Hangul.JamoVtCount; ++s)
            {
                set.Add(s);
            }
        }
        return set.Count > 0;
    }

    // Adds the composites that norm composes forward into, recursing into any composite that itself
    // composes forward — so a composite formed from another composite (A∘diaeresis = Ä, then Ä∘macron
    // = Ǟ) lands in the start set of the original starter.
    private void AddComposites(CodePointData norm, SortedSet<int> set)
    {
        foreach (var pair in norm.Compositions!)
        {
            var compositeNorm = _norms.GetNorm(pair.Composite);
            if (compositeNorm?.Compositions is not null)
            {
                AddComposites(compositeNorm, set);
            }
            set.Add(pair.Composite);
        }
    }
}
