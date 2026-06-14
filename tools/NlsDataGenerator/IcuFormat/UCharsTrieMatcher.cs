namespace NlsDataGenerator.IcuFormat;

// Runtime navigator over a serialized UCharsTrie, ported from ICU's UCharsTrie matcher
// (ucharstrie.cpp). The collation engine uses it to match contraction suffixes and prefix contexts:
// firstForCodePoint/nextForCodePoint advance one code point at a time, getValue reads the value at a
// matched node, and saveState/resetToState support discontiguous-contraction backtracking. The trie
// data is a slice of a char[] (the collation contexts array) starting at a given root offset.
internal sealed class UCharsTrieMatcher
{
    // UStringTrieResult.
    public const int NoMatch = 0;
    public const int NoValue = 1;
    public const int FinalValue = 2;
    public const int IntermediateValue = 3;

    public static bool HasValue(int result)
    {
        return result >= FinalValue;
    }

    public static bool HasNext(int result)
    {
        return (result & 1) != 0;
    }

    public struct State
    {
        public int Pos;
        public int RemainingMatchLength;
    }

    private readonly char[] _uchars;
    private readonly int _root;
    private int _pos;
    private int _remainingMatchLength;

    public UCharsTrieMatcher(char[] uchars, int root)
    {
        _uchars = uchars;
        _root = root;
        _pos = root;
        _remainingMatchLength = -1;
    }

    public void Reset()
    {
        _pos = _root;
        _remainingMatchLength = -1;
    }

    public void SaveState(ref State state)
    {
        state.Pos = _pos;
        state.RemainingMatchLength = _remainingMatchLength;
    }

    public void ResetToState(State state)
    {
        _pos = state.Pos;
        _remainingMatchLength = state.RemainingMatchLength;
    }

    public int Current()
    {
        if (_pos < 0)
        {
            return NoMatch;
        }
        int node = _uchars[_pos];
        return _remainingMatchLength < 0 && node >= UCharsTrieFormat.MinValueLead
            ? ValueResult(node)
            : NoValue;
    }

    public int GetValue()
    {
        var pos = _pos;
        int leadUnit = _uchars[pos++];
        return (leadUnit & UCharsTrieFormat.ValueIsFinal) != 0
            ? ReadValue(pos, leadUnit & 0x7fff)
            : ReadNodeValue(pos, leadUnit);
    }

    public int FirstForCodePoint(int cp)
    {
        if (cp <= 0xffff)
        {
            return First(cp);
        }
        return HasNext(First(Lead(cp))) ? Next(Trail(cp)) : NoMatch;
    }

    public int NextForCodePoint(int cp)
    {
        if (cp <= 0xffff)
        {
            return Next(cp);
        }
        return HasNext(Next(Lead(cp))) ? Next(Trail(cp)) : NoMatch;
    }

    private int First(int unit)
    {
        _remainingMatchLength = -1;
        return NextImpl(_root, unit);
    }

    private int Next(int unit)
    {
        var pos = _pos;
        if (pos < 0)
        {
            return NoMatch;
        }
        var length = _remainingMatchLength;
        if (length >= 0)
        {
            // Remaining part of a linear-match node.
            if (unit == _uchars[pos++])
            {
                _remainingMatchLength = --length;
                _pos = pos;
                int node;
                return length < 0 && (node = _uchars[pos]) >= UCharsTrieFormat.MinValueLead
                    ? ValueResult(node)
                    : NoValue;
            }
            Stop();
            return NoMatch;
        }
        return NextImpl(pos, unit);
    }

    private int NextImpl(int pos, int unit)
    {
        int node = _uchars[pos++];
        for (;;)
        {
            if (node < UCharsTrieFormat.MinLinearMatch)
            {
                return BranchNext(pos, node, unit);
            }
            if (node < UCharsTrieFormat.MinValueLead)
            {
                // Match the first of length+1 units.
                var length = node - UCharsTrieFormat.MinLinearMatch;
                if (unit == _uchars[pos++])
                {
                    _remainingMatchLength = --length;
                    _pos = pos;
                    return length < 0 && (node = _uchars[pos]) >= UCharsTrieFormat.MinValueLead
                        ? ValueResult(node)
                        : NoValue;
                }
                break;
            }
            if ((node & UCharsTrieFormat.ValueIsFinal) != 0)
            {
                break;
            }
            pos = SkipNodeValue(pos, node);
            node &= UCharsTrieFormat.NodeTypeMask;
        }
        Stop();
        return NoMatch;
    }

    private int BranchNext(int pos, int length, int unit)
    {
        if (length == 0)
        {
            length = _uchars[pos++];
        }
        ++length;
        while (length > UCharsTrieFormat.MaxBranchLinearSubNodeLength)
        {
            if (unit < _uchars[pos++])
            {
                length >>= 1;
                pos = JumpByDelta(pos);
            }
            else
            {
                length = length - (length >> 1);
                pos = SkipDelta(pos);
            }
        }
        do
        {
            if (unit == _uchars[pos++])
            {
                int result;
                int node = _uchars[pos];
                if ((node & UCharsTrieFormat.ValueIsFinal) != 0)
                {
                    result = FinalValue;
                }
                else
                {
                    ++pos;
                    int delta;
                    if (node < UCharsTrieFormat.MinTwoUnitValueLead)
                    {
                        delta = node;
                    }
                    else if (node < UCharsTrieFormat.ThreeUnitValueLead)
                    {
                        delta = ((node - UCharsTrieFormat.MinTwoUnitValueLead) << 16) | _uchars[pos++];
                    }
                    else
                    {
                        delta = (_uchars[pos] << 16) | _uchars[pos + 1];
                        pos += 2;
                    }
                    pos += delta;
                    node = _uchars[pos];
                    result = node >= UCharsTrieFormat.MinValueLead ? ValueResult(node) : NoValue;
                }
                _pos = pos;
                return result;
            }
            --length;
            pos = SkipValue(pos);
        }
        while (length > 1);
        if (unit == _uchars[pos++])
        {
            _pos = pos;
            int node = _uchars[pos];
            return node >= UCharsTrieFormat.MinValueLead ? ValueResult(node) : NoValue;
        }
        Stop();
        return NoMatch;
    }

    private void Stop()
    {
        _pos = -1;
    }

    private static int ValueResult(int node)
    {
        return IntermediateValue - ((node >> 15) & 1);
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

    private int SkipValue(int pos)
    {
        int leadUnit = _uchars[pos++];
        return SkipValue(pos, leadUnit & 0x7fff);
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
            return (((leadUnit & 0x7fc0) - UCharsTrieFormat.MinTwoUnitNodeValueLead) << 10) | _uchars[pos];
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

    private static int Lead(int cp)
    {
        return 0xD7C0 + (cp >> 10);
    }

    private static int Trail(int cp)
    {
        return 0xDC00 + (cp & 0x3FF);
    }
}
