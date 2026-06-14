using System.Text;
using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Collation;

// State of combining marks skipped during discontiguous-contraction matching, ported from
// collationiterator.cpp's SkippedState. Created on first need and kept around between uses. It buffers
// the marks read past a partial match so they can be replayed, and saves/restores the suffix-trie
// state for retrying after a skip.
internal sealed class SkippedCombiningMarks
{
    private readonly StringBuilder _oldBuffer = new();
    private readonly StringBuilder _newBuffer = new();
    private int _pos;
    private int _skipLengthAtMatch;
    private UCharsTrieMatcher.State _state;

    public void Clear()
    {
        _oldBuffer.Clear();
        _pos = 0;
    }

    public bool IsEmpty()
    {
        return _oldBuffer.Length == 0;
    }

    public bool HasNext()
    {
        return _pos < _oldBuffer.Length;
    }

    public int Next()
    {
        var c = char.ConvertToUtf32(_oldBuffer.ToString(), _pos);
        _pos += c > 0xFFFF ? 2 : 1;
        return c;
    }

    public void IncBeyond()
    {
        ++_pos;
    }

    public int BackwardNumCodePoints(int n)
    {
        var length = _oldBuffer.Length;
        var beyond = _pos - length;
        if (beyond > 0)
        {
            if (beyond >= n)
            {
                _pos -= n;
                return n;
            }
            _pos = MoveIndex32(_oldBuffer, length, beyond - n);
            return beyond;
        }
        _pos = MoveIndex32(_oldBuffer, _pos, -n);
        return 0;
    }

    public void SetFirstSkipped(int c)
    {
        _skipLengthAtMatch = 0;
        _newBuffer.Clear();
        _newBuffer.Append(char.ConvertFromUtf32(c));
    }

    public void Skip(int c)
    {
        _newBuffer.Append(char.ConvertFromUtf32(c));
    }

    public void RecordMatch()
    {
        _skipLengthAtMatch = _newBuffer.Length;
    }

    public void ReplaceMatch()
    {
        var pos = Math.Min(_pos, _oldBuffer.Length);
        _oldBuffer.Remove(0, pos);
        _oldBuffer.Insert(0, _newBuffer.ToString(0, _skipLengthAtMatch));
        _pos = 0;
    }

    public void SaveTrieState(UCharsTrieMatcher trie)
    {
        trie.SaveState(ref _state);
    }

    public void ResetToTrieState(UCharsTrieMatcher trie)
    {
        trie.ResetToState(_state);
    }

    private static int MoveIndex32(StringBuilder buffer, int index, int delta)
    {
        var s = buffer.ToString();
        if (delta >= 0)
        {
            while (delta > 0 && index < s.Length)
            {
                index += char.IsHighSurrogate(s[index]) ? 2 : 1;
                --delta;
            }
        }
        else
        {
            while (delta < 0 && index > 0)
            {
                --index;
                if (index > 0 && char.IsLowSurrogate(s[index]))
                {
                    --index;
                }
                ++delta;
            }
        }
        return index;
    }
}
