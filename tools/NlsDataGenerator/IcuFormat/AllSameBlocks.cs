namespace NlsDataGenerator.IcuFormat;

// Fixed-capacity table of all-same-value data blocks for the UCPTrie compaction, tracking
// reference counts so FindMostUsed can pick the most common value for the shared null block.
// Ported from umutablecptrie.cpp.
internal sealed class AllSameBlocks
{
    public const int NewUnique = -1;
    public const int Overflow = -2;

    private const int Capacity = 32;
    private const int IndexLimit = Unicode.CodePointLimit >> 4;

    private readonly int[] _indexes = new int[Capacity];
    private readonly uint[] _values = new uint[Capacity];
    private readonly int[] _refCounts = new int[Capacity];
    private int _length;
    private int _mostRecent = -1;

    public int FindOrAdd(int index, int count, uint value)
    {
        if (_mostRecent >= 0 && _values[_mostRecent] == value)
        {
            _refCounts[_mostRecent] += count;
            return _indexes[_mostRecent];
        }

        for (var i = 0; i < _length; i++)
        {
            if (_values[i] == value)
            {
                _mostRecent = i;
                _refCounts[i] += count;
                return _indexes[i];
            }
        }

        if (_length == Capacity)
        {
            return Overflow;
        }

        _mostRecent = _length;
        _indexes[_length] = index;
        _values[_length] = value;
        _refCounts[_length++] = count;
        return NewUnique;
    }

    // Replaces the block with the lowest reference count. Called only when the table is full.
    public void Add(int index, int count, uint value)
    {
        var least = -1;
        var leastCount = IndexLimit;
        for (var i = 0; i < _length; i++)
        {
            if (_refCounts[i] < leastCount)
            {
                least = i;
                leastCount = _refCounts[i];
            }
        }

        _mostRecent = least;
        _indexes[least] = index;
        _values[least] = value;
        _refCounts[least] = count;
    }

    public int FindMostUsed()
    {
        if (_length == 0)
        {
            return -1;
        }

        var max = -1;
        var maxCount = 0;
        for (var i = 0; i < _length; i++)
        {
            if (_refCounts[i] > maxCount)
            {
                max = i;
                maxCount = _refCounts[i];
            }
        }
        return _indexes[max];
    }
}
