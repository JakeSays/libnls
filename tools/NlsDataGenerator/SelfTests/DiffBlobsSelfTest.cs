namespace NlsDataGenerator.SelfTests;

// Diffs two raw %%CollationBin blobs at the CE64 level. Reads the CES section of each (between the
// IX_CES_OFFSET and IX_RESERVED10_OFFSET index entries) and lists the 64-bit CEs unique to each
// side, to pin down which expansions one build produces that the other does not.
internal static class DiffBlobsSelfTest
{
    public static int Run(string minePath, string referencePath)
    {
        var mine = File.ReadAllBytes(minePath);
        var reference = File.ReadAllBytes(referencePath);
        var mineCes = ReadCes(mine);
        var refCes = ReadCes(reference);
        Console.WriteLine($"mine ces={mineCes.Count} ref ces={refCes.Count}");

        // Longest-common-subsequence-free simple report: list CEs present in ref but not mine (and
        // vice versa) by value+index, so a shifted-but-equal run shows as empty.
        Console.WriteLine("-- CES by index (i: mine | ref) where they differ --");
        var max = Math.Max(mineCes.Count, refCes.Count);
        var shown = 0;
        for (var i = 0; i < max && shown < 60; ++i)
        {
            var m = i < mineCes.Count ? mineCes[i].ToString("x16") : "----------------";
            var r = i < refCes.Count ? refCes[i].ToString("x16") : "----------------";
            if (m != r)
            {
                Console.WriteLine($"  {i}: {m} | {r}");
                ++shown;
            }
        }

        // Multiset difference: CEs in ref not accounted for by mine (ignores order).
        var mineCounts = Counts(mineCes);
        var refCounts = Counts(refCes);
        Console.WriteLine("-- in REF but not MINE (value: refCount-mineCount) --");
        foreach (var (value, count) in refCounts.OrderBy(p => p.Key))
        {
            mineCounts.TryGetValue(value, out var mc);
            if (count > mc)
            {
                Console.WriteLine($"  {value:x16}: +{count - mc}  (primary={(uint)(value >> 32):x8} sec={(ushort)(value >> 16):x4} ter={(ushort)value:x4})");
            }
        }
        Console.WriteLine("-- in MINE but not REF --");
        foreach (var (value, count) in mineCounts.OrderBy(p => p.Key))
        {
            refCounts.TryGetValue(value, out var rc);
            if (count > rc)
            {
                Console.WriteLine($"  {value:x16}: +{count - rc}");
            }
        }
        return 0;
    }

    private static Dictionary<ulong, int> Counts(List<ulong> ces)
    {
        var result = new Dictionary<ulong, int>();
        foreach (var ce in ces)
        {
            result.TryGetValue(ce, out var c);
            result[ce] = c + 1;
        }
        return result;
    }

    private static List<ulong> ReadCes(byte[] blob)
    {
        var headerSize = blob[0] | (blob[1] << 8);
        int Index(int i)
        {
            var o = headerSize + i * 4;
            return blob[o] | (blob[o + 1] << 8) | (blob[o + 2] << 16) | (blob[o + 3] << 24);
        }
        // Index offsets are measured from the index-array start (== headerSize). The CE64 array lives
        // between IX_CES_OFFSET (9) and IX_RESERVED10_OFFSET (10, == ce32s start with no reserved gap).
        var start = headerSize + Index(9);
        var end = headerSize + Index(10);
        var result = new List<ulong>();
        for (var o = start; o + 8 <= end; o += 8)
        {
            ulong value = 0;
            for (var b = 0; b < 8; ++b)
            {
                value |= (ulong)blob[o + b] << (b * 8);
            }
            result.Add(value);
        }
        return result;
    }
}
