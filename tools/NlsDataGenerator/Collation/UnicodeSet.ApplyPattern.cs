using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Collation;

// Parses a UnicodeSet pattern into this set, ported from ICU's UnicodeSet::applyPattern
// (uniset_props.cpp) for the subset reachable through collation rules. CollationRuleParser only
// calls this from [optimize [...]] and [suppressContractions [...]]; across every CLDR collation
// locale those sets use only single code points, c-c ranges, and a leading '^' complement, separated
// by pattern white space. Backslash escapes are already resolved upstream (the rule reader unescapes
// \uXXXX exactly as genrb's lexer does), so the iterator here sees literal code points. The set
// algebra forms ICU also parses here (nested [...], '&' intersection, '-' set difference, {string}
// elements, [:property:] patterns) are not reachable from a collation rule; they assert loudly.
internal sealed partial class UnicodeSet
{
    public void ApplyPattern(string pattern)
    {
        // mode: 0=before '[', 1=inside [...], 2=after ']'. lastItem: 0=none, 1=pending char.
        var mode = 0;
        var lastItem = 0;
        var lastChar = 0;
        var op = '\0';
        var invert = false;

        _ranges.Clear();
        var i = 0;
        while (mode != 2)
        {
            if (i >= pattern.Length)
            {
                break;
            }
            var (c, afterC) = NextCodePoint(pattern, i);
            if (c < 0)
            {
                break;
            }
            i = afterC;

            if (c == '[')
            {
                if (mode == 1)
                {
                    throw new NotSupportedException("nested UnicodeSet is not reachable in collation rules");
                }
                mode = 1;
                var (c2, afterC2) = NextCodePoint(pattern, i);
                if (c2 == '^')
                {
                    invert = true;
                    i = afterC2;
                    (c2, afterC2) = NextCodePoint(pattern, i);
                }
                if (c2 == '-')
                {
                    // A literal '-' immediately after '[' or '[^'.
                    i = afterC2;
                    lastItem = 1;
                    lastChar = c2;
                }
                // Otherwise leave c2 unconsumed; the loop re-reads it as the first element.
                continue;
            }

            if (mode == 0)
            {
                throw new InvalidOperationException("UnicodeSet pattern is missing '['");
            }

            switch (c)
            {
                case ']':
                    if (lastItem == 1)
                    {
                        Add(lastChar);
                    }
                    if (op == '-')
                    {
                        // Trailing '-' is a literal.
                        Add('-');
                    }
                    else if (op == '&')
                    {
                        throw new InvalidOperationException("trailing '&' in UnicodeSet pattern");
                    }
                    mode = 2;
                    continue;
                case '-':
                    if (op == '\0' && lastItem == 1)
                    {
                        op = '-';
                        continue;
                    }
                    if (op == '\0' && lastItem == 0)
                    {
                        // A '-' with nothing pending is a literal only just before ']'.
                        Add('-');
                        var (cNext, afterNext) = NextCodePoint(pattern, i);
                        if (cNext == ']')
                        {
                            i = afterNext;
                            mode = 2;
                            continue;
                        }
                    }
                    throw new InvalidOperationException("'-' not after a character in UnicodeSet pattern");
                case '&':
                    throw new NotSupportedException("set intersection is not reachable in collation rules");
                case '^':
                    throw new InvalidOperationException("'^' not after '[' in UnicodeSet pattern");
                case '{':
                    throw new NotSupportedException("multi-character strings are not reachable in collation rules");
                default:
                    break;
            }

            // A literal character.
            if (lastItem == 0)
            {
                lastItem = 1;
                lastChar = c;
            }
            else if (op == '-')
            {
                if (lastChar >= c)
                {
                    throw new InvalidOperationException("empty or reversed range in UnicodeSet pattern");
                }
                Add(lastChar, c);
                lastItem = 0;
                op = '\0';
            }
            else
            {
                Add(lastChar);
                lastChar = c;
            }
        }

        if (mode != 2)
        {
            throw new InvalidOperationException("UnicodeSet pattern is missing ']'");
        }
        if (invert)
        {
            Complement();
        }
    }

    // Reads the next code point at or after i, skipping leading pattern white space. Returns the code
    // point (or -1 at end) and the index just past it.
    private static (int CodePoint, int Next) NextCodePoint(string pattern, int i)
    {
        while (i < pattern.Length && PatternWhiteSpace.IsWhiteSpace(pattern[i]))
        {
            ++i;
        }
        if (i >= pattern.Length)
        {
            return (-1, i);
        }
        var c = char.ConvertToUtf32(pattern, i);
        return (c, i + (c > 0xFFFF ? 2 : 1));
    }

    // Replaces this set with its complement over the whole code-point space [0, 0x10FFFF].
    private void Complement()
    {
        var inverted = new List<(int Start, int End)>();
        var next = 0;
        foreach (var range in _ranges)
        {
            if (range.Start > next)
            {
                inverted.Add((next, range.Start - 1));
            }
            next = range.End + 1;
        }
        if (next <= Unicode.MaxCodePoint)
        {
            inverted.Add((next, Unicode.MaxCodePoint));
        }
        _ranges.Clear();
        _ranges.AddRange(inverted);
    }
}
