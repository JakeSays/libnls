namespace NlsDataGenerator.Collation;

// Constants for the fast-Latin mini-table, from ICU's CollationFastLatin (collationfastlatin.h).
// The table maps the common Latin and punctuation characters to compact 16-bit "mini CEs" so the
// runtime can compare common Latin text without full CE iteration. The builder packs primary,
// secondary, case, and tertiary weights into these mini CEs using the thresholds below.
internal static class CollationFastLatinFormat
{
    public const int Version = 2;

    public const int LatinMax = 0x17F;
    public const int LatinLimit = LatinMax + 1;
    public const int PunctStart = 0x2000;
    public const int PunctLimit = 0x2040;
    public const int NumFastChars = LatinLimit + (PunctLimit - PunctStart);

    public const uint ShortPrimaryMask = 0xFC00;
    public const uint IndexMask = 0x3FF;
    public const uint SecondaryMask = 0x3E0;
    public const uint CaseMask = 0x18;
    public const uint TertiaryMask = 7;

    public const uint Contraction = 0x400;
    public const uint Expansion = 0x800;

    public const uint MinLong = 0xC00;
    public const uint LongInc = 8;
    public const uint MaxLong = 0xFF8;

    public const uint MinShort = 0x1000;
    public const uint ShortInc = 0x400;
    public const uint MaxShort = ShortPrimaryMask;

    public const uint MinSecBefore = 0;
    public const uint SecInc = 0x20;
    public const uint MaxSecBefore = MinSecBefore + 4 * SecInc;
    public const uint CommonSec = MaxSecBefore + SecInc;
    public const uint MinSecAfter = CommonSec + SecInc;
    public const uint MaxSecAfter = MinSecAfter + 5 * SecInc;
    public const uint MinSecHigh = MaxSecAfter + SecInc;
    public const uint MaxSecHigh = SecondaryMask;

    public const uint LowerCase = 8;

    public const uint CommonTer = 0;
    public const uint MaxTerAfter = 7;

    public const uint BailOut = 1;

    public const uint ContrCharMask = 0x1FF;
    public const int ContrLengthShift = 9;

    public static int GetCharIndex(char c)
    {
        if (c <= LatinMax)
        {
            return c;
        }
        if (PunctStart <= c && c < PunctLimit)
        {
            return c - (PunctStart - LatinLimit);
        }
        // Not a fast Latin character.
        return -1;
    }
}
