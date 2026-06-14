using System.Text;
using NlsDataGenerator.Parsing;

namespace NlsDataGenerator.Collation;

// Processes UCA_Rules.txt into the root bundle's UCARules string, ported from genrb's parseUCARules
// (parse.cpp). It strips '#' comments and unquoted whitespace, resolves backslash escapes, and
// preserves [bracketed commands] verbatim (including their internal spaces). The result is stored as
// the root collation bundle's UCARules string resource.
internal static class UcaRulesProcessor
{
    public static string Process(string content)
    {
        var result = new StringBuilder(content.Length);
        var quoted = false;
        var i = 0;
        while (i < content.Length)
        {
            var c = content[i];
            if (c == '\'')
            {
                quoted = !quoted;
            }
            if (c == '[' && !quoted)
            {
                // Preserve the command, including its spaces, up to and including ']'.
                while (c != ']')
                {
                    result.Append(c);
                    if (++i >= content.Length)
                    {
                        return result.ToString();
                    }
                    c = content[i];
                }
            }
            else if (c == '#' && !quoted)
            {
                // Skip a comment to the end of the line.
                while (i < content.Length && c != '\r' && c != '\n')
                {
                    c = ++i < content.Length ? content[i] : '\0';
                }
                continue;
            }
            else if (c == '\\')
            {
                var offset = i + 1;
                var cp = IcuUnescape.UnescapeOne(content, ref offset);
                result.Append(char.ConvertFromUtf32(cp));
                i = offset;
                continue;
            }
            else if (!quoted && (c == ' ' || c == '\t' || c == '\r' || c == '\n'))
            {
                ++i;
                continue;
            }
            result.Append(c);
            ++i;
        }
        return result.ToString();
    }
}
