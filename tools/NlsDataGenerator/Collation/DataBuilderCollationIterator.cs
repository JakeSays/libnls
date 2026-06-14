namespace NlsDataGenerator.Collation;

// A CollationIterator over a CollationDataBuilder's in-progress data, ported from
// collationdatabuilder.cpp's DataBuilderCollationIterator. Unlike the runtime iterator it reads CE32s
// from the builder's mutable trie and resolves BUILDER_DATA_TAG CE32s (Jamo indirections and built
// contexts) through the builder. fetchCEs walks a string and collects its non-zero CEs — this is the
// builder's getCEs, used for reset/relation/closure strings.
internal sealed class DataBuilderCollationIterator : CollationIterator
{
    private readonly CollationDataBuilder _builder;
    private readonly CollationData _builderData;
    private string _s = "";
    private int _pos;

    public DataBuilderCollationIterator(CollationDataBuilder builder)
        : base(BuildBuilderData(builder), false)
    {
        _builder = builder;
        _builderData = Data;
    }

    private static CollationData BuildBuilderData(CollationDataBuilder builder)
    {
        var data = new CollationData
        {
            Base = builder.BuilderBase,
            Fcd16Provider = builder.Fcd16,
            Trie = builder.TrieRef,
        };
        var jamo = new uint[CollationData.JamoCe32sLength];
        for (var j = 0; j < jamo.Length; ++j)
        {
            var cp = CollationDataBuilder.JamoCpFromIndex(j);
            jamo[j] = Collator.MakeCe32FromTagAndIndex(Collator.BuilderDataTag, cp)
                | CollationDataBuilder.IsBuilderJamoCe32;
        }
        data.JamoCe32Override = jamo;
        return data;
    }

    public int FetchCEs(string str, int start, long[] ces, int cesLength)
    {
        _builderData.Ce32s = _builder.Ce32Snapshot();
        _builderData.Ces = _builder.Ce64Snapshot();
        _builderData.Contexts = _builder.ContextsSnapshot();
        ClearCEs();
        _s = str;
        _pos = start;
        while (_pos < _s.Length)
        {
            ClearCEs();
            var c = char.ConvertToUtf32(_s, _pos);
            _pos += c > 0xFFFF ? 2 : 1;
            var ce32 = _builder.TrieGet(c);
            CollationData d;
            if (ce32 == Collator.FallbackCe32)
            {
                d = _builder.BuilderBase!;
                ce32 = d.GetCe32(c);
            }
            else
            {
                d = _builderData;
            }
            AppendCEsFromCe32(d, c, ce32, true);
            for (var i = 0; i < GetCesLength(); ++i)
            {
                var ce = GetCe(i);
                if (ce != 0)
                {
                    if (cesLength < Collator.MaxExpansionLength)
                    {
                        ces[cesLength] = ce;
                    }
                    ++cesLength;
                }
            }
        }
        return cesLength;
    }

    public override int GetOffset()
    {
        return _pos;
    }

    public override void ResetToOffset(int newOffset)
    {
        ClearCEs();
        _pos = newOffset;
    }

    public override int NextCodePoint()
    {
        if (_pos == _s.Length)
        {
            return -1;
        }
        var c = char.ConvertToUtf32(_s, _pos);
        _pos += c > 0xFFFF ? 2 : 1;
        return c;
    }

    public override int PreviousCodePoint()
    {
        if (_pos == 0)
        {
            return -1;
        }
        var c = char.ConvertToUtf32(_s, _pos - (char.IsLowSurrogate(_s[_pos - 1]) ? 2 : 1));
        _pos -= c > 0xFFFF ? 2 : 1;
        return c;
    }

    protected override void ForwardNumCodePoints(int num)
    {
        while (num-- > 0 && _pos < _s.Length)
        {
            var c = char.ConvertToUtf32(_s, _pos);
            _pos += c > 0xFFFF ? 2 : 1;
        }
    }

    protected override void BackwardNumCodePoints(int num)
    {
        while (num-- > 0 && _pos > 0)
        {
            _pos -= char.IsLowSurrogate(_s[_pos - 1]) ? 2 : 1;
        }
    }

    protected override uint GetDataCe32(int c)
    {
        return _builder.TrieGet(c);
    }

    protected override uint GetCe32FromBuilderData(uint ce32)
    {
        var result = _builder.GetCe32FromBuilderData(ce32);
        _builderData.Contexts = _builder.ContextsSnapshot();
        return result;
    }
}
