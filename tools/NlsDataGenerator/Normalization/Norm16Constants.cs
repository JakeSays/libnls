namespace NlsDataGenerator.Normalization;

// The fixed norm16 values and bit layout the Normalizer2 runtime reads, from ICU's
// normalizer2impl.h. norm16 is the per-code-point value stored in the .nrm trie: bit 0 is
// comp-boundary-after, the rest encode the quick-check class and an offset into the extra data.
internal static class Norm16Constants
{
    // Fixed norm16 values.
    public const int MinYesYesWithCc = 0xFE02;
    public const int JamoVt = 0xFE00;
    public const int MinNormalMaybeYes = 0xFC00;
    public const int JamoL = 2;
    public const int Inert = 1;

    public const int HasCompBoundaryAfter = 1;
    public const int OffsetShift = 1;

    // For algorithmic one-way mappings, bits 2..1 carry the tccc (0, 1, >1).
    public const int DeltaTccc0 = 0;
    public const int DeltaTccc1 = 2;
    public const int DeltaTcccGt1 = 4;
    public const int DeltaTcccMask = 6;
    public const int DeltaShift = 3;

    public const int MaxDelta = 0x40;

    // Mapping "first unit" flags.
    public const int MappingHasCccLcccWord = 0x80;
    public const int MappingHasRawMapping = 0x40;
    public const int MappingLengthMask = 0x1F;

    // Composition tuple encoding.
    public const int Comp1LastTuple = 0x8000;
    public const int Comp1Triple = 1;
    public const int Comp1TrailLimit = 0x3400;
    public const int Comp1TrailMask = 0x7FFE;
    public const int Comp1TrailShift = 9;
    public const int Comp2TrailShift = 6;
    public const int Comp2TrailMask = 0xFFC0;
}
