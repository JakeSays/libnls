namespace NlsDataGenerator.Collation;

// A CollationIterator over a UTF-16 string with explicit bounds, ported from ICU's
// UTF16CollationIterator (utf16collationiterator.cpp). The tailoring builder's setCaseBits runs one of
// these over the base data to read a string's root CEs. handleNextCE32 looks up one code unit at a
// time; surrogates are resolved through the LEAD_SURROGATE_TAG path. No NUL-termination handling is
// needed since the builder always supplies an explicit limit.
internal sealed class Utf16CollationIterator : CollationIterator
{
    private readonly string _text;
    private readonly int _start;
    private readonly int _limit;
    private int _pos;

    public Utf16CollationIterator(CollationData data, bool numeric, string text, int start, int limit)
        : base(data, numeric)
    {
        _text = text;
        _start = start;
        _limit = limit;
        _pos = start;
    }

    public override int GetOffset()
    {
        return _pos - _start;
    }

    public override void ResetToOffset(int newOffset)
    {
        ClearCEs();
        _pos = _start + newOffset;
    }

    protected override uint HandleNextCE32(out int c)
    {
        if (_pos == _limit)
        {
            c = -1;
            return Collator.FallbackCe32;
        }
        c = _text[_pos++];
        return Data.GetCe32ForU16SingleLead(c);
    }

    protected override char HandleGetTrailSurrogate()
    {
        if (_pos == _limit)
        {
            return '\0';
        }
        var trail = _text[_pos];
        if (char.IsLowSurrogate(trail))
        {
            ++_pos;
        }
        return trail;
    }

    public override int NextCodePoint()
    {
        if (_pos == _limit)
        {
            return -1;
        }
        int c = _text[_pos++];
        if (char.IsHighSurrogate((char)c) && _pos != _limit && char.IsLowSurrogate(_text[_pos]))
        {
            var trail = _text[_pos++];
            return ((c - 0xD800) << 10) + (trail - 0xDC00) + 0x10000;
        }
        return c;
    }

    public override int PreviousCodePoint()
    {
        if (_pos == _start)
        {
            return -1;
        }
        int c = _text[--_pos];
        if (char.IsLowSurrogate((char)c) && _pos != _start && char.IsHighSurrogate(_text[_pos - 1]))
        {
            var lead = _text[--_pos];
            return ((lead - 0xD800) << 10) + (c - 0xDC00) + 0x10000;
        }
        return c;
    }

    protected override void ForwardNumCodePoints(int num)
    {
        while (num > 0 && _pos != _limit)
        {
            var c = _text[_pos++];
            --num;
            if (char.IsHighSurrogate(c) && _pos != _limit && char.IsLowSurrogate(_text[_pos]))
            {
                ++_pos;
            }
        }
    }

    protected override void BackwardNumCodePoints(int num)
    {
        while (num > 0 && _pos != _start)
        {
            var c = _text[--_pos];
            --num;
            if (char.IsLowSurrogate(c) && _pos != _start && char.IsHighSurrogate(_text[_pos - 1]))
            {
                --_pos;
            }
        }
    }
}
