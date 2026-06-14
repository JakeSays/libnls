using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Case;

// Builds ucase.icu from the UCD case data. This part ports casepropsbuilder's setProps: the
// per-code-point 16-bit value (type, simple-mapping delta or exception flag, dot/ignorable bits),
// collecting the exception props, the case-sensitive closure seed, the max full-mapping length,
// and the multi-character fold mappings for the unfold table. makeException / addClosure / the
// container assembly follow.
internal sealed partial class CaseGenerator
{
    private const uint TypeNone = 0;
    private const uint TypeLower = 1;
    private const uint TypeUpper = 2;
    private const uint TypeTitle = 3;
    private const uint Ignorable = 4;
    private const uint Exception = 8;
    private const uint Sensitive = 0x10;
    private const uint SoftDottedBit = 0x20;
    private const uint AboveBit = 0x40;
    private const uint OtherAccentBit = 0x60;
    private const int DeltaShift = 7;
    private const uint DeltaMask = 0xFF80;
    private const int MaxDelta = 0xFF;
    private const int MinDelta = -MaxDelta - 1;

    // During the build the trie holds 32-bit values; the temporary exception index lives above the
    // 16-bit value and is replaced with the final exception index by makeException.
    private const int TempExceptionShift = 20;

    private readonly CaseInputs _inputs;
    private readonly Trie2Builder _trie = new(0, 0);
    private readonly List<ExceptionProps> _exceptions = [];
    private readonly HashSet<int> _caseSensitive = [];
    private int _maxFullLength = 2;

    public CaseGenerator(CaseInputs inputs)
    {
        _inputs = inputs;
    }

    public void ComputeMainValues()
    {
        foreach (var codePoint in _inputs.AssignedCodePoints)
        {
            ComputeValue(codePoint);
        }
    }

    private void ComputeValue(int codePoint)
    {
        var type = TypeNone;
        if (_inputs.IsLowercase(codePoint))
        {
            type = TypeLower;
        }
        else if (_inputs.IsUppercase(codePoint))
        {
            type = TypeUpper;
        }
        else if (_inputs.GeneralCategory(codePoint) == "Lt")
        {
            type = TypeTitle;
        }

        var value = type;
        var delta = 0;
        var noDelta = false;

        var simpleUpper = _inputs.SimpleUpper(codePoint);
        var simpleLower = _inputs.SimpleLower(codePoint);
        var simpleTitle = _inputs.SimpleTitle(codePoint);
        var simpleFold = _inputs.SimpleFold(codePoint);
        var hasMapping = false;

        // Uppercase mapping as a delta only if the character is lowercase.
        if (simpleUpper >= 0)
        {
            hasMapping = true;
            if (type == TypeLower)
            {
                delta = simpleUpper - codePoint;
            }
            else
            {
                noDelta = true;
                value |= Exception;
            }
        }
        // Lowercase mapping as a delta only if the character is uppercase or titlecase.
        if (simpleLower >= 0)
        {
            hasMapping = true;
            if (type >= TypeUpper)
            {
                delta = simpleLower - codePoint;
            }
            else
            {
                noDelta = true;
                value |= Exception;
            }
        }
        if (simpleTitle >= 0)
        {
            hasMapping = true;
        }
        if (simpleUpper != simpleTitle)
        {
            noDelta = true;
            value |= Exception;
        }

        // Simple case folding falls back to simple lowercasing; store separately if they differ.
        if (simpleFold >= 0 && simpleFold != simpleLower)
        {
            hasMapping = true;
            noDelta = true;
            value |= Exception;
        }

        // No case folding but a lowercase mapping (e.g. Cherokee uppercase syllables).
        var hasNoSimpleCaseFolding = false;
        if (simpleFold < 0 && simpleLower >= 0)
        {
            hasNoSimpleCaseFolding = true;
            value |= Exception;
        }

        if (noDelta)
        {
            delta = 0;
        }
        else if (delta < MinDelta || delta > MaxDelta)
        {
            // Delta too big for the main data word; it goes into an exception slot.
            value |= Exception;
        }

        var fullLower = _inputs.FullLower(codePoint);
        var fullUpper = _inputs.FullUpper(codePoint);
        var fullTitle = _inputs.FullTitle(codePoint);
        var fullFold = _inputs.FullFold(codePoint);
        var turkic = _inputs.TurkicFold(codePoint);
        var hasConditional = _inputs.HasConditional(codePoint);

        if (fullLower.Length > 0 || fullUpper.Length > 0 || fullTitle.Length > 0 || hasConditional)
        {
            hasMapping = true;
            value |= Exception;
        }
        var fullFoldDiffers = fullFold.Length > 0 && !(fullFold.Length == 1 && fullFold[0] == simpleFold);
        if (fullFoldDiffers || turkic.Length > 0)
        {
            hasMapping = true;
            value |= Exception;
        }

        if (_inputs.IsSoftDotted(codePoint))
        {
            value |= SoftDottedBit;
        }
        var combiningClass = _inputs.CombiningClass(codePoint);
        if (combiningClass != 0)
        {
            value |= combiningClass == 230 ? AboveBit : OtherAccentBit;
        }
        if (_inputs.IsCaseIgnorable(codePoint))
        {
            value |= Ignorable;
        }

        if ((value & Exception) != 0)
        {
            var exceptionIndex = _exceptions.Count;
            _exceptions.Add(new ExceptionProps
            {
                CodePoint = codePoint,
                SimpleLower = simpleLower,
                SimpleUpper = simpleUpper,
                SimpleTitle = simpleTitle,
                SimpleFold = simpleFold,
                Delta = delta,
                FullLower = fullLower,
                FullUpper = fullUpper,
                FullTitle = fullTitle,
                FullFold = fullFold,
                Turkic = turkic,
                // U+0587 ligature ech-yiwn has language-specific upper/title mappings (ICU-13416).
                HasConditional = hasConditional || codePoint == 0x0587,
                HasTurkic = turkic.Length > 0,
                HasNoSimpleCaseFolding = hasNoSimpleCaseFolding,
            });
            value |= (uint)exceptionIndex << TempExceptionShift;
        }
        else
        {
            value |= ((uint)delta << DeltaShift) & DeltaMask;
        }

        // ICU's setProps returns early for code points with no case-relevant property, never
        // touching the trie; a zero value means exactly that. Setting it anyway would allocate
        // spurious blocks and change the compaction, so skip it.
        if (value != 0)
        {
            _trie.Set(codePoint, value);
        }

        if (hasMapping)
        {
            _caseSensitive.Add(codePoint);
            if (simpleFold >= 0)
            {
                _caseSensitive.Add(simpleFold);
            }
            if (simpleLower >= 0)
            {
                _caseSensitive.Add(simpleLower);
            }
            if (simpleUpper >= 0)
            {
                _caseSensitive.Add(simpleUpper);
            }
            if (simpleTitle >= 0)
            {
                _caseSensitive.Add(simpleTitle);
            }
            AddAll(_caseSensitive, fullFold);
            AddAll(_caseSensitive, fullLower);
            AddAll(_caseSensitive, fullUpper);
            AddAll(_caseSensitive, fullTitle);

            _maxFullLength = Math.Max(_maxFullLength, Utf16Length(fullFold));
            _maxFullLength = Math.Max(_maxFullLength, Utf16Length(fullLower));
            _maxFullLength = Math.Max(_maxFullLength, Utf16Length(fullUpper));
            _maxFullLength = Math.Max(_maxFullLength, Utf16Length(fullTitle));
        }

        // A full folding of more than one code point feeds the unfold (reverse-folding) table.
        if (fullFold.Length > 1)
        {
            AddUnfolding(codePoint, fullFold);
        }
    }

    private static void AddAll(HashSet<int> set, int[] codePoints)
    {
        foreach (var codePoint in codePoints)
        {
            set.Add(codePoint);
        }
    }

    private static int Utf16Length(int[] codePoints)
    {
        var length = 0;
        foreach (var codePoint in codePoints)
        {
            length += codePoint <= 0xFFFF ? 1 : 2;
        }
        return length;
    }
}
