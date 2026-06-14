namespace NlsDataGenerator.Case;

// Ports casepropsbuilder's makeException / makeExceptions: encodes each ExceptionProps into the
// uint16 exceptions[] array (a main word, optional slot values, the full mapping strings, and the
// closure), sharing entries where ICU does, then rewrites the trie word for each exception code
// point with the real exception index.
internal sealed partial class CaseGenerator
{
    // Optional-slot presence bits in the main exception word (U_MASK(slot)).
    private const uint ExcLower = 1 << 0;
    private const uint ExcFold = 1 << 1;
    private const uint ExcUpper = 1 << 2;
    private const uint ExcTitle = 1 << 3;
    private const uint ExcDelta = 1 << 4;
    private const uint ExcClosure = 1 << 6;
    private const uint ExcFullMappings = 1 << 7;

    private const uint ExcDoubleSlots = 0x100;
    private const uint ExcNoSimpleCaseFolding = 0x200;
    private const uint ExcDeltaIsNegative = 0x400;
    private const uint ExcConditionalSpecial = 0x4000;
    private const uint ExcConditionalFold = 0x8000;
    private const int ExcDotShift = 7;
    private const uint DotAndSensitiveMask = 0x70;
    private const int ClosureMaxLength = 0xF;
    private const int MaxExceptions = 0x1000;

    private const uint UgencaseExcMask = 0xFFF00000;
    private const uint UcaseExcMask = 0xFFF0;
    private const int UcaseExcShift = 4;

    private readonly List<ushort> _exceptionWords = [];

    public IReadOnlyList<ushort> ExceptionWords
    {
        get
        {
            return _exceptionWords;
        }
    }

    private void MakeExceptions()
    {
        // ICU iterates code points 0..10FFFF; the closure pass appends exceptions out of order, so
        // sort by code point to encode them (and lay out the exceptions array) identically.
        foreach (var props in _exceptions.OrderBy(static e => e.CodePoint))
        {
            var codePoint = props.CodePoint;
            var value = _trie.Get(codePoint);
            var exceptionIndex = MakeException(codePoint, value, props);
            value = (value & ~(UgencaseExcMask | UcaseExcMask)) | ((uint)exceptionIndex << UcaseExcShift);
            _trie.Set(codePoint, value);
        }
    }

    private int MakeException(int codePoint, uint value, ExceptionProps props)
    {
        if (_exceptionWords.Count >= MaxExceptions)
        {
            throw new InvalidOperationException("too many ucase exception words");
        }

        // The soft-dotted and case-sensitive bits move into the exception word.
        var excWord = (value & DotAndSensitiveMask) << ExcDotShift;

        var fullLower = props.FullLower;
        var fullUpper = props.FullUpper;
        var fullTitle = props.FullTitle;
        var fullFold = props.FullFold;

        if (props.HasConditional)
        {
            excWord |= ExcConditionalSpecial;
            fullLower = [];
            fullUpper = [];
            fullTitle = [];
        }
        if (props.HasTurkic)
        {
            excWord |= ExcConditionalFold;
            fullFold = [];
        }
        if (props.HasNoSimpleCaseFolding)
        {
            excWord |= ExcNoSimpleCaseFolding;
        }

        // Drop full mappings that are identical to the simple ones.
        if (FullEqualsSimple(fullLower, props.SimpleLower, codePoint))
        {
            fullLower = [];
        }
        if (FullEqualsSimple(fullUpper, props.SimpleUpper, codePoint))
        {
            fullUpper = [];
        }
        if (FullEqualsSimple(fullTitle, props.SimpleTitle, codePoint))
        {
            fullTitle = [];
        }
        if (FullEqualsSimple(fullFold, props.SimpleFold, codePoint))
        {
            fullFold = [];
        }

        var slots = new uint[8];
        var slotBits = 0u;
        var count = 0;

        if (props.Delta != 0)
        {
            var delta = props.Delta;
            if (delta < 0)
            {
                excWord |= ExcDeltaIsNegative;
                delta = -delta;
            }
            slots[count] = (uint)delta;
            slotBits |= slots[count];
            count++;
            excWord |= ExcDelta;
        }
        else
        {
            if (props.SimpleLower >= 0)
            {
                slots[count] = (uint)props.SimpleLower;
                slotBits |= slots[count];
                count++;
                excWord |= ExcLower;
            }
            if (props.SimpleFold >= 0 && (props.SimpleLower >= 0 ? props.SimpleFold != props.SimpleLower : props.SimpleFold != codePoint))
            {
                slots[count] = (uint)props.SimpleFold;
                slotBits |= slots[count];
                count++;
                excWord |= ExcFold;
            }
            if (props.SimpleUpper >= 0)
            {
                slots[count] = (uint)props.SimpleUpper;
                slotBits |= slots[count];
                count++;
                excWord |= ExcUpper;
            }
            if (props.SimpleUpper != props.SimpleTitle)
            {
                slots[count] = props.SimpleTitle >= 0 ? (uint)props.SimpleTitle : (uint)codePoint;
                slotBits |= slots[count];
                count++;
                excWord |= ExcTitle;
            }
        }

        if (props.Closure.Count > 0)
        {
            var length = Utf16Length([.. props.Closure]);
            if (length > ClosureMaxLength)
            {
                throw new InvalidOperationException($"case closure for U+{codePoint:X4} exceeds {ClosureMaxLength}");
            }
            slots[count] = (uint)length;
            slotBits |= slots[count];
            count++;
            excWord |= ExcClosure;
        }

        var fullLengths = Utf16Length(fullLower) | (Utf16Length(fullFold) << 4) | (Utf16Length(fullUpper) << 8) | (Utf16Length(fullTitle) << 12);
        if (fullLengths != 0)
        {
            slots[count] = (uint)fullLengths;
            slotBits |= slots[count];
            count++;
            excWord |= ExcFullMappings;
        }

        if (count == 0)
        {
            // No optional slots: try to share a lone exception word.
            var shared = IndexOfWord((ushort)excWord);
            if (shared >= 0)
            {
                return shared;
            }
            var index = _exceptionWords.Count;
            _exceptionWords.Add((ushort)excWord);
            return index;
        }

        var entry = new List<ushort> { 0 };
        if (slotBits <= 0xFFFF)
        {
            for (var i = 0; i < count; i++)
            {
                entry.Add((ushort)slots[i]);
            }
        }
        else
        {
            excWord |= ExcDoubleSlots;
            for (var i = 0; i < count; i++)
            {
                entry.Add((ushort)(slots[i] >> 16));
                entry.Add((ushort)slots[i]);
            }
        }

        AppendUtf16(entry, fullLower);
        AppendUtf16(entry, fullFold);
        AppendUtf16(entry, fullUpper);
        AppendUtf16(entry, fullTitle);
        AppendUtf16(entry, [.. props.Closure]);
        entry[0] = (ushort)excWord;

        if (count == 1 && props.Delta != 0)
        {
            var shared = IndexOfSequence(entry);
            if (shared >= 0)
            {
                return shared;
            }
        }

        var appendIndex = _exceptionWords.Count;
        _exceptionWords.AddRange(entry);
        return appendIndex;
    }

    // The full mapping equals the simple one when it is the single code point `simple` (or the
    // character itself when there is no simple mapping).
    private static bool FullEqualsSimple(int[] full, int simple, int codePoint)
    {
        var target = simple > 0 ? simple : codePoint;
        return full.Length == 1 && full[0] == target;
    }

    private static void AppendUtf16(List<ushort> entry, int[] codePoints)
    {
        foreach (var codePoint in codePoints)
        {
            if (codePoint <= 0xFFFF)
            {
                entry.Add((ushort)codePoint);
            }
            else
            {
                var scalar = codePoint - 0x10000;
                entry.Add((ushort)(0xD800 + (scalar >> 10)));
                entry.Add((ushort)(0xDC00 + (scalar & 0x3FF)));
            }
        }
    }

    private int IndexOfWord(ushort word)
    {
        for (var i = 0; i < _exceptionWords.Count; i++)
        {
            if (_exceptionWords[i] == word)
            {
                return i;
            }
        }
        return -1;
    }

    private int IndexOfSequence(List<ushort> sequence)
    {
        var limit = _exceptionWords.Count - sequence.Count;
        for (var start = 0; start <= limit; start++)
        {
            var match = true;
            for (var i = 0; i < sequence.Count; i++)
            {
                if (_exceptionWords[start + i] != sequence[i])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return start;
            }
        }
        return -1;
    }
}
