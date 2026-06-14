namespace NlsDataGenerator.Parsing;

// Unicode Pattern_White_Space (TR31), matching ICU's PatternProps::isWhiteSpace. The collation rule
// syntax uses this exact set for token separation, not general Unicode whitespace.
internal static class PatternWhiteSpace
{
    public static bool IsWhiteSpace(int c)
    {
        if (c < 0x20)
        {
            return c >= 0x09 && c <= 0x0d;
        }
        return c == 0x20 || c == 0x85 || c == 0x200e || c == 0x200f || c == 0x2028 || c == 0x2029;
    }
}
