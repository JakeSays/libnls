namespace NlsDataGenerator.Collation;

// Collation v2 basic definitions and helpers, ported from ICU's Collation (collation.h/.cpp).
// Data structures other than expansion tables store 32-bit CEs (CE32) that are either specials
// (distinguished by a tag in the low byte) or compact forms of 64-bit CEs. This type is the shared
// vocabulary: the weight/mask constants, the special-CE32 tags, and the bit-twiddling helpers that
// pack and unpack CE32s and primary weights.
internal static class Collator
{
    // Special sort key bytes for all levels.
    public const byte TerminatorByte = 0;
    public const byte LevelSeparatorByte = 1;

    // The secondary/tertiary lower limit for tailoring before any root elements.
    public const uint BeforeWeight16 = 0x0100;

    // Merge-sort-key separator; same as the primary/identical weights of U+FFFE.
    public const byte MergeSeparatorByte = 2;
    public const uint MergeSeparatorPrimary = 0x02000000;
    public const uint MergeSeparatorCe32 = 0x02000505;

    public const byte PrimaryCompressionLowByte = 3;
    public const byte PrimaryCompressionHighByte = 0xFF;

    // Default secondary/tertiary weight lead byte.
    public const byte CommonByte = 5;
    public const uint CommonWeight16 = 0x0500;
    public const uint CommonSecondaryCe = 0x05000000;
    public const uint CommonTertiaryCe = 0x0500;
    public const uint CommonSecAndTerCe = 0x05000500;

    public const uint SecondaryMask = 0xFFFF0000;
    public const uint CaseMask = 0xC000;
    public const uint SecondaryAndCaseMask = SecondaryMask | CaseMask;
    // Only the 2*6 bits for the pure tertiary weight.
    public const uint OnlyTertiaryMask = 0x3F3F;
    // Only the secondary & tertiary bits; no case, no quaternary.
    public const uint OnlySecTerMask = SecondaryMask | OnlyTertiaryMask;
    public const uint CaseAndTertiaryMask = CaseMask | OnlyTertiaryMask;
    public const uint QuaternaryMask = 0xC0;
    public const uint CaseAndQuaternaryMask = CaseMask | QuaternaryMask;

    public const byte UnassignedImplicitByte = 0xFE;
    // First unassigned: AlphabeticIndex overflow boundary; a 3-byte primary so it fits the root
    // elements table.
    public const uint FirstUnassignedPrimary = 0xFE040200;

    public const byte TrailWeightByte = 0xFF;
    public const uint FirstTrailingPrimary = 0xFF020200;
    public const uint MaxPrimary = 0xFFFF0000;
    public const uint MaxRegularCe32 = 0xFFFF0505;

    // CE32 / primary for U+FFFD (third-highest primary weight, as in UCA 6.3+).
    public const uint FffdPrimary = MaxPrimary - 0x20000;
    public const uint FffdCe32 = MaxRegularCe32 - 0x20000;

    // A CE32 is special if its low byte is this or greater; this value itself means fall back to
    // the base collator.
    public const byte SpecialCe32LowByte = 0xC0;
    public const uint FallbackCe32 = SpecialCe32LowByte;
    public const byte LongPrimaryCe32LowByte = 0xC1;

    public const uint UnassignedCe32 = 0xFFFFFFFF;

    public const uint NoCe32 = 1;

    public const uint NoCePrimary = 1;
    public const uint NoCeWeight16 = 0x0100;
    public const long NoCe = 0x101000100L;

    // Special-CE32 tags, from bits 3..0 of a special 32-bit CE. Bits 31..8 carry tag-specific data.
    public const int FallbackTag = 0;
    public const int LongPrimaryTag = 1;
    public const int LongSecondaryTag = 2;
    public const int ReservedTag3 = 3;
    public const int LatinExpansionTag = 4;
    public const int Expansion32Tag = 5;
    public const int ExpansionTag = 6;
    public const int BuilderDataTag = 7;
    public const int PrefixTag = 8;
    public const int ContractionTag = 9;
    public const int DigitTag = 10;
    public const int U0000Tag = 11;
    public const int HangulTag = 12;
    public const int LeadSurrogateTag = 13;
    public const int OffsetTag = 14;
    public const int ImplicitTag = 15;

    public const int MaxExpansionLength = 31;
    public const int MaxIndex = 0x7FFFF;

    // Contraction CE32 flags.
    public const uint ContractSingleCpNoMatch = 0x100;
    public const uint ContractNextCcc = 0x200;
    public const uint ContractTrailingCcc = 0x400;
    public const uint ContractHasStarter = 0x800;

    // For HANGUL_TAG: none of its Jamo CE32s is special.
    public const uint HangulNoSpecialJamo = 0x100;

    public const uint LeadAllUnassigned = 0;
    public const uint LeadAllFallback = 0x100;
    public const uint LeadMixed = 0x200;
    public const uint LeadTypeMask = 0x300;

    public static bool IsAssignedCe32(uint ce32)
    {
        return ce32 != FallbackCe32 && ce32 != UnassignedCe32;
    }

    public static uint MakeLongPrimaryCe32(uint p)
    {
        return p | LongPrimaryCe32LowByte;
    }

    public static uint PrimaryFromLongPrimaryCe32(uint ce32)
    {
        return ce32 & 0xFFFFFF00;
    }

    public static long CeFromLongPrimaryCe32(uint ce32)
    {
        return ((long)(ce32 & 0xFFFFFF00) << 32) | CommonSecAndTerCe;
    }

    public static uint MakeLongSecondaryCe32(uint lower32)
    {
        return lower32 | SpecialCe32LowByte | LongSecondaryTag;
    }

    public static long CeFromLongSecondaryCe32(uint ce32)
    {
        return ce32 & 0xFFFFFF00;
    }

    public static uint MakeCe32FromTagIndexAndLength(int tag, int index, int length)
    {
        return (uint)((index << 13) | (length << 8) | SpecialCe32LowByte | tag);
    }

    public static uint MakeCe32FromTagAndIndex(int tag, int index)
    {
        return (uint)((index << 13) | SpecialCe32LowByte | tag);
    }

    public static bool IsSpecialCe32(uint ce32)
    {
        return (ce32 & 0xFF) >= SpecialCe32LowByte;
    }

    public static int TagFromCe32(uint ce32)
    {
        return (int)(ce32 & 0xF);
    }

    public static bool HasCe32Tag(uint ce32, int tag)
    {
        return IsSpecialCe32(ce32) && TagFromCe32(ce32) == tag;
    }

    public static bool IsLongPrimaryCe32(uint ce32)
    {
        return HasCe32Tag(ce32, LongPrimaryTag);
    }

    public static bool IsSimpleOrLongCe32(uint ce32)
    {
        return !IsSpecialCe32(ce32)
            || TagFromCe32(ce32) == LongPrimaryTag
            || TagFromCe32(ce32) == LongSecondaryTag;
    }

    // True if the ce32 yields one or more CEs without further data lookups.
    public static bool IsSelfContainedCe32(uint ce32)
    {
        return !IsSpecialCe32(ce32)
            || TagFromCe32(ce32) == LongPrimaryTag
            || TagFromCe32(ce32) == LongSecondaryTag
            || TagFromCe32(ce32) == LatinExpansionTag;
    }

    public static bool IsPrefixCe32(uint ce32)
    {
        return HasCe32Tag(ce32, PrefixTag);
    }

    public static bool IsContractionCe32(uint ce32)
    {
        return HasCe32Tag(ce32, ContractionTag);
    }

    public static bool Ce32HasContext(uint ce32)
    {
        return IsSpecialCe32(ce32)
            && (TagFromCe32(ce32) == PrefixTag || TagFromCe32(ce32) == ContractionTag);
    }

    // The first of the two Latin-expansion CEs encoded in ce32.
    public static long LatinCe0FromCe32(uint ce32)
    {
        return ((long)(ce32 & 0xFF000000) << 32) | CommonSecondaryCe | ((ce32 & 0xFF0000) >> 8);
    }

    // The second of the two Latin-expansion CEs encoded in ce32.
    public static long LatinCe1FromCe32(uint ce32)
    {
        return ((ce32 & 0xFF00) << 16) | CommonTertiaryCe;
    }

    public static int IndexFromCe32(uint ce32)
    {
        return (int)(ce32 >> 13);
    }

    public static int LengthFromCe32(uint ce32)
    {
        return (int)((ce32 >> 8) & 31);
    }

    public static int DigitFromCe32(uint ce32)
    {
        return (int)((ce32 >> 8) & 0xF);
    }

    // A 64-bit CE from a simple (non-special) CE32: ppppsstt -> pppp0000ss00tt00.
    public static long CeFromSimpleCe32(uint ce32)
    {
        return ((long)(ce32 & 0xFFFF0000) << 32) | ((ce32 & 0xFF00) << 16) | ((ce32 & 0xFF) << 8);
    }

    // A 64-bit CE from a simple/long-primary/long-secondary CE32.
    public static long CeFromCe32(uint ce32)
    {
        var tertiary = ce32 & 0xFF;
        if (tertiary < SpecialCe32LowByte)
        {
            return ((long)(ce32 & 0xFFFF0000) << 32) | ((ce32 & 0xFF00) << 16) | (tertiary << 8);
        }
        ce32 -= tertiary;
        if ((tertiary & 0xF) == LongPrimaryTag)
        {
            // long-primary form ppppppC1 -> pppppp0005000500
            return ((long)ce32 << 32) | CommonSecAndTerCe;
        }
        // long-secondary form ssssttC2 -> 00000000sssstt00
        return ce32;
    }

    // A CE from a primary weight.
    public static long MakeCe(uint p)
    {
        return ((long)p << 32) | CommonSecAndTerCe;
    }

    // A CE from a primary weight, 16-bit secondary/tertiary weights, and a 2-bit quaternary.
    public static long MakeCe(uint p, uint s, uint t, uint q)
    {
        return ((long)p << 32) | ((long)s << 16) | t | (q << 6);
    }

    // Increments a 2-byte primary by a code point offset.
    public static uint IncTwoBytePrimaryByOffset(uint basePrimary, bool isCompressible, int offset)
    {
        uint primary;
        if (isCompressible)
        {
            offset += ((int)(basePrimary >> 16) & 0xFF) - 4;
            primary = (uint)((offset % 251) + 4) << 16;
            offset /= 251;
        }
        else
        {
            offset += ((int)(basePrimary >> 16) & 0xFF) - 2;
            primary = (uint)((offset % 254) + 2) << 16;
            offset /= 254;
        }
        return primary | ((basePrimary & 0xFF000000) + (uint)(offset << 24));
    }

    // Increments a 3-byte primary by a code point offset.
    public static uint IncThreeBytePrimaryByOffset(uint basePrimary, bool isCompressible, int offset)
    {
        offset += ((int)(basePrimary >> 8) & 0xFF) - 2;
        var primary = (uint)((offset % 254) + 2) << 8;
        offset /= 254;
        if (isCompressible)
        {
            offset += ((int)(basePrimary >> 16) & 0xFF) - 4;
            primary |= (uint)((offset % 251) + 4) << 16;
            offset /= 251;
        }
        else
        {
            offset += ((int)(basePrimary >> 16) & 0xFF) - 2;
            primary |= (uint)((offset % 254) + 2) << 16;
            offset /= 254;
        }
        return primary | ((basePrimary & 0xFF000000) + (uint)(offset << 24));
    }

    // Decrements a 2-byte primary by one range step (1..0x7f).
    public static uint DecTwoBytePrimaryByOneStep(uint basePrimary, bool isCompressible, int step)
    {
        var byte2 = ((int)(basePrimary >> 16) & 0xFF) - step;
        if (isCompressible)
        {
            if (byte2 < 4)
            {
                byte2 += 251;
                basePrimary -= 0x1000000;
            }
        }
        else
        {
            if (byte2 < 2)
            {
                byte2 += 254;
                basePrimary -= 0x1000000;
            }
        }
        return (basePrimary & 0xFF000000) | ((uint)byte2 << 16);
    }

    // Decrements a 3-byte primary by one range step (1..0x7f).
    public static uint DecThreeBytePrimaryByOneStep(uint basePrimary, bool isCompressible, int step)
    {
        var byte3 = ((int)(basePrimary >> 8) & 0xFF) - step;
        if (byte3 >= 2)
        {
            return (basePrimary & 0xFFFF0000) | ((uint)byte3 << 8);
        }
        byte3 += 254;
        var byte2 = ((int)(basePrimary >> 16) & 0xFF) - 1;
        if (isCompressible)
        {
            if (byte2 < 4)
            {
                byte2 = 0xFE;
                basePrimary -= 0x1000000;
            }
        }
        else
        {
            if (byte2 < 2)
            {
                byte2 = 0xFF;
                basePrimary -= 0x1000000;
            }
        }
        return (basePrimary & 0xFF000000) | ((uint)byte2 << 16) | ((uint)byte3 << 8);
    }

    // A 3-byte primary for c's OFFSET_TAG data "CE".
    public static uint GetThreeBytePrimaryForOffsetData(int c, long dataCe)
    {
        var p = (uint)(dataCe >> 32);
        var lower32 = (int)dataCe;
        var offset = (c - (lower32 >> 8)) * (lower32 & 0x7F);
        var isCompressible = (lower32 & 0x80) != 0;
        return IncThreeBytePrimaryByOffset(p, isCompressible, offset);
    }

    // The unassigned-character implicit primary weight for any valid code point c.
    public static uint UnassignedPrimaryFromCodePoint(int c)
    {
        // Create a gap before U+0000. Use c=-1 for [first unassigned].
        ++c;
        uint primary = (uint)(2 + (c % 18) * 14);
        c /= 18;
        primary |= (uint)(2 + (c % 254)) << 8;
        c /= 254;
        primary |= (uint)(4 + (c % 251)) << 16;
        return primary | ((uint)UnassignedImplicitByte << 24);
    }

    public static long UnassignedCeFromCodePoint(int c)
    {
        return MakeCe(UnassignedPrimaryFromCodePoint(c));
    }
}
