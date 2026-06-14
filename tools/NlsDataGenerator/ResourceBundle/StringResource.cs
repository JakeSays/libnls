using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.ResourceBundle;

// A Unicode string value, ported from genrb's StringResource (the formatVersion 2 "String-v2"
// form). Strings live in the bundle's shared 16-bit-units pool: genrb deduplicates equal strings
// and lets a short string point into the suffix of a longer one, so a StringResource carries the
// bookkeeping for that sharing (its canonical twin, suffix offset, and copy count).
internal sealed class StringResource : Resource
{
    public string Value { get; }

    // How many leading 16-bit units encode this string's explicit length: 0 when the length is
    // implicit (short string, runtime calls strlen), otherwise 1..3.
    public int CharsForLength { get; private set; }

    // When this string duplicates or is a suffix of another, the canonical string it shares.
    public StringResource? Same { get; set; }

    // For a suffix string, the offset within Same's characters where this string begins.
    public int SuffixOffset { get; set; }

    // Number of references to the canonical string (itself plus its duplicates).
    public int Copies { get; set; }

    // 16-bit units saved by sharing this string and its suffixes; drives compaction sort order.
    public int UnitsSaved { get; set; }

    public StringResource(int key, string value)
        : base(key)
    {
        Value = value;
    }

    public int Length => Value.Length;

    // The number of 16-bit units this string occupies on its own: length prefix, characters, NUL.
    public int SixteenBitLength => CharsForLength + Length + 1;

    protected override void HandlePreflightStrings(ResourceBundleWriter bundle)
    {
        Same = bundle.FindString(Value);
        if (Same != null)
        {
            ++Same.Copies;
            return;
        }
        Copies = 1;
        bundle.AddString(Value, this);

        var length = Length;
        if (length <= MaxImplicitStringLength
            && (length == 0 || !IsTrailSurrogate(Value[0]))
            && Value.IndexOf('\0') < 0)
        {
            // Stored without an explicit length; the runtime detects the non-trail lead unit.
            CharsForLength = 0;
        }
        else if (length <= 0x3ee)
        {
            CharsForLength = 1;
        }
        else if (length <= 0xfffff)
        {
            CharsForLength = 2;
        }
        else
        {
            CharsForLength = 3;
        }
        bundle.AddStringUnits(CharsForLength + length + 1);
    }

    protected override void HandleWrite16(ResourceBundleWriter bundle)
    {
        // Only reached for a duplicate; share the canonical string's resource word.
        if (Same != null)
        {
            Res = Same.Res;
            Written = Same.Written;
        }
    }

    // Appends this string's 16-bit units to the pool and records its resource word. The offset is
    // measured from the pool start; base accounts for shared pool-bundle strings (always 0 here).
    public void WriteUtf16V2(int baseOffset, List<ushort> pool)
    {
        var length = Length;
        Res = ResourceType.MakeResource(ResourceType.StringV2, baseOffset + pool.Count);
        Written = true;
        switch (CharsForLength)
        {
            case 0:
                break;
            case 1:
                pool.Add((ushort)(0xdc00 + length));
                break;
            case 2:
                pool.Add((ushort)(0xdfef + (length >> 16)));
                pool.Add((ushort)length);
                break;
            case 3:
                pool.Add(0xdfff);
                pool.Add((ushort)(length >> 16));
                pool.Add((ushort)length);
                break;
        }
        foreach (var unit in Value)
        {
            pool.Add(unit);
        }
        pool.Add(0);
    }

    protected override void HandlePreWrite(ref uint byteOffset)
    {
        // String-v2 values are emitted into the 16-bit pool, so Res is already set and PreWrite
        // returns early; this is unreachable for the formatVersion 2 form.
        throw new InvalidOperationException("String-v2 resources are written into the 16-bit pool");
    }

    protected override void HandleWrite(LittleEndianWriter writer, ref uint byteOffset)
    {
        throw new InvalidOperationException("String-v2 resources are written into the 16-bit pool");
    }

    // The longest string stored without an explicit length prefix.
    private const int MaxImplicitStringLength = 40;

    private static bool IsTrailSurrogate(char c)
    {
        return c >= 0xDC00 && c <= 0xDFFF;
    }
}
