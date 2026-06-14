using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// The runtime collation-element engine, ported from ICU's CollationIterator (collationiterator.cpp).
// Given a data object and a source of code points, it produces the sequence of 64-bit CEs by expanding
// each code point's CE32 — handling the full set of CE32 special tags (long primary/secondary,
// expansions, prefixes, contiguous and discontiguous contractions, Hangul, offsets, implicit weights,
// fallback to base data). Subclasses supply the code-point source: Utf16CollationIterator over a
// string, DataBuilderCollationIterator over a builder's in-progress data. The tailoring builder uses
// fetchCEs/getCEs to learn the CEs of reset and relation strings.
internal abstract class CollationIterator
{
    protected readonly CollationData Data;

    private readonly CeBuffer _ceBuffer = new();
    private int _cesIndex;
    private SkippedCombiningMarks? _skipped;
    private int _numCpFwd = -1;
    protected bool IsNumeric;

    protected CollationIterator(CollationData data, bool numeric)
    {
        Data = data;
        IsNumeric = numeric;
    }

    public abstract int GetOffset();
    public abstract void ResetToOffset(int newOffset);
    public abstract int NextCodePoint();
    public abstract int PreviousCodePoint();
    protected abstract void ForwardNumCodePoints(int num);
    protected abstract void BackwardNumCodePoints(int num);

    protected virtual uint HandleNextCE32(out int c)
    {
        c = NextCodePoint();
        return c < 0 ? Collator.FallbackCe32 : Data.GetCe32(c);
    }

    protected virtual char HandleGetTrailSurrogate()
    {
        return '\0';
    }

    protected virtual bool FoundNullTerminator()
    {
        return false;
    }

    protected virtual bool ForbidSurrogateCodePoints()
    {
        return false;
    }

    protected virtual uint GetDataCe32(int c)
    {
        return Data.GetCe32(c);
    }

    protected virtual uint GetCe32FromBuilderData(uint ce32)
    {
        throw new NotSupportedException("builder data CE32 outside the builder");
    }

    public int GetCesLength()
    {
        return _ceBuffer.Length;
    }

    public long GetCe(int i)
    {
        return _ceBuffer.Get(i);
    }

    public void ClearCEs()
    {
        _cesIndex = 0;
        _ceBuffer.Length = 0;
    }

    public int FetchCEs()
    {
        while (NextCE() != Collator.NoCe)
        {
            _cesIndex = _ceBuffer.Length;
        }
        return _ceBuffer.Length;
    }

    public long NextCE()
    {
        if (_cesIndex < _ceBuffer.Length)
        {
            return _ceBuffer.Get(_cesIndex++);
        }
        _ceBuffer.EnsureAppendCapacity(1);
        ++_ceBuffer.Length;
        var ce32 = HandleNextCE32(out var c);
        var t = ce32 & 0xff;
        if (t < Collator.SpecialCe32LowByte)
        {
            return _ceBuffer.Set(_cesIndex++, SimpleCe(ce32, t));
        }
        CollationData d;
        if (t == Collator.SpecialCe32LowByte)
        {
            if (c < 0)
            {
                return _ceBuffer.Set(_cesIndex++, Collator.NoCe);
            }
            d = Data.Base!;
            ce32 = d.GetCe32(c);
            t = ce32 & 0xff;
            if (t < Collator.SpecialCe32LowByte)
            {
                return _ceBuffer.Set(_cesIndex++, SimpleCe(ce32, t));
            }
        }
        else
        {
            d = Data;
        }
        if (t == Collator.LongPrimaryCe32LowByte)
        {
            return _ceBuffer.Set(_cesIndex++, ((long)(ce32 - t) << 32) | Collator.CommonSecAndTerCe);
        }
        --_ceBuffer.Length;
        AppendCEsFromCe32(d, c, ce32, true);
        return _ceBuffer.Get(_cesIndex++);
    }

    private static long SimpleCe(uint ce32, uint t)
    {
        return ((long)(ce32 & 0xffff0000) << 32) | ((long)(ce32 & 0xff00) << 16) | ((long)t << 8);
    }

    protected void AppendCEsFromCe32(CollationData d, int c, uint ce32, bool forward)
    {
        while (Collator.IsSpecialCe32(ce32))
        {
            switch (Collator.TagFromCe32(ce32))
            {
                case Collator.FallbackTag:
                case Collator.ReservedTag3:
                    throw new InvalidOperationException("unexpected CE32 tag in collation data");
                case Collator.LongPrimaryTag:
                    _ceBuffer.Append(Collator.CeFromLongPrimaryCe32(ce32));
                    return;
                case Collator.LongSecondaryTag:
                    _ceBuffer.Append(Collator.CeFromLongSecondaryCe32(ce32));
                    return;
                case Collator.LatinExpansionTag:
                    _ceBuffer.EnsureAppendCapacity(2);
                    _ceBuffer.Set(_ceBuffer.Length, Collator.LatinCe0FromCe32(ce32));
                    _ceBuffer.Set(_ceBuffer.Length + 1, Collator.LatinCe1FromCe32(ce32));
                    _ceBuffer.Length += 2;
                    return;
                case Collator.Expansion32Tag:
                {
                    var index = Collator.IndexFromCe32(ce32);
                    var length = Collator.LengthFromCe32(ce32);
                    _ceBuffer.EnsureAppendCapacity(length);
                    for (var i = 0; i < length; ++i)
                    {
                        _ceBuffer.AppendUnsafe(Collator.CeFromCe32(d.Ce32s[index + i]));
                    }
                    return;
                }
                case Collator.ExpansionTag:
                {
                    var index = Collator.IndexFromCe32(ce32);
                    var length = Collator.LengthFromCe32(ce32);
                    _ceBuffer.EnsureAppendCapacity(length);
                    for (var i = 0; i < length; ++i)
                    {
                        _ceBuffer.AppendUnsafe(d.Ces[index + i]);
                    }
                    return;
                }
                case Collator.BuilderDataTag:
                    ce32 = GetCe32FromBuilderData(ce32);
                    if (ce32 == Collator.FallbackCe32)
                    {
                        d = Data.Base!;
                        ce32 = d.GetCe32(c);
                    }
                    break;
                case Collator.PrefixTag:
                    if (forward)
                    {
                        BackwardNumCodePoints(1);
                    }
                    ce32 = GetCe32FromPrefix(d, ce32);
                    if (forward)
                    {
                        ForwardNumCodePoints(1);
                    }
                    break;
                case Collator.ContractionTag:
                {
                    var p = Collator.IndexFromCe32(ce32);
                    var defaultCe32 = CollationData.ReadCe32(d.Contexts, p);
                    if (!forward)
                    {
                        ce32 = defaultCe32;
                        break;
                    }
                    int nextCp;
                    if (_skipped == null && _numCpFwd < 0)
                    {
                        nextCp = NextCodePoint();
                        if (nextCp < 0)
                        {
                            ce32 = defaultCe32;
                            break;
                        }
                        if ((ce32 & Collator.ContractNextCcc) != 0 && (d.GetFcd16(nextCp) >> 8) == 0)
                        {
                            BackwardNumCodePoints(1);
                            ce32 = defaultCe32;
                            break;
                        }
                    }
                    else
                    {
                        nextCp = NextSkippedCodePoint();
                        if (nextCp < 0)
                        {
                            ce32 = defaultCe32;
                            break;
                        }
                        if ((ce32 & Collator.ContractNextCcc) != 0 && (d.GetFcd16(nextCp) >> 8) == 0)
                        {
                            BackwardNumSkipped(1);
                            ce32 = defaultCe32;
                            break;
                        }
                    }
                    ce32 = NextCe32FromContraction(d, ce32, p + 2, defaultCe32, nextCp);
                    if (ce32 == Collator.NoCe32)
                    {
                        return;
                    }
                    break;
                }
                case Collator.DigitTag:
                    if (IsNumeric)
                    {
                        throw new NotSupportedException("numeric collation not supported");
                    }
                    ce32 = d.Ce32s[Collator.IndexFromCe32(ce32)];
                    break;
                case Collator.U0000Tag:
                    if (forward && FoundNullTerminator())
                    {
                        _ceBuffer.Append(Collator.NoCe);
                        return;
                    }
                    ce32 = d.Ce32s[0];
                    break;
                case Collator.HangulTag:
                {
                    c -= Hangul.HangulBase;
                    var tj = c % Hangul.JamoTCount;
                    c /= Hangul.JamoTCount;
                    var vj = c % Hangul.JamoVCount;
                    c /= Hangul.JamoVCount;
                    if ((ce32 & Collator.HangulNoSpecialJamo) != 0)
                    {
                        _ceBuffer.EnsureAppendCapacity(tj == 0 ? 2 : 3);
                        _ceBuffer.Set(_ceBuffer.Length, Collator.CeFromCe32(d.JamoCe32(c)));
                        _ceBuffer.Set(_ceBuffer.Length + 1, Collator.CeFromCe32(d.JamoCe32(19 + vj)));
                        _ceBuffer.Length += 2;
                        if (tj != 0)
                        {
                            _ceBuffer.AppendUnsafe(Collator.CeFromCe32(d.JamoCe32(39 + tj)));
                        }
                        return;
                    }
                    AppendCEsFromCe32(d, -1, d.JamoCe32(c), forward);
                    AppendCEsFromCe32(d, -1, d.JamoCe32(19 + vj), forward);
                    if (tj == 0)
                    {
                        return;
                    }
                    ce32 = d.JamoCe32(39 + tj);
                    c = -1;
                    break;
                }
                case Collator.LeadSurrogateTag:
                {
                    var trail = HandleGetTrailSurrogate();
                    if (trail >= 0xDC00 && trail <= 0xDFFF)
                    {
                        c = ((c - 0xD800) << 10) + (trail - 0xDC00) + 0x10000;
                        ce32 &= Collator.LeadTypeMask;
                        if (ce32 == Collator.LeadAllUnassigned)
                        {
                            ce32 = Collator.UnassignedCe32;
                        }
                        else if (ce32 == Collator.LeadAllFallback
                            || (ce32 = d.GetCe32FromSupplementary(c)) == Collator.FallbackCe32)
                        {
                            d = d.Base!;
                            ce32 = d.GetCe32FromSupplementary(c);
                        }
                    }
                    else
                    {
                        ce32 = Collator.UnassignedCe32;
                    }
                    break;
                }
                case Collator.OffsetTag:
                    _ceBuffer.Append(d.GetCeFromOffsetCe32(c, ce32));
                    return;
                case Collator.ImplicitTag:
                    if (c >= 0xD800 && c <= 0xDFFF && ForbidSurrogateCodePoints())
                    {
                        ce32 = Collator.FffdCe32;
                        break;
                    }
                    _ceBuffer.Append(Collator.UnassignedCeFromCodePoint(c));
                    return;
            }
        }
        _ceBuffer.Append(Collator.CeFromSimpleCe32(ce32));
    }

    private uint GetCe32FromPrefix(CollationData d, uint ce32)
    {
        var p = Collator.IndexFromCe32(ce32);
        ce32 = CollationData.ReadCe32(d.Contexts, p);
        var prefixes = new UCharsTrieMatcher(d.Contexts, p + 2);
        var lookBehind = 0;
        for (;;)
        {
            var c = PreviousCodePoint();
            if (c < 0)
            {
                break;
            }
            ++lookBehind;
            var match = prefixes.NextForCodePoint(c);
            if (UCharsTrieMatcher.HasValue(match))
            {
                ce32 = (uint)prefixes.GetValue();
            }
            if (!UCharsTrieMatcher.HasNext(match))
            {
                break;
            }
        }
        ForwardNumCodePoints(lookBehind);
        return ce32;
    }

    private int NextSkippedCodePoint()
    {
        if (_skipped != null && _skipped.HasNext())
        {
            return _skipped.Next();
        }
        if (_numCpFwd == 0)
        {
            return -1;
        }
        var c = NextCodePoint();
        if (_skipped != null && !_skipped.IsEmpty() && c >= 0)
        {
            _skipped.IncBeyond();
        }
        if (_numCpFwd > 0 && c >= 0)
        {
            --_numCpFwd;
        }
        return c;
    }

    private void BackwardNumSkipped(int n)
    {
        if (_skipped != null && !_skipped.IsEmpty())
        {
            n = _skipped.BackwardNumCodePoints(n);
        }
        BackwardNumCodePoints(n);
        if (_numCpFwd >= 0)
        {
            _numCpFwd += n;
        }
    }

    private uint NextCe32FromContraction(CollationData d, uint contractionCe32, int p,
        uint ce32, int c)
    {
        var lookAhead = 1;
        var sinceMatch = 1;
        var suffixes = new UCharsTrieMatcher(d.Contexts, p);
        if (_skipped != null && !_skipped.IsEmpty())
        {
            _skipped.SaveTrieState(suffixes);
        }
        var match = suffixes.FirstForCodePoint(c);
        for (;;)
        {
            int nextCp;
            if (UCharsTrieMatcher.HasValue(match))
            {
                ce32 = (uint)suffixes.GetValue();
                if (!UCharsTrieMatcher.HasNext(match) || (c = NextSkippedCodePoint()) < 0)
                {
                    return ce32;
                }
                if (_skipped != null && !_skipped.IsEmpty())
                {
                    _skipped.SaveTrieState(suffixes);
                }
                sinceMatch = 1;
            }
            else if (match == UCharsTrieMatcher.NoMatch || (nextCp = NextSkippedCodePoint()) < 0)
            {
                if ((contractionCe32 & Collator.ContractTrailingCcc) != 0
                    && ((contractionCe32 & Collator.ContractSingleCpNoMatch) == 0 || sinceMatch < lookAhead))
                {
                    if (sinceMatch > 1)
                    {
                        BackwardNumSkipped(sinceMatch);
                        c = NextSkippedCodePoint();
                        lookAhead -= sinceMatch - 1;
                        sinceMatch = 1;
                    }
                    if (d.GetFcd16(c) > 0xff)
                    {
                        return NextCe32FromDiscontiguousContraction(d, suffixes, ce32, lookAhead, c);
                    }
                }
                break;
            }
            else
            {
                c = nextCp;
                ++sinceMatch;
            }
            ++lookAhead;
            match = suffixes.NextForCodePoint(c);
        }
        BackwardNumSkipped(sinceMatch);
        return ce32;
    }

    private uint NextCe32FromDiscontiguousContraction(CollationData d, UCharsTrieMatcher suffixes,
        uint ce32, int lookAhead, int c)
    {
        var nextCp = NextSkippedCodePoint();
        if (nextCp < 0)
        {
            BackwardNumSkipped(1);
            return ce32;
        }
        ++lookAhead;
        var prevCc = (uint)(d.GetFcd16(c) & 0xff);
        var fcd16 = d.GetFcd16(nextCp);
        if (fcd16 <= 0xff)
        {
            BackwardNumSkipped(2);
            return ce32;
        }

        if (_skipped == null || _skipped.IsEmpty())
        {
            _skipped ??= new SkippedCombiningMarks();
            suffixes.Reset();
            if (lookAhead > 2)
            {
                BackwardNumCodePoints(lookAhead);
                suffixes.FirstForCodePoint(NextCodePoint());
                for (var i = 3; i < lookAhead; ++i)
                {
                    suffixes.NextForCodePoint(NextCodePoint());
                }
                ForwardNumCodePoints(2);
            }
            _skipped.SaveTrieState(suffixes);
        }
        else
        {
            _skipped.ResetToTrieState(suffixes);
        }

        _skipped.SetFirstSkipped(c);
        var sinceMatch = 2;
        c = nextCp;
        for (;;)
        {
            int match;
            if (prevCc < (uint)(fcd16 >> 8)
                && UCharsTrieMatcher.HasValue(match = suffixes.NextForCodePoint(c)))
            {
                ce32 = (uint)suffixes.GetValue();
                sinceMatch = 0;
                _skipped.RecordMatch();
                if (!UCharsTrieMatcher.HasNext(match))
                {
                    break;
                }
                _skipped.SaveTrieState(suffixes);
            }
            else
            {
                _skipped.Skip(c);
                _skipped.ResetToTrieState(suffixes);
                prevCc = (uint)(fcd16 & 0xff);
            }
            if ((c = NextSkippedCodePoint()) < 0)
            {
                break;
            }
            ++sinceMatch;
            fcd16 = d.GetFcd16(c);
            if (fcd16 <= 0xff)
            {
                break;
            }
        }
        BackwardNumSkipped(sinceMatch);
        var isTopDiscontiguous = _skipped.IsEmpty();
        _skipped.ReplaceMatch();
        if (isTopDiscontiguous && !_skipped.IsEmpty())
        {
            c = -1;
            for (;;)
            {
                AppendCEsFromCe32(d, c, ce32, true);
                if (!_skipped.HasNext())
                {
                    break;
                }
                c = _skipped.Next();
                ce32 = GetCe32(Data, c);
            }
            _skipped.Clear();
            return Collator.NoCe32;
        }
        return ce32;
    }

    private uint GetCe32(CollationData d, int c)
    {
        var ce32 = d.GetCe32(c);
        if (ce32 == Collator.FallbackCe32)
        {
            ce32 = d.Base!.GetCe32(c);
        }
        return ce32;
    }
}
