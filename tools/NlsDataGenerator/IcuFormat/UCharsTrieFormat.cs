namespace NlsDataGenerator.IcuFormat;

// Serialization constants for UCharsTrie (char16 string trie), from ICU's UCharsTrie
// (unicode/ucharstrie.h). These thresholds define how node types, values, and jump deltas pack into
// one, two, or three 16-bit units; the builder must use them exactly to match ICU's output.
internal static class UCharsTrieFormat
{
    public const int MaxBranchLinearSubNodeLength = 5;
    public const int MaxSplitBranchLevels = 14;

    public const int MinLinearMatch = 0x30;
    public const int MaxLinearMatchLength = 0x10;

    public const int MinValueLead = MinLinearMatch + MaxLinearMatchLength; // 0x40
    public const int NodeTypeMask = MinValueLead - 1; // 0x3f

    public const int ValueIsFinal = 0x8000;

    // Compact value (e.g. a final value or a branch jump delta) encoding.
    public const int MaxOneUnitValue = 0x3FFF;
    public const int MinTwoUnitValueLead = MaxOneUnitValue + 1; // 0x4000
    public const int ThreeUnitValueLead = 0x7FFF;
    public const int MaxTwoUnitValue = ((ThreeUnitValueLead - MinTwoUnitValueLead) << 16) - 1; // 0x3ffeffff

    // Node value (a value attached to a match node) encoding.
    public const int MaxOneUnitNodeValue = 0xFF;
    public const int MinTwoUnitNodeValueLead = MinValueLead + ((MaxOneUnitNodeValue + 1) << 6); // 0x4040
    public const int ThreeUnitNodeValueLead = 0x7FC0;
    public const int MaxTwoUnitNodeValue = ((ThreeUnitNodeValueLead - MinTwoUnitNodeValueLead) << 10) - 1; // 0xfdffff

    // Jump delta encoding.
    public const int MaxOneUnitDelta = 0xFBFF;
    public const int MinTwoUnitDeltaLead = MaxOneUnitDelta + 1; // 0xfc00
    public const int ThreeUnitDeltaLead = 0xFFFF;
    public const int MaxTwoUnitDelta = ((ThreeUnitDeltaLead - MinTwoUnitDeltaLead) << 16) - 1; // 0x03feffff
}
