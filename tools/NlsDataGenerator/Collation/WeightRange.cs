namespace NlsDataGenerator.Collation;

// A contiguous run of collation weights of one byte-length, ported from CollationWeights::WeightRange.
// allocWeights() fills an array of these; nextWeight() walks them, mutating start/count as it goes.
internal struct WeightRange
{
    public uint Start;
    public uint End;
    public int Length;
    public int Count;
}
