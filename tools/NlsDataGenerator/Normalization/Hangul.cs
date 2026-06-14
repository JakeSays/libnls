namespace NlsDataGenerator.Normalization;

// Korean Hangul/Jamo algorithmic composition, ported from ICU's Hangul (normalizer2impl.h).
// Hangul syllables decompose algorithmically into leading/vowel/trailing jamo, so they carry no
// stored mapping; the builder synthesizes their norm16 data instead.
internal static class Hangul
{
    public const int JamoLBase = 0x1100;
    public const int JamoLEnd = 0x1112;
    public const int JamoVBase = 0x1161;
    public const int JamoVEnd = 0x1175;
    public const int JamoTBase = 0x11A7;
    public const int JamoTEnd = 0x11C2;

    public const int HangulBase = 0xAC00;
    public const int HangulEnd = 0xD7A3;

    public const int JamoLCount = 19;
    public const int JamoVCount = 21;
    public const int JamoTCount = 28;

    public const int JamoVtCount = JamoVCount * JamoTCount;
    public const int HangulCount = JamoLCount * JamoVCount * JamoTCount;
    public const int HangulLimit = HangulBase + HangulCount;

    public static bool IsHangul(int c)
    {
        return HangulBase <= c && c < HangulLimit;
    }

    public static bool IsHangulLv(int c)
    {
        c -= HangulBase;
        return 0 <= c && c < HangulCount && (c % JamoTCount) == 0;
    }

    public static bool IsJamoL(int c)
    {
        return (uint)(c - JamoLBase) < JamoLCount;
    }

    public static bool IsJamoV(int c)
    {
        return (uint)(c - JamoVBase) < JamoVCount;
    }

    public static bool IsJamoT(int c)
    {
        var t = c - JamoTBase;
        // Excludes JamoTBase itself.
        return 0 < t && t < JamoTCount;
    }

    public static bool IsJamo(int c)
    {
        return JamoLBase <= c && c <= JamoTEnd
            && (c <= JamoLEnd || (JamoVBase <= c && c <= JamoVEnd) || JamoTBase < c);
    }

    // Decomposes a Hangul syllable into its 2 or 3 constituent jamo, returning the length.
    public static int Decompose(int c, char[] buffer)
    {
        c -= HangulBase;
        var c2 = c % JamoTCount;
        c /= JamoTCount;
        buffer[0] = (char)(JamoLBase + c / JamoVCount);
        buffer[1] = (char)(JamoVBase + c % JamoVCount);
        if (c2 == 0)
        {
            return 2;
        }
        buffer[2] = (char)(JamoTBase + c2);
        return 3;
    }

    // The raw (non-recursive) decomposition of a Hangul syllable: always length 2 (LVT -> LV + T,
    // or LV -> L + V).
    public static void GetRawDecomposition(int c, char[] buffer)
    {
        var orig = c;
        c -= HangulBase;
        var c2 = c % JamoTCount;
        if (c2 == 0)
        {
            c /= JamoTCount;
            buffer[0] = (char)(JamoLBase + c / JamoVCount);
            buffer[1] = (char)(JamoVBase + c % JamoVCount);
        }
        else
        {
            buffer[0] = (char)(orig - c2);
            buffer[1] = (char)(JamoTBase + c2);
        }
    }
}
