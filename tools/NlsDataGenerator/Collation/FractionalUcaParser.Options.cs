namespace NlsDataGenerator.Collation;

// FractionalUCA "[...]" option lines, ported from genuca's readAnOption. Only the options that
// affect the root data are handled: the Han orderings, compressible lead bytes, the fixed
// secondary/tertiary boundary bytes, and the UCA version. Everything else is ignored.
internal sealed partial class FractionalUcaParser
{
    private static readonly string[] FixedByteOptions =
    {
        "[fixed secondary common byte",
        "[fixed tertiary common byte",
        "[fixed last secondary common byte",
        "[fixed first ignorable secondary byte",
        "[fixed first ignorable tertiary byte",
    };

    private void ReadOption(string line)
    {
        if (line.StartsWith("[Unified_Ideograph", StringComparison.Ordinal))
        {
            ParseUnifiedIdeograph(line);
            return;
        }
        if (line.StartsWith("[radical", StringComparison.Ordinal))
        {
            ParseRadical(line);
            return;
        }
        if (line.StartsWith("[top_byte", StringComparison.Ordinal))
        {
            ParseTopByte(line);
            return;
        }
        if (line.StartsWith("[UCA version", StringComparison.Ordinal))
        {
            ParseUcaVersion(line);
            return;
        }
        foreach (var name in FixedByteOptions)
        {
            if (line.StartsWith(name, StringComparison.Ordinal))
            {
                var pos = SkipSpaces(line, name.Length);
                _fixedBytes[name] = (int)(ParseWeight(line, ref pos, "]", 1) >> 24);
                return;
            }
        }
        // Any other directive does not affect the root data.
    }

    private void ParseUnifiedIdeograph(string line)
    {
        _implicitHanOrder = new HanOrder();
        var pos = SkipSpaces(line, "[Unified_Ideograph".Length);
        while (pos < line.Length && line[pos] != ']')
        {
            var end = pos;
            while (end < line.Length && line[end] != ' ' && line[end] != '\t' && line[end] != ']')
            {
                ++end;
            }
            var (start, last) = ParseCodePointRange(line[pos..end]);
            _implicitHanOrder.AddRange(start, last);
            pos = SkipSpaces(line, end);
        }
        if (_hanOrderKind == HanOrderKind.Implicit)
        {
            _implicitHanOrder.Apply(_builder);
        }
        _implicitHanOrder.Done = true;
    }

    private void ParseRadical(string line)
    {
        if (_radicalStrokeOrder is null)
        {
            if (_implicitHanOrder is null)
            {
                throw new InvalidOperationException("[radical] before [Unified_Ideograph]");
            }
            _radicalStrokeOrder = new HanOrder();
        }
        if (line.StartsWith("[radical end]", StringComparison.Ordinal))
        {
            if (_hanOrderKind == HanOrderKind.RadicalStroke)
            {
                _radicalStrokeOrder.Apply(_builder);
            }
            _radicalStrokeOrder.Done = true;
            return;
        }
        // Han characters and ranges are listed between ':' and ']' in radical-stroke order; the
        // radical number and character before ':' are ignored.
        var colon = line.IndexOf(':');
        var bracket = line.IndexOf(']');
        if (colon < 0 || bracket < 0 || colon + 1 >= bracket)
        {
            throw new InvalidOperationException($"[radical]: no Han characters between : and ]: {line}");
        }
        var hanText = line[(colon + 1)..bracket];
        var i = 0;
        while (i < hanText.Length)
        {
            var start = char.ConvertToUtf32(hanText, i);
            i += char.IsHighSurrogate(hanText[i]) ? 2 : 1;
            int end;
            if (i < hanText.Length && hanText[i] == '-')
            {
                ++i;
                end = char.ConvertToUtf32(hanText, i);
                i += char.IsHighSurrogate(hanText[i]) ? 2 : 1;
            }
            else
            {
                end = start;
            }
            _radicalStrokeOrder.AddRange(start, end);
        }
    }

    private void ParseTopByte(string line)
    {
        if (!line.Contains("COMPRESS", StringComparison.Ordinal))
        {
            return;
        }
        var pos = SkipSpaces(line, "[top_byte".Length);
        var leadByte = Hex(CharAt(line, pos)) * 16 + Hex(CharAt(line, pos + 1));
        _builder.SetCompressibleLeadByte((uint)leadByte);
    }

    private void ParseUcaVersion(string line)
    {
        var eq = line.IndexOf('=');
        var rest = line[(eq + 1)..].Trim().TrimEnd(']').Trim();
        var parts = rest.Split('.');
        for (var i = 0; i < 4 && i < parts.Length; ++i)
        {
            if (int.TryParse(parts[i], out var value))
            {
                UcaVersion[i] = value;
            }
        }
    }

    private static (int Start, int End) ParseCodePointRange(string token)
    {
        var dots = token.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            var c = Convert.ToInt32(token, 16);
            return (c, c);
        }
        return (Convert.ToInt32(token[..dots], 16), Convert.ToInt32(token[(dots + 2)..], 16));
    }
}
