namespace NlsDataGenerator.Normalization;

// Canonical-ordering buffer used while reordering a mapping's combining marks, ported from ICU's
// BuilderReorderingBuffer (norms.h/.cpp). Each slot packs the code point in the high bits and its
// canonical combining class in the low byte; appending a character bubbles it back past any
// higher-ccc neighbours to restore canonical order.
internal sealed class ReorderingBuffer
{
    // Capacity is ICU's MAPPING_LENGTH_MASK (the largest a mapping may be).
    private const int MappingLengthMask = 0x1F;

    private readonly int[] _array = new int[MappingLengthMask];
    private int _length;
    private int _lastStarterIndex = -1;
    private bool _didReorder;

    public int Length => _length;

    public bool IsEmpty => _length == 0;

    public int LastStarterIndex => _lastStarterIndex;

    public bool DidReorder => _didReorder;

    public void Reset()
    {
        _length = 0;
        _lastStarterIndex = -1;
        _didReorder = false;
    }

    public int CharAt(int i)
    {
        return _array[i] >> 8;
    }

    public byte CcAt(int i)
    {
        return (byte)_array[i];
    }

    public void Append(int c, byte cc)
    {
        if (cc == 0 || _length == 0 || CcAt(_length - 1) <= cc)
        {
            if (cc == 0)
            {
                _lastStarterIndex = _length;
            }
            _array[_length++] = (c << 8) | cc;
            return;
        }

        // Let this character bubble back to its canonical order.
        var i = _length - 1;
        while (i > _lastStarterIndex && CcAt(i) > cc)
        {
            --i;
        }
        // After the last starter, or where prevCC <= cc.
        ++i;
        // Move this and the following characters forward one to make space.
        for (var j = _length; i < j; --j)
        {
            _array[j] = _array[j - 1];
        }
        _array[i] = (c << 8) | cc;
        ++_length;
        _didReorder = true;
    }

    public string ToMappingString()
    {
        var dest = new System.Text.StringBuilder(_length);
        for (var i = 0; i < _length; ++i)
        {
            dest.Append(char.ConvertFromUtf32(CharAt(i)));
        }
        return dest.ToString();
    }
}
