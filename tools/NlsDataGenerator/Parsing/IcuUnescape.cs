using System.Text;


namespace NlsDataGenerator.Parsing;

// Faithful port of ICU's u_unescapeAt / u_unescape (common/ustring.cpp), the escape processor genrb
// runs on every string token while lexing a resource bundle (via ucbuf_getcx32 -> u_unescapeAt). The
// CLDR collation rules carry \uXXXX (and occasionally \U / \x / C-style) escapes; genrb resolves them
// before the rules reach the collation builder, so when this tool reads the CLDR XML directly it must
// resolve them the same way to feed the parser — and store the same — byte-identical bytes.
internal static class IcuUnescape
{
    // C-style single-character escapes, ascending by escape code (ICU's UNESCAPE_MAP):
    // \a=BEL \b=BS \e=ESC \f=FF \n=LF \r=CR \t=HT \v=VT.
    private static readonly (char Escape, char Value)[] Map =
    [
        ('a', (char)0x07),
        ('b', (char)0x08),
        ('e', (char)0x1b),
        ('f', (char)0x0c),
        ('n', (char)0x0a),
        ('r', (char)0x0d),
        ('t', (char)0x09),
        ('v', (char)0x0b),
    ];

    // Resolves every backslash escape in the string, leaving other characters unchanged. Throws on a
    // malformed escape (genrb treats that as a fatal lexer error).
    public static string Unescape(string s)
    {
        var result = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i++];
            if (c != '\\')
            {
                result.Append(c);
                continue;
            }
            var offset = i;
            var cp = UnescapeAt(s, ref offset);
            if (cp < 0)
            {
                throw new FormatException($"malformed escape in collation rules at index {i - 1}");
            }
            result.Append(char.ConvertFromUtf32(cp));
            i = offset;
        }
        return result.ToString();
    }

    // Resolves one escape sequence at offset (the character just past the '\\'); advances offset past
    // it and returns the code point. Throws on a malformed escape.
    public static int UnescapeOne(string s, ref int offset)
    {
        var cp = UnescapeAt(s, ref offset);
        if (cp < 0)
        {
            throw new FormatException($"malformed escape at index {offset}");
        }
        return cp;
    }

    // Parses one escape sequence starting at offset (the character just past the '\\'); advances
    // offset past it and returns the code point, or -1 (ICU's 0xFFFFFFFF) on a malformed sequence.
    private static int UnescapeAt(string s, ref int offset)
    {
        var start = offset;
        var length = s.Length;
        if (offset < 0 || offset >= length)
        {
            offset = start;
            return -1;
        }

        var result = 0;
        var n = 0;
        var minDig = 0;
        var maxDig = 0;
        var bitsPerDigit = 4;
        var braces = false;

        var c = s[offset++];
        switch (c)
        {
            case 'u':
                minDig = maxDig = 4;
                break;
            case 'U':
                minDig = maxDig = 8;
                break;
            case 'x':
                minDig = 1;
                if (offset < length && s[offset] == '{')
                {
                    ++offset;
                    braces = true;
                    maxDig = 8;
                }
                else
                {
                    maxDig = 2;
                }
                break;
            default:
                var octal = Digit(c, 8);
                if (octal >= 0)
                {
                    minDig = 1;
                    maxDig = 3;
                    n = 1;
                    bitsPerDigit = 3;
                    result = octal;
                }
                break;
        }

        if (minDig != 0)
        {
            while (offset < length && n < maxDig)
            {
                c = s[offset];
                var dig = Digit(c, bitsPerDigit == 3 ? 8 : 16);
                if (dig < 0)
                {
                    break;
                }
                result = (result << bitsPerDigit) | dig;
                ++offset;
                ++n;
            }
            if (n < minDig)
            {
                offset = start;
                return -1;
            }
            if (braces)
            {
                if (c != '}')
                {
                    offset = start;
                    return -1;
                }
                ++offset;
            }
            if (result < 0 || result >= 0x110000)
            {
                offset = start;
                return -1;
            }
            // A lead surrogate joins with a following trail surrogate (escaped or literal).
            if (offset < length && char.IsHighSurrogate((char)result))
            {
                var ahead = offset + 1;
                c = s[offset];
                if (c == '\\' && ahead < length)
                {
                    var tail = UnescapeAt(s, ref ahead);
                    if (tail >= 0)
                    {
                        c = (char)tail;
                    }
                }
                if (char.IsLowSurrogate(c))
                {
                    offset = ahead;
                    result = char.ConvertToUtf32((char)result, c);
                }
            }
            return result;
        }

        foreach (var (escape, value) in Map)
        {
            if (c == escape)
            {
                return value;
            }
            if (c < escape)
            {
                break;
            }
        }

        if (c == 'c' && offset < length)
        {
            c = s[offset++];
            if (char.IsHighSurrogate(c) && offset < length && char.IsLowSurrogate(s[offset]))
            {
                c = (char)char.ConvertToUtf32(c, s[offset]);
                ++offset;
            }
            return 0x1F & c;
        }

        // Otherwise the backslash generically escapes the next character.
        if (char.IsHighSurrogate(c) && offset < length && char.IsLowSurrogate(s[offset]))
        {
            var supplementary = char.ConvertToUtf32(c, s[offset]);
            ++offset;
            return supplementary;
        }
        return c;
    }

    // One digit in the given radix (8 or 16), or -1.
    private static int Digit(char c, int radix)
    {
        if (radix == 8)
        {
            return c is >= '0' and <= '7'
                ? c - '0'
                : -1;
        }

        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - ('A' - 10),
            >= 'a' and <= 'f' => c - ('a' - 10),
            _ => -1
        };
    }
}
