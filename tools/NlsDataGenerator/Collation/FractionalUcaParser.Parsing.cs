using System.Text;

namespace NlsDataGenerator.Collation;

// Low-level FractionalUCA token parsing, ported from genuca's parseWeight/parseCE. Weights are
// hex byte pairs separated by spaces and left-aligned to 4 bytes; a CE is "[pp pp, ss, tt]" or
// "[U+XXXX, ...]" where the code point's CE is looked up in the builder.
internal sealed partial class FractionalUcaParser
{
    private long ParseCe(string text, ref int pos)
    {
        ++pos; // skip '['
        if (CharAt(text, pos) == 'U' && CharAt(text, pos + 1) == '+')
        {
            pos += 2;
            var start = pos;
            while (Hex(CharAt(text, pos)) >= 0)
            {
                ++pos;
            }
            var c = Convert.ToInt32(text[start..pos], 16);
            var ce = _builder.GetSingleCe(c);
            if (CharAt(text, pos) == ']')
            {
                ++pos;
                return ce;
            }
            if (CharAt(text, pos) != ',')
            {
                throw Invalid(text);
            }
            pos = SkipSpaces(text, pos + 1);
            var w = ParseWeight(text, ref pos, ",]", 2);
            if (CharAt(text, pos) == ']')
            {
                ++pos;
                // Set the tertiary weight to w.
                return (ce & unchecked((long)0xFFFFFFFFFFFF0000UL)) | (w >> 16);
            }
            // Set the secondary weight, then the tertiary.
            ce = (ce & unchecked((long)0xFFFFFFFF00000000UL)) | w;
            pos = SkipSpaces(text, pos + 1);
            w = ParseWeight(text, ref pos, "]", 2);
            ++pos;
            return ce | (w >> 16);
        }
        else
        {
            var p = ParseWeight(text, ref pos, ",", 4);
            var ce = (long)p << 32;
            pos = SkipSpaces(text, pos + 1);
            var w = ParseWeight(text, ref pos, ",", 2);
            ce |= w;
            pos = SkipSpaces(text, pos + 1);
            w = ParseWeight(text, ref pos, "]", 2);
            ++pos;
            return ce | (w >> 16);
        }
    }

    private uint ParseWeight(string text, ref int pos, string separators, int maxBytes)
    {
        uint weight = 0;
        var numBytes = 0;
        for (;;)
        {
            // One character at a time so we don't run over a 00.
            var nibble1 = Hex(CharAt(text, pos));
            var nibble2 = Hex(CharAt(text, pos + 1));
            if (nibble1 < 0 || nibble2 < 0)
            {
                break;
            }
            if (numBytes == maxBytes || (numBytes != 0 && nibble1 == 0 && nibble2 <= 1))
            {
                // Too many bytes, or a 00/01 byte which is illegal inside a weight.
                throw Invalid(text);
            }
            weight = (weight << 8) | ((uint)nibble1 << 4) | (uint)nibble2;
            ++numBytes;
            pos += 2;
            if (CharAt(text, pos) != ' ')
            {
                break;
            }
            ++pos;
        }
        var separator = CharAt(text, pos);
        if (separator == '\0' || !separators.Contains(separator))
        {
            throw Invalid(text);
        }
        // numBytes==0 is OK (e.g. [, 82, 05]). Left-align the weight.
        while (numBytes < 4)
        {
            weight <<= 8;
            ++numBytes;
        }
        return weight;
    }

    // Parses space-separated hex code points ("0041 0301") into a UTF-16 string.
    private static string ParseHexCodePoints(string field)
    {
        var builder = new StringBuilder();
        foreach (var token in field.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ConvertFromUtf32(Convert.ToInt32(token, 16)));
        }
        return builder.ToString();
    }

    private static int SkipSpaces(string text, int pos)
    {
        while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t'))
        {
            ++pos;
        }
        return pos;
    }

    private static char CharAt(string text, int pos)
    {
        return pos < text.Length ? text[pos] : '\0';
    }

    private static int Hex(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }
        if (c is >= 'a' and <= 'f')
        {
            return c - 'a' + 10;
        }
        if (c is >= 'A' and <= 'F')
        {
            return c - 'A' + 10;
        }
        return -1;
    }

    private static InvalidOperationException Invalid(string line)
    {
        return new InvalidOperationException($"invalid FractionalUCA syntax: {line}");
    }
}
