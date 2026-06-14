using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Case;

// Runs the full ucase build (value computation -> closure -> apply sensitive -> encode exceptions
// -> freeze trie) and assembles ucase.icu: the udata header, indexes[16], the serialized UTrie2,
// the exceptions array, and the unfold array.
internal sealed partial class CaseGenerator
{
    public byte[] Generate()
    {
        ComputeMainValues();
        MakeCaseClosure();

        // Greek final sigma is conditional but not locale-sensitive: it is taken when lowercasing
        // U+03A3 alone, so its lowercase result must be marked case-sensitive.
        _caseSensitive.Add(0x3C2);
        foreach (var codePoint in _caseSensitive)
        {
            var value = _trie.Get(codePoint);
            if ((value & Sensitive) == 0)
            {
                _trie.Set(codePoint, value | Sensitive);
            }
        }

        MakeExceptions();
        var trie = _trie.Freeze(Trie2ValueBits.Bits16);

        var indexes = new int[16];
        indexes[0] = indexes.Length;
        indexes[2] = trie.Length;
        indexes[3] = _exceptionWords.Count;
        indexes[4] = _unfold.Count;
        indexes[1] = 4 * indexes.Length + trie.Length + 2 * _exceptionWords.Count + 2 * _unfold.Count;
        indexes[15] = _maxFullLength;

        var writer = new LittleEndianWriter();
        new IcuDataHeader("cASE", [4, 0, 0, 0], [17, 0, 0, 0]).Write(writer);
        foreach (var index in indexes)
        {
            writer.WriteUInt32((uint)index);
        }
        writer.WriteBytes(trie);
        foreach (var word in _exceptionWords)
        {
            writer.WriteUInt16(word);
        }
        foreach (var word in _unfold)
        {
            writer.WriteUInt16(word);
        }

        return writer.ToArray();
    }
}
