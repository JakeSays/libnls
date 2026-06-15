namespace NlsDataGenerator.Collation;

// Maps a script's sample code point to its reordering code, ported from genuca's hardcoded
// sampleCharsToScripts table plus the newer scripts genuca gets from ICU's uscript samples. The
// FractionalUCA parser calls Lookup() for each "FDD1 <sampleChar>" line to find the script whose
// primary boundary starts there. Script names resolve to UScriptCode values through the vendored
// uscript.h (passed in); the five special groups map to UCOL_REORDER_CODE_* values.
internal static class ScriptSampleChars
{
    public const int ReorderCodeSpace = CollationData.UcolReorderCodeFirst;
    public const int ReorderCodePunctuation = CollationData.UcolReorderCodeFirst + 1;
    public const int ReorderCodeSymbol = CollationData.UcolReorderCodeFirst + 2;
    public const int ReorderCodeCurrency = CollationData.UcolReorderCodeFirst + 3;
    public const int ReorderCodeDigit = CollationData.UcolReorderCodeFirst + 4;

    // (sample code point, bare name). Special groups use the lowercase pseudo-names; everything else
    // is a USCRIPT_<name> token after upper-casing.
    private static readonly (int Cp, string Name)[] Table =
    {
        (0x00A0, "SPACE"), (0x201C, "PUNCTUATION"), (0x263A, "SYMBOL"), (0x20AC, "CURRENCY"),
        (0x0034, "DIGIT"),
        (0x004C, "LATIN"), (0x03A9, "GREEK"), (0x03E2, "COPTIC"), (0x042F, "CYRILLIC"),
        (0x2C00, "GLAGOLITIC"), (0x1036B, "OLD_PERMIC"), (0x10D3, "GEORGIAN"), (0x0531, "ARMENIAN"),
        (0x05D0, "HEBREW"), (0x10900, "PHOENICIAN"), (0x0800, "SAMARITAN"), (0x0628, "ARABIC"),
        (0x0710, "SYRIAC"), (0x0840, "MANDAIC"), (0x078C, "THAANA"), (0x07CA, "NKO"), (0x07D8, "NKO"),
        (0x2D30, "TIFINAGH"), (0x2D5E, "TIFINAGH"), (0x12A0, "ETHIOPIC"), (0x0905, "DEVANAGARI"),
        (0x0995, "BENGALI"), (0x0A15, "GURMUKHI"), (0x0A95, "GUJARATI"), (0x0B15, "ORIYA"),
        (0x0B95, "TAMIL"), (0x0C15, "TELUGU"), (0x0C95, "KANNADA"), (0x0D15, "MALAYALAM"),
        (0x0D85, "SINHALA"), (0xABC0, "MEITEI_MAYEK"), (0xA800, "SYLOTI_NAGRI"), (0xA882, "SAURASHTRA"),
        (0x11083, "KAITHI"), (0x11152, "MAHAJANI"), (0x11183, "SHARADA"), (0x11208, "KHOJKI"),
        (0x112BE, "KHUDAWADI"), (0x1128F, "MULTANI"), (0x11315, "GRANTHA"), (0x11412, "NEWA"),
        (0x11484, "TIRHUTA"), (0x1158E, "SIDDHAM"), (0x1160E, "MODI"), (0x11680, "TAKRI"),
        (0x1180B, "DOGRA"), (0x11717, "AHOM"), (0x11D71, "GUNJALA_GONDI"), (0x1B83, "SUNDANESE"),
        (0x11005, "BRAHMI"), (0x10A00, "KHAROSHTHI"), (0x11C0E, "BHAIKSUKI"), (0x0E17, "THAI"),
        (0x0EA5, "LAO"), (0xAA80, "TAI_VIET"), (0x0F40, "TIBETAN"), (0x11C72, "MARCHEN"),
        (0x1C00, "LEPCHA"), (0xA840, "PHAGS_PA"), (0x1900, "LIMBU"), (0x1703, "TAGALOG"),
        (0x1723, "HANUNOO"), (0x1743, "BUHID"), (0x1763, "TAGBANWA"), (0x1A00, "BUGINESE"),
        (0x11EE5, "MAKASAR"), (0x1BC0, "BATAK"), (0xA930, "REJANG"), (0xA90A, "KAYAH_LI"),
        (0x1000, "MYANMAR"), (0x10D12, "HANIFI_ROHINGYA"), (0x11103, "CHAKMA"), (0x1780, "KHMER"),
        (0x1950, "TAI_LE"), (0x1980, "NEW_TAI_LUE"), (0x1A20, "LANNA"), (0xAA00, "CHAM"),
        (0x1B05, "BALINESE"), (0xA984, "JAVANESE"), (0x1826, "MONGOLIAN"), (0x1C5A, "OL_CHIKI"),
        (0x13C4, "CHEROKEE"), (0x104B5, "OSAGE"), (0x14C0, "CANADIAN_ABORIGINAL"), (0x168F, "OGHAM"),
        (0x16A0, "RUNIC"), (0x10CA1, "OLD_HUNGARIAN"), (0x10C00, "ORKHON"), (0xA549, "VAI"),
        (0xA6A0, "BAMUM"), (0x16AE6, "BASSA_VAH"), (0x1E802, "MENDE"), (0x16E40, "MEDEFAIDRIN"),
        (0x1E909, "ADLAM"), (0xAC00, "HANGUL"), (0x304B, "HIRAGANA"), (0x30AB, "KATAKANA"),
        (0x3105, "BOPOMOFO"), (0xA288, "YI"), (0xA4D0, "LISU"), (0xA4E8, "LISU"), (0x16F00, "MIAO"),
        (0x118B4, "WARANG_CITI"), (0x11AC0, "PAU_CIN_HAU"), (0x16B1C, "PAHAWH_HMONG"),
        (0x10280, "LYCIAN"), (0x102A0, "CARIAN"), (0x102B7, "CARIAN"), (0x10920, "LYDIAN"),
        (0x10300, "OLD_ITALIC"), (0x10308, "OLD_ITALIC"), (0x10330, "GOTHIC"), (0x10414, "DESERET"),
        (0x10450, "SHAVIAN"), (0x1BC20, "DUPLOYAN"), (0x10480, "OSMANYA"), (0x10500, "ELBASAN"),
        (0x10537, "CAUCASIAN_ALBANIAN"), (0x110D0, "SORA_SOMPENG"), (0x16A4F, "MRO"),
        (0x10000, "LINEAR_B"), (0x10647, "LINEAR_A"), (0x10800, "CYPRIOT"), (0x10A60, "OLD_SOUTH_ARABIAN"),
        (0x10A95, "OLD_NORTH_ARABIAN"), (0x10B00, "AVESTAN"), (0x10873, "PALMYRENE"),
        (0x10896, "NABATAEAN"), (0x108F4, "HATRAN"), (0x10840, "IMPERIAL_ARAMAIC"),
        (0x10B40, "INSCRIPTIONAL_PARTHIAN"), (0x10B60, "INSCRIPTIONAL_PAHLAVI"),
        (0x10B8F, "PSALTER_PAHLAVI"), (0x10AC1, "MANICHAEAN"), (0x10AD8, "MANICHAEAN"),
        (0x10F19, "OLD_SOGDIAN"), (0x10F42, "SOGDIAN"), (0x10380, "UGARITIC"), (0x103A0, "OLD_PERSIAN"),
        (0x12000, "CUNEIFORM"), (0x13153, "EGYPTIAN_HIEROGLYPHS"), (0x109A0, "MEROITIC_CURSIVE"),
        (0x10980, "MEROITIC_HIEROGLYPHS"), (0x14400, "ANATOLIAN_HIEROGLYPHS"), (0x18229, "TANGUT"),
        (0x5B57, "HAN"), (0x11D10, "MASARAM_GONDI"), (0x11A0B, "ZANABAZAR_SQUARE"), (0x11A5C, "SOYOMBO"),
        (0x1B1C4, "NUSHU"), (0xFDD0, "UNKNOWN"),

        // Newer scripts genuca resolves through ICU's uscript samples (not in its hardcoded table).
        (0x11392, "TULU_TIGALARI"), (0x1190C, "DIVES_AKURU"), (0x119CE, "NANDINAGARI"),
        (0x11DC6, "TOLONG_SIKI"), (0x1E6D5, "TAI_YO"), (0x11F1B, "KAWI"), (0x1E5D0, "OL_ONAL"),
        (0x16EA1, "BERIA_ERFE"), (0x10D5D, "GARAY"), (0x1E108, "NYIAKENG_PUACHUE_HMONG"),
        (0x1E290, "TOTO"), (0x1E2E1, "WANCHO"), (0x1E4E6, "NAG_MUNDARI"), (0x10950, "SIDETIC"),
        (0x10582, "VITHKUQI"), (0x105C2, "TODHRI"), (0x16ABC, "TANGSA"), (0x11BC4, "SUNUWAR"),
        (0x1611C, "GURUNG_KHEMA"), (0x16D45, "KIRAT_RAI"), (0x12FE5, "CYPRO_MINOAN"),
        (0x10FF1, "ELYMAIC"), (0x10F7C, "OLD_UYGHUR"), (0x10E88, "YEZIDI"), (0x10FBF, "CHORASMIAN"),
        (0x18C65, "KHITAN_SMALL_SCRIPT"),
    };

    // Builds the sample-char -> reordering-code lookup, resolving script names via the uscript map.
    public static Dictionary<int, int> Build(IReadOnlyDictionary<string, int> uscript)
    {
        var map = new Dictionary<int, int>(Table.Length);
        foreach (var (cp, name) in Table)
        {
            map[cp] = Resolve(name, uscript);
        }
        return map;
    }

    private static int Resolve(string name, IReadOnlyDictionary<string, int> uscript)
    {
        switch (name)
        {
            case "SPACE":
                return ReorderCodeSpace;
            case "PUNCTUATION":
                return ReorderCodePunctuation;
            case "SYMBOL":
                return ReorderCodeSymbol;
            case "CURRENCY":
                return ReorderCodeCurrency;
            case "DIGIT":
                return ReorderCodeDigit;
            default:
                if (uscript.TryGetValue($"USCRIPT_{name}", out var code))
                {
                    return code;
                }
                throw new InvalidOperationException($"unknown script USCRIPT_{name} in uscript.h");
        }
    }
}
