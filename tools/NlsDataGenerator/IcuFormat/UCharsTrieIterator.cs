using System.Text;

namespace NlsDataGenerator.IcuFormat;

// Reader that enumerates every (string, value) pair in a serialized UCharsTrie, ported from
// UCharsTrie::Iterator (ucharstrieiterator.cpp) plus the read helpers in ucharstrie.h. The fast-
// Latin builder uses this to walk a contraction's suffixes out of the collation contexts array.
// Positions index into the units array; a negative position is ICU's null "stop" pointer.
// Constructed from a trie root offset; maxLength 0 means unbounded (the only mode collation uses).
internal sealed class UCharsTrieIterator
{
    private readonly char[] _uchars;
    private readonly int _initialPos;
    private readonly int _maxLength;
    private readonly StringBuilder _str = new();
    private readonly List<int> _stack = [];

    private int _pos;
    private int _remainingMatchLength;
    private bool _skipValue;
    private int _value;

    public UCharsTrieIterator(char[] uchars, int startOffset, int maxLength)
    {
        _uchars = uchars;
        _pos = startOffset;
        _initialPos = startOffset;
        _remainingMatchLength = -1;
        _maxLength = maxLength;
    }

    public string Str => _str.ToString();

    public int Value => _value;

    public void Reset()
    {
        _pos = _initialPos;
        _remainingMatchLength = -1;
        _skipValue = false;
        _str.Clear();
        _stack.Clear();
    }

    public bool Next()
    {
        var pos = _pos;
        if (pos < 0)
        {
            if (_stack.Count == 0)
            {
                return false;
            }
            // Pop the state and continue with the next outbound edge of a branch node.
            var stackSize = _stack.Count;
            var length = _stack[stackSize - 1];
            pos = _stack[stackSize - 2];
            _stack.RemoveRange(stackSize - 2, 2);
            _str.Length = length & 0xFFFF;
            length = (int)((uint)length >> 16);
            if (length > 1)
            {
                pos = BranchNext(pos, length);
                if (pos < 0)
                {
                    return true; // reached a final value
                }
            }
            else
            {
                _str.Append(_uchars[pos++]);
            }
        }
        if (_remainingMatchLength >= 0)
        {
            return TruncateAndStop();
        }
        for (;;)
        {
            int node = _uchars[pos++];
            if (node >= UCharsTrieFormat.MinValueLead)
            {
                if (_skipValue)
                {
                    pos = SkipNodeValue(pos, node);
                    node &= UCharsTrieFormat.NodeTypeMask;
                    _skipValue = false;
                }
                else
                {
                    var isFinal = (node >> 15) != 0;
                    _value = isFinal ? ReadValue(pos, node & 0x7FFF) : ReadNodeValue(pos, node);
                    if (isFinal || (_maxLength > 0 && _str.Length == _maxLength))
                    {
                        _pos = -1;
                    }
                    else
                    {
                        // Keep pos on the node lead unit; its match node is evaluated next time.
                        _pos = pos - 1;
                        _skipValue = true;
                    }
                    return true;
                }
            }
            if (_maxLength > 0 && _str.Length == _maxLength)
            {
                return TruncateAndStop();
            }
            if (node < UCharsTrieFormat.MinLinearMatch)
            {
                if (node == 0)
                {
                    node = _uchars[pos++];
                }
                pos = BranchNext(pos, node + 1);
                if (pos < 0)
                {
                    return true; // reached a final value
                }
            }
            else
            {
                // Linear-match node: append its units.
                var length = node - UCharsTrieFormat.MinLinearMatch + 1;
                if (_maxLength > 0 && _str.Length + length > _maxLength)
                {
                    _str.Append(_uchars, pos, _maxLength - _str.Length);
                    return TruncateAndStop();
                }
                _str.Append(_uchars, pos, length);
                pos += length;
            }
        }
    }

    private bool TruncateAndStop()
    {
        _pos = -1;
        _value = -1;
        return true;
    }

    // Takes the first outbound edge of a branch and pushes state for the rest.
    private int BranchNext(int pos, int length)
    {
        while (length > UCharsTrieFormat.MaxBranchLinearSubNodeLength)
        {
            ++pos; // ignore the comparison unit
            _stack.Add(SkipDelta(pos));
            _stack.Add(((length - (length >> 1)) << 16) | _str.Length);
            length >>= 1;
            pos = JumpByDelta(pos);
        }
        var trieUnit = _uchars[pos++];
        int node = _uchars[pos++];
        var isFinal = (node >> 15) != 0;
        node &= 0x7FFF;
        var value = ReadValue(pos, node);
        pos = SkipValue(pos, node);
        _stack.Add(pos);
        _stack.Add(((length - 1) << 16) | _str.Length);
        _str.Append(trieUnit);
        if (isFinal)
        {
            _pos = -1;
            _value = value;
            return -1;
        }
        return pos + value;
    }

    private int ReadValue(int pos, int leadUnit)
    {
        if (leadUnit < UCharsTrieFormat.MinTwoUnitValueLead)
        {
            return leadUnit;
        }
        if (leadUnit < UCharsTrieFormat.ThreeUnitValueLead)
        {
            return ((leadUnit - UCharsTrieFormat.MinTwoUnitValueLead) << 16) | _uchars[pos];
        }
        return (_uchars[pos] << 16) | _uchars[pos + 1];
    }

    private static int SkipValue(int pos, int leadUnit)
    {
        if (leadUnit >= UCharsTrieFormat.MinTwoUnitValueLead)
        {
            pos += leadUnit < UCharsTrieFormat.ThreeUnitValueLead ? 1 : 2;
        }
        return pos;
    }

    private int ReadNodeValue(int pos, int leadUnit)
    {
        if (leadUnit < UCharsTrieFormat.MinTwoUnitNodeValueLead)
        {
            return (leadUnit >> 6) - 1;
        }
        if (leadUnit < UCharsTrieFormat.ThreeUnitNodeValueLead)
        {
            return (((leadUnit & 0x7FC0) - UCharsTrieFormat.MinTwoUnitNodeValueLead) << 10) | _uchars[pos];
        }
        return (_uchars[pos] << 16) | _uchars[pos + 1];
    }

    private static int SkipNodeValue(int pos, int leadUnit)
    {
        if (leadUnit >= UCharsTrieFormat.MinTwoUnitNodeValueLead)
        {
            pos += leadUnit < UCharsTrieFormat.ThreeUnitNodeValueLead ? 1 : 2;
        }
        return pos;
    }

    private int JumpByDelta(int pos)
    {
        int delta = _uchars[pos++];
        if (delta >= UCharsTrieFormat.MinTwoUnitDeltaLead)
        {
            if (delta == UCharsTrieFormat.ThreeUnitDeltaLead)
            {
                delta = (_uchars[pos] << 16) | _uchars[pos + 1];
                pos += 2;
            }
            else
            {
                delta = ((delta - UCharsTrieFormat.MinTwoUnitDeltaLead) << 16) | _uchars[pos++];
            }
        }
        return pos + delta;
    }

    private int SkipDelta(int pos)
    {
        int delta = _uchars[pos++];
        if (delta >= UCharsTrieFormat.MinTwoUnitDeltaLead)
        {
            pos += delta == UCharsTrieFormat.ThreeUnitDeltaLead ? 2 : 1;
        }
        return pos;
    }
}
