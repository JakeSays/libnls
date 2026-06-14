namespace NlsDataGenerator.ResourceBundle;

// The 4-bit resource type tags and the 32-bit resource-word helpers from ICU's uresdata.h. A
// Resource word packs the type in bits 31..28 and a 28-bit offset (or direct value) in bits 27..0;
// offsets are in 32-bit words for the byte-addressed data, or in 16-bit units for the v2 types.
internal static class ResourceType
{
    public const int String = 0;
    public const int Binary = 1;
    public const int Table = 2;
    public const int Table32 = 4;
    public const int Table16 = 5;
    public const int StringV2 = 6;
    public const int Int = 7;
    public const int Array = 8;
    public const int Array16 = 9;
    public const int IntVector = 14;

    public const uint Bogus = 0xFFFFFFFF;
    public const int MaxOffset = 0x0FFFFFFF;

    public static uint MakeResource(int type, int offset)
    {
        return ((uint)type << 28) | (uint)offset;
    }

    public static uint MakeEmptyResource(int type)
    {
        return (uint)type << 28;
    }

    public static int GetType(uint resource)
    {
        return (int)(resource >> 28);
    }

    public static int GetOffset(uint resource)
    {
        return (int)(resource & 0x0FFFFFFF);
    }
}
