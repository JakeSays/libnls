namespace NlsDataGenerator.Collation;

// Collation attribute and strength constants from ICU's ucol.h (UColAttributeValue / UColReorderCode).
// The rule parser uses these for strengths (&, <, <<, …), the [strength]/[alternate]/[caseFirst]/…
// settings, and [reorder] codes.
internal static class Ucol
{
    public const int Default = -1;

    // Strengths.
    public const int Primary = 0;
    public const int Secondary = 1;
    public const int Tertiary = 2;
    public const int Quaternary = 3;
    public const int Identical = 15;

    // On/off and attribute values.
    public const int Off = 16;
    public const int On = 17;
    public const int Shifted = 20;
    public const int NonIgnorable = 21;
    public const int LowerFirst = 24;
    public const int UpperFirst = 25;

    // Reorder codes (UColReorderCode). Special groups start at FIRST; scripts use their UScriptCode.
    public const int ReorderCodeNone = 103;
    public const int ReorderCodeOthers = 103;
    public const int ReorderCodeFirst = 0x1000;
}
