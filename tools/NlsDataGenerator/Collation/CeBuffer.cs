namespace NlsDataGenerator.Collation;

// A growable buffer of 64-bit collation elements, ported from CollationIterator::CEBuffer. The engine
// appends CEs as it expands CE32s; random-access set/get support in-place rewriting (e.g. the
// long-primary fast path in nextCE).
internal sealed class CeBuffer
{
    private long[] _buffer = new long[40];

    public int Length;

    public bool EnsureAppendCapacity(int additional)
    {
        if (Length + additional <= _buffer.Length)
        {
            return true;
        }
        var capacity = _buffer.Length;
        do
        {
            capacity = capacity < 1000 ? capacity * 4 : capacity * 2;
        }
        while (capacity < Length + additional);
        Array.Resize(ref _buffer, capacity);
        return true;
    }

    public void Append(long ce)
    {
        EnsureAppendCapacity(1);
        _buffer[Length++] = ce;
    }

    public void AppendUnsafe(long ce)
    {
        _buffer[Length++] = ce;
    }

    public long Set(int i, long ce)
    {
        _buffer[i] = ce;
        return ce;
    }

    public long Get(int i)
    {
        return _buffer[i];
    }
}
