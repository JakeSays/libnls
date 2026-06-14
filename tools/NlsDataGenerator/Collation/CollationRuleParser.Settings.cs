using System.Text;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Collation;

// The string/position/setting half of CollationRuleParser: low-level string scanning (quoting and
// escapes), special reset positions ([first/last X], [top], [variable top]), the [setting value] and
// [reorder …] options, [import langTag] inlining, UnicodeSet option collection, and the lexer
// helpers (readWords, skipComment, skipWhiteSpace, isSyntaxChar) plus error reporting.
internal sealed partial class CollationRuleParser
{
    private static readonly string[] PositionNames =
    [
        "first tertiary ignorable",
        "last tertiary ignorable",
        "first secondary ignorable",
        "last secondary ignorable",
        "first primary ignorable",
        "last primary ignorable",
        "first variable",
        "last variable",
        "first regular",
        "last regular",
        "first implicit",
        "last implicit",
        "first trailing",
        "last trailing",
    ];

    private static readonly string[] SpecialReorderCodes =
        ["space", "punct", "symbol", "currency", "digit"];

    private (int, string) ParseString(int i)
    {
        var raw = new StringBuilder();
        while (i < _rules.Length)
        {
            int c = _rules[i++];
            if (IsSyntaxChar(c))
            {
                if (c == '\'')
                {
                    if (i < _rules.Length && _rules[i] == '\'')
                    {
                        // Double apostrophe encodes a single one.
                        raw.Append('\'');
                        ++i;
                        continue;
                    }
                    // Quoted literal text until the next single apostrophe.
                    for (;;)
                    {
                        if (i == _rules.Length)
                        {
                            throw ParseError("quoted literal text missing terminating apostrophe");
                        }
                        c = _rules[i++];
                        if (c == '\'')
                        {
                            if (i < _rules.Length && _rules[i] == '\'')
                            {
                                ++i;
                            }
                            else
                            {
                                break;
                            }
                        }
                        raw.Append((char)c);
                    }
                }
                else if (c == '\\')
                {
                    if (i == _rules.Length)
                    {
                        throw ParseError("backslash escape at the end of the rule string");
                    }
                    var cp = Char32At(i);
                    raw.Append(char.ConvertFromUtf32(cp));
                    i += cp > 0xFFFF ? 2 : 1;
                }
                else
                {
                    // Any other syntax character terminates the string.
                    --i;
                    break;
                }
            }
            else if (PatternWhiteSpace.IsWhiteSpace(c))
            {
                --i;
                break;
            }
            else
            {
                raw.Append((char)c);
            }
        }

        var result = raw.ToString();
        var j = 0;
        while (j < result.Length)
        {
            var c = Char32OfString(result, j);
            if (c >= 0xD800 && c <= 0xDFFF)
            {
                throw ParseError("string contains an unpaired surrogate");
            }
            if (c >= 0xFFFD && c <= 0xFFFF)
            {
                throw ParseError("string contains U+FFFD, U+FFFE or U+FFFF");
            }
            j += c > 0xFFFF ? 2 : 1;
        }
        return (i, result);
    }

    private (int, string) ParseSpecialPosition(int i)
    {
        var (j, raw) = ReadWords(i + 1);
        if (j > i && j < _rules.Length && _rules[j] == ']' && raw.Length != 0)
        {
            ++j;
            for (var pos = 0; pos < PositionNames.Length; ++pos)
            {
                if (raw == PositionNames[pos])
                {
                    return (j, PositionString(pos));
                }
            }
            if (raw == "top")
            {
                return (j, PositionString((int)ResetPosition.LastRegular));
            }
            if (raw == "variable top")
            {
                return (j, PositionString((int)ResetPosition.LastVariable));
            }
        }
        throw ParseError("not a valid special reset position");
    }

    private static string PositionString(int pos)
    {
        return new string([PositionLead, (char)(PositionBase + pos)]);
    }

    private void ParseSetting()
    {
        var (j, raw) = ReadWords(_ruleIndex + 1);
        if (j <= _ruleIndex || raw.Length == 0)
        {
            throw ParseError("expected a setting/option at '['");
        }
        if (_rules[j] == ']')
        {
            ++j;
            if (raw.StartsWith("reorder", StringComparison.Ordinal)
                && (raw.Length == 7 || raw[7] == ' '))
            {
                ParseReordering(raw);
                _ruleIndex = j;
                return;
            }
            if (raw == "backwards 2")
            {
                _settings.SetFlag(CollationSettings.BackwardSecondary, Ucol.On);
                _ruleIndex = j;
                return;
            }
            var value = "";
            var valueIndex = raw.LastIndexOf(' ');
            if (valueIndex >= 0)
            {
                value = raw.Substring(valueIndex + 1);
                raw = raw.Substring(0, valueIndex);
            }
            if (raw == "strength" && value.Length == 1)
            {
                var strength = Ucol.Default;
                var c = value[0];
                if (c >= '1' && c <= '4')
                {
                    strength = Ucol.Primary + (c - '1');
                }
                else if (c == 'I')
                {
                    strength = Ucol.Identical;
                }
                if (strength != Ucol.Default)
                {
                    _settings.SetStrength(strength);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "alternate")
            {
                var alternate = Ucol.Default;
                if (value == "non-ignorable")
                {
                    alternate = Ucol.NonIgnorable;
                }
                else if (value == "shifted")
                {
                    alternate = Ucol.Shifted;
                }
                if (alternate != Ucol.Default)
                {
                    _settings.SetAlternateHandling(alternate);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "maxVariable")
            {
                var maxVariable = Ucol.Default;
                if (value == "space")
                {
                    maxVariable = CollationSettings.MaxVarSpace;
                }
                else if (value == "punct")
                {
                    maxVariable = CollationSettings.MaxVarPunct;
                }
                else if (value == "symbol")
                {
                    maxVariable = CollationSettings.MaxVarSymbol;
                }
                else if (value == "currency")
                {
                    maxVariable = CollationSettings.MaxVarCurrency;
                }
                if (maxVariable != Ucol.Default)
                {
                    _settings.SetMaxVariable(maxVariable);
                    _settings.VariableTop = _baseData.GetLastPrimaryForGroup(
                        Ucol.ReorderCodeFirst + maxVariable);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "caseFirst")
            {
                var caseFirst = Ucol.Default;
                if (value == "off")
                {
                    caseFirst = Ucol.Off;
                }
                else if (value == "lower")
                {
                    caseFirst = Ucol.LowerFirst;
                }
                else if (value == "upper")
                {
                    caseFirst = Ucol.UpperFirst;
                }
                if (caseFirst != Ucol.Default)
                {
                    _settings.SetCaseFirst(caseFirst);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "caseLevel")
            {
                var on = GetOnOffValue(value);
                if (on != Ucol.Default)
                {
                    _settings.SetFlag(CollationSettings.CaseLevel, on);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "normalization")
            {
                var on = GetOnOffValue(value);
                if (on != Ucol.Default)
                {
                    _settings.SetFlag(CollationSettings.CheckFcd, on);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "numericOrdering")
            {
                var on = GetOnOffValue(value);
                if (on != Ucol.Default)
                {
                    _settings.SetFlag(CollationSettings.Numeric, on);
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "hiraganaQ")
            {
                var on = GetOnOffValue(value);
                if (on != Ucol.Default)
                {
                    if (on == Ucol.On)
                    {
                        throw ParseError("[hiraganaQ on] is not supported");
                    }
                    _ruleIndex = j;
                    return;
                }
            }
            else if (raw == "import")
            {
                if (_importer is null)
                {
                    throw ParseError("[import langTag] is not supported");
                }
                var (localeId, collationType) = CollationImportTag.Parse(value);
                var importedRules = _importer.GetRules(localeId, collationType);
                var outerRules = _rules;
                ParseRules(importedRules);
                _rules = outerRules;
                _ruleIndex = j;
                return;
            }
        }
        else if (_rules[j] == '[')
        {
            var set = new UnicodeSet();
            j = ParseUnicodeSet(j, set);
            if (raw == "optimize")
            {
                _sink.Optimize(set);
                _ruleIndex = j;
                return;
            }
            if (raw == "suppressContractions")
            {
                _sink.SuppressContractions(set);
                _ruleIndex = j;
                return;
            }
        }
        throw ParseError("not a valid setting/option");
    }

    private void ParseReordering(string raw)
    {
        var i = 7; // after "reorder"
        if (i == raw.Length)
        {
            _settings.ResetReordering();
            return;
        }
        var codes = new List<int>();
        while (i < raw.Length)
        {
            ++i; // skip the word-separating space
            var limit = raw.IndexOf(' ', i);
            if (limit < 0)
            {
                limit = raw.Length;
            }
            var word = raw.Substring(i, limit - i);
            var code = GetReorderCode(word);
            if (code < 0)
            {
                throw ParseError("unknown script or reorder code");
            }
            codes.Add(code);
            i = limit;
        }
        _settings.SetReordering(_baseData, [.. codes]);
    }

    // Resolves a [reorder …] word: a special group (space/punct/symbol/currency/digit), a script ISO
    // code (Latn, Cyrl, …, via the base data's name table), or "others" (= USCRIPT_UNKNOWN). Ported
    // from CollationRuleParser::getReorderCode.
    private int GetReorderCode(string word)
    {
        for (var i = 0; i < SpecialReorderCodes.Length; ++i)
        {
            if (string.Equals(word, SpecialReorderCodes[i], StringComparison.OrdinalIgnoreCase))
            {
                return Ucol.ReorderCodeFirst + i;
            }
        }
        if (_baseData.ScriptCodeByIsoName.TryGetValue(word, out var script))
        {
            return script;
        }
        if (string.Equals(word, "others", StringComparison.OrdinalIgnoreCase))
        {
            return Ucol.ReorderCodeOthers;
        }
        return -1;
    }

    private static int GetOnOffValue(string s)
    {
        if (s == "on")
        {
            return Ucol.On;
        }
        if (s == "off")
        {
            return Ucol.Off;
        }
        return Ucol.Default;
    }

    private int ParseUnicodeSet(int i, UnicodeSet set)
    {
        // Collect a UnicodeSet pattern between a balanced pair of [brackets], then require the
        // option-terminating ']' of the enclosing [optimize [set]] / [suppressContractions [set]].
        var level = 0;
        var j = i;
        for (;;)
        {
            if (j == _rules.Length)
            {
                throw ParseError("unbalanced UnicodeSet pattern brackets");
            }
            var c = _rules[j++];
            if (c == '[')
            {
                ++level;
            }
            else if (c == ']')
            {
                if (--level == 0)
                {
                    break;
                }
            }
        }
        set.ApplyPattern(_rules.Substring(i, j - i));
        j = SkipWhiteSpace(j);
        if (j == _rules.Length || _rules[j] != ']')
        {
            throw ParseError("missing option-terminating ']' after UnicodeSet pattern");
        }
        return ++j;
    }

    private (int, string) ReadWords(int i)
    {
        var raw = new StringBuilder();
        i = SkipWhiteSpace(i);
        for (;;)
        {
            if (i >= _rules.Length)
            {
                return (0, raw.ToString());
            }
            var c = _rules[i];
            if (IsSyntaxChar(c) && c != '-' && c != '_')
            {
                if (raw.Length == 0)
                {
                    return (i, "");
                }
                if (raw[^1] == ' ')
                {
                    raw.Length -= 1;
                }
                return (i, raw.ToString());
            }
            if (PatternWhiteSpace.IsWhiteSpace(c))
            {
                raw.Append(' ');
                i = SkipWhiteSpace(i + 1);
            }
            else
            {
                raw.Append(c);
                ++i;
            }
        }
    }

    private int SkipComment(int i)
    {
        while (i < _rules.Length)
        {
            int c = _rules[i++];
            // Stop past a newline function: LF, FF, CR, NEL, LS or PS.
            if (c == 0xa || c == 0xc || c == 0xd || c == 0x85 || c == 0x2028 || c == 0x2029)
            {
                break;
            }
        }
        return i;
    }

    private int SkipWhiteSpace(int i)
    {
        while (i < _rules.Length && PatternWhiteSpace.IsWhiteSpace(_rules[i]))
        {
            ++i;
        }
        return i;
    }

    private static bool IsSyntaxChar(int c)
    {
        return c >= 0x21 && c <= 0x7e
            && (c <= 0x2f || (c >= 0x3a && c <= 0x40) || (c >= 0x5b && c <= 0x60) || c >= 0x7b);
    }

    private int Char32At(int i)
    {
        var c = _rules[i];
        if (char.IsHighSurrogate(c) && i + 1 < _rules.Length && char.IsLowSurrogate(_rules[i + 1]))
        {
            return char.ConvertToUtf32(c, _rules[i + 1]);
        }
        return c;
    }

    private static int Char32OfString(string s, int i)
    {
        var c = s[i];
        if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            return char.ConvertToUtf32(c, s[i + 1]);
        }
        return c;
    }

    private CollationParseException ParseError(string reason)
    {
        const int contextLength = 16;
        var start = _ruleIndex - (contextLength - 1);
        if (start < 0)
        {
            start = 0;
        }
        else if (start > 0 && char.IsLowSurrogate(_rules[start]))
        {
            ++start;
        }
        var pre = _rules.Substring(start, _ruleIndex - start);
        var postLength = _rules.Length - _ruleIndex;
        if (postLength >= contextLength)
        {
            postLength = contextLength - 1;
            if (char.IsHighSurrogate(_rules[_ruleIndex + postLength - 1]))
            {
                --postLength;
            }
        }
        var post = _rules.Substring(_ruleIndex, postLength);
        return new CollationParseException(reason, _ruleIndex, pre, post);
    }
}
