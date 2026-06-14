namespace NlsDataGenerator.IcuFormat;

// One (string, value) pair added to a UCharsTrieBuilder, ported from UCharsTrieElement
// (ucharstriebuilder.cpp). ICU packs the strings into a shared buffer with length prefixes; here
// each element just holds its string directly.
internal readonly struct UCharsTrieElement
{
    public UCharsTrieElement(string s, int value)
    {
        S = s;
        Value = value;
    }

    public string S { get; }

    public int Value { get; }
}
