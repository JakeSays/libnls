using NlsDataGenerator.Normalization;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Collation;

// Parses LDML tailoring rules into reset/relation/setting callbacks, ported from ICU's
// CollationRuleParser (collationruleparser.cpp). It recognizes resets (&str, &[before n]str,
// &[position]), relations (<, <<, <<<, <<<<, =, and their starred forms with ranges), the
// prefix|str/extension context syntax, [setting value] options, [reorder …], [import langTag] (inlined
// via the importer), and comments. Reset/relation callbacks drive the builder sink; settings update
// CollationSettings. Errors throw CollationParseException (replacing ICU's UErrorCode threading).
internal sealed partial class CollationRuleParser
{
    private const int StrengthMask = 0xf;
    private const int StarredFlag = 0x10;
    private const int OffsetShift = 8;

    private const char PositionLead = (char)0xFFFE;
    private const int PositionBase = 0x2800;

    private const string Before = "[before";

    private readonly CollationData _baseData;
    private readonly BuildTimeNormalizer _normalizer;

    private CollationSettings _settings = null!;
    private CollationRuleSink _sink = null!;
    private ICollationImporter? _importer;

    private string _rules = "";
    private int _ruleIndex;

    public CollationRuleParser(CollationData baseData, BuildTimeNormalizer normalizer)
    {
        _baseData = baseData;
        _normalizer = normalizer;
    }

    public void Parse(string ruleString, CollationSettings settings, CollationRuleSink sink,
        ICollationImporter? importer)
    {
        _settings = settings;
        _sink = sink;
        _importer = importer;
        ParseRules(ruleString);
    }

    private void ParseRules(string ruleString)
    {
        _rules = ruleString;
        _ruleIndex = 0;
        while (_ruleIndex < _rules.Length)
        {
            var c = _rules[_ruleIndex];
            if (PatternWhiteSpace.IsWhiteSpace(c))
            {
                ++_ruleIndex;
                continue;
            }
            switch (c)
            {
                case '&':
                    ParseRuleChain();
                    break;
                case '[':
                    ParseSetting();
                    break;
                case '#':
                    // A comment runs to the end of the line.
                    _ruleIndex = SkipComment(_ruleIndex + 1);
                    break;
                case '@':
                    // Equivalent to [backwards 2].
                    _settings.SetFlag(CollationSettings.BackwardSecondary, Ucol.On);
                    ++_ruleIndex;
                    break;
                case '!':
                    // Old Thai/Lao reversal switch: accepted and ignored.
                    ++_ruleIndex;
                    break;
                default:
                    throw ParseError("expected a reset or setting or comment");
            }
        }
    }

    private void ParseRuleChain()
    {
        var resetStrength = ParseResetAndPosition();
        var isFirstRelation = true;
        for (;;)
        {
            var result = ParseRelationOperator();
            if (result < 0)
            {
                if (_ruleIndex < _rules.Length && _rules[_ruleIndex] == '#')
                {
                    _ruleIndex = SkipComment(_ruleIndex + 1);
                    continue;
                }
                if (isFirstRelation)
                {
                    throw ParseError("reset not followed by a relation");
                }
                return;
            }
            var strength = result & StrengthMask;
            if (resetStrength < Ucol.Identical)
            {
                // Reset-before chain: relations must not be stronger than the reset.
                if (isFirstRelation)
                {
                    if (strength != resetStrength)
                    {
                        throw ParseError("reset-before strength differs from its first relation");
                    }
                }
                else if (strength < resetStrength)
                {
                    throw ParseError("reset-before strength followed by a stronger relation");
                }
            }
            var i = _ruleIndex + (result >> OffsetShift);
            if ((result & StarredFlag) == 0)
            {
                ParseRelationStrings(strength, i);
            }
            else
            {
                ParseStarredCharacters(strength, i);
            }
            isFirstRelation = false;
        }
    }

    private int ParseResetAndPosition()
    {
        var i = SkipWhiteSpace(_ruleIndex + 1);
        int resetStrength;
        int j;
        if (Matches(i, Before)
            && (j = i + Before.Length) < _rules.Length
            && PatternWhiteSpace.IsWhiteSpace(_rules[j])
            && (j = SkipWhiteSpace(j + 1)) + 1 < _rules.Length
            && _rules[j] >= '1' && _rules[j] <= '3'
            && _rules[j + 1] == ']')
        {
            // &[before n] with n = 1, 2 or 3.
            resetStrength = Ucol.Primary + (_rules[j] - '1');
            i = SkipWhiteSpace(j + 2);
        }
        else
        {
            resetStrength = Ucol.Identical;
        }
        if (i >= _rules.Length)
        {
            throw ParseError("reset without position");
        }
        string str;
        if (_rules[i] == '[')
        {
            (i, str) = ParseSpecialPosition(i);
        }
        else
        {
            (i, str) = ParseTailoringString(i);
        }
        _sink.AddReset(resetStrength, str);
        _ruleIndex = i;
        return resetStrength;
    }

    private int ParseRelationOperator()
    {
        _ruleIndex = SkipWhiteSpace(_ruleIndex);
        if (_ruleIndex >= _rules.Length)
        {
            return Ucol.Default;
        }
        int strength;
        var i = _ruleIndex;
        var c = _rules[i++];
        switch (c)
        {
            case '<':
                if (i < _rules.Length && _rules[i] == '<')
                {
                    ++i;
                    if (i < _rules.Length && _rules[i] == '<')
                    {
                        ++i;
                        if (i < _rules.Length && _rules[i] == '<')
                        {
                            ++i;
                            strength = Ucol.Quaternary;
                        }
                        else
                        {
                            strength = Ucol.Tertiary;
                        }
                    }
                    else
                    {
                        strength = Ucol.Secondary;
                    }
                }
                else
                {
                    strength = Ucol.Primary;
                }
                if (i < _rules.Length && _rules[i] == '*')
                {
                    ++i;
                    strength |= StarredFlag;
                }
                break;
            case ';':
                strength = Ucol.Secondary;
                break;
            case ',':
                strength = Ucol.Tertiary;
                break;
            case '=':
                strength = Ucol.Identical;
                if (i < _rules.Length && _rules[i] == '*')
                {
                    ++i;
                    strength |= StarredFlag;
                }
                break;
            default:
                return Ucol.Default;
        }
        return ((i - _ruleIndex) << OffsetShift) | strength;
    }

    private void ParseRelationStrings(int strength, int i)
    {
        // prefix | str / extension, where prefix and extension are optional.
        string prefix = "";
        string extension = "";
        var (afterStr, str) = ParseTailoringString(i);
        i = afterStr;
        var next = i < _rules.Length ? _rules[i] : '\0';
        if (next == '|')
        {
            prefix = str;
            (i, str) = ParseTailoringString(i + 1);
            next = i < _rules.Length ? _rules[i] : '\0';
        }
        if (next == '/')
        {
            (i, extension) = ParseTailoringString(i + 1);
        }
        if (prefix.Length != 0)
        {
            var prefix0 = char.ConvertToUtf32(prefix, 0);
            var c = char.ConvertToUtf32(str, 0);
            if (!_normalizer.HasCompBoundaryBefore(prefix0) || !_normalizer.HasCompBoundaryBefore(c))
            {
                throw ParseError("in 'prefix|str', prefix and str must each start with an NFC boundary");
            }
        }
        _sink.AddRelation(strength, prefix, str, extension);
        _ruleIndex = i;
    }

    private void ParseStarredCharacters(int strength, int i)
    {
        var (afterRaw, raw) = ParseString(SkipWhiteSpace(i));
        i = afterRaw;
        if (raw.Length == 0)
        {
            throw ParseError("missing starred-relation string");
        }
        var prev = -1;
        var j = 0;
        for (;;)
        {
            while (j < raw.Length)
            {
                var c = char.ConvertToUtf32(raw, j);
                if (!_normalizer.IsInert(c))
                {
                    throw ParseError("starred-relation string is not all NFD-inert");
                }
                _sink.AddRelation(strength, "", char.ConvertFromUtf32(c), "");
                j += c > 0xFFFF ? 2 : 1;
                prev = c;
            }
            if (i >= _rules.Length || _rules[i] != '-')
            {
                break;
            }
            if (prev < 0)
            {
                throw ParseError("range without start in starred-relation string");
            }
            (i, raw) = ParseString(i + 1);
            if (raw.Length == 0)
            {
                throw ParseError("range without end in starred-relation string");
            }
            var rangeEnd = char.ConvertToUtf32(raw, 0);
            if (rangeEnd < prev)
            {
                throw ParseError("range start greater than end in starred-relation string");
            }
            while (++prev <= rangeEnd)
            {
                if (!_normalizer.IsInert(prev))
                {
                    throw ParseError("starred-relation string range is not all NFD-inert");
                }
                if (prev >= 0xD800 && prev <= 0xDFFF)
                {
                    throw ParseError("starred-relation string range contains a surrogate");
                }
                if (prev >= 0xFFFD && prev <= 0xFFFF)
                {
                    throw ParseError("starred-relation string range contains U+FFFD, U+FFFE or U+FFFF");
                }
                _sink.AddRelation(strength, "", char.ConvertFromUtf32(prev), "");
            }
            prev = -1;
            j = rangeEnd > 0xFFFF ? 2 : 1;
        }
        _ruleIndex = SkipWhiteSpace(i);
    }

    private (int, string) ParseTailoringString(int i)
    {
        var (afterRaw, raw) = ParseString(SkipWhiteSpace(i));
        if (raw.Length == 0)
        {
            throw ParseError("missing relation string");
        }
        return (SkipWhiteSpace(afterRaw), raw);
    }

    private bool Matches(int i, string text)
    {
        return i + text.Length <= _rules.Length
            && string.CompareOrdinal(_rules, i, text, 0, text.Length) == 0;
    }
}
