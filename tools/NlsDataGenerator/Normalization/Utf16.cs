namespace NlsDataGenerator.Normalization;

// Small UTF-16 helpers matching ICU's U16_* macro semantics on .NET strings, tolerant of the lone
// surrogates that the well-formed mappings here never actually contain.
internal static class Utf16
{
    // The code point starting at index i (a code-point boundary).
    public static int CodePointAt(string s, int i)
    {
        var hi = s[i];
        if (char.IsHighSurrogate(hi) && (i + 1) < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            return char.ConvertToUtf32(hi, s[i + 1]);
        }
        return hi;
    }

    // The number of UTF-16 units a code point occupies.
    public static int CharCount(int codePoint)
    {
        return codePoint >= Unicode.SupplementaryMin ? 2 : 1;
    }

    // The code point ending at the final unit (U16_GET semantics at length-1).
    public static int LastCodePoint(string s)
    {
        var last = s[^1];
        if (char.IsLowSurrogate(last) && s.Length >= 2 && char.IsHighSurrogate(s[^2]))
        {
            return char.ConvertToUtf32(s[^2], last);
        }
        return last;
    }
}
