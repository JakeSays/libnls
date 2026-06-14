namespace NlsDataGenerator.IcuFormat;

// Builder for a UCharsTrie (char16 string trie), ported from UCharsTrieBuilder + the SMALL-build
// path of StringTrieBuilder. Collation uses this to compile each code point's contraction suffixes
// and prefixes into the serialized tries stored in the contexts array, so the output must match
// ICU exactly. Only the SMALL build is ported (the only one collation uses).
//
// ICU fills the output buffer back-to-front; here each written unit is appended to a list and the
// list is reversed at the end. Multi-unit writes are appended in reverse, so the running length
// (used for offsets and jump deltas) stays consistent with ICU's.
internal sealed class UCharsTrieBuilder
{
    private readonly List<UCharsTrieElement> _elements = [];
    private readonly List<char> _reversed = [];
    private readonly HashSet<TrieNode> _nodes = [];

    public int MinLinearMatch => UCharsTrieFormat.MinLinearMatch;

    public void Add(string s, int value)
    {
        _elements.Add(new UCharsTrieElement(s, value));
    }

    public void Clear()
    {
        _elements.Clear();
        _reversed.Clear();
        _nodes.Clear();
    }

    // Builds and char16-serializes the trie for the added (string, value) pairs (SMALL build).
    public string Build()
    {
        if (_elements.Count == 0)
        {
            throw new InvalidOperationException("UCharsTrie cannot be empty");
        }
        _elements.Sort(static (a, b) => string.CompareOrdinal(a.S, b.S));
        for (var i = 1; i < _elements.Count; ++i)
        {
            if (string.Equals(_elements[i - 1].S, _elements[i].S, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("duplicate UCharsTrie string");
            }
        }

        _reversed.Clear();
        _nodes.Clear();
        var root = MakeNode(0, _elements.Count, 0);
        root.MarkRightEdgesFirst(-1);
        root.Write(this);

        var n = _reversed.Count;
        var forward = new char[n];
        for (var i = 0; i < n; ++i)
        {
            forward[i] = _reversed[n - 1 - i];
        }
        return new string(forward);
    }

    // ---- node construction (StringTrieBuilder::makeNode / makeBranchSubNode) ----

    private TrieNode MakeNode(int start, int limit, int unitIndex)
    {
        var hasValue = false;
        var value = 0;
        if (unitIndex == GetElementStringLength(start))
        {
            // An intermediate or final value.
            value = GetElementValue(start++);
            if (start == limit)
            {
                return RegisterFinalValue(value);
            }
            hasValue = true;
        }
        TrieNode node;
        var minUnit = GetElementUnit(start, unitIndex);
        var maxUnit = GetElementUnit(limit - 1, unitIndex);
        if (minUnit == maxUnit)
        {
            // Linear-match node: all strings share the unit at unitIndex.
            var lastUnitIndex = GetLimitOfLinearMatch(start, limit - 1, unitIndex);
            var nextNode = MakeNode(start, limit, lastUnitIndex);
            var length = lastUnitIndex - unitIndex;
            var maxLinearMatchLength = UCharsTrieFormat.MaxLinearMatchLength;
            while (length > maxLinearMatchLength)
            {
                lastUnitIndex -= maxLinearMatchLength;
                length -= maxLinearMatchLength;
                node = CreateLinearMatchNode(start, lastUnitIndex, maxLinearMatchLength, nextNode);
                nextNode = RegisterNode(node);
            }
            node = CreateLinearMatchNode(start, unitIndex, length, nextNode);
        }
        else
        {
            var length = CountElementUnits(start, limit, unitIndex);
            var subNode = MakeBranchSubNode(start, limit, unitIndex, length);
            node = new BranchHeadNode(length, subNode);
        }
        if (hasValue)
        {
            ((ValueNode)node).SetValue(value);
        }
        return RegisterNode(node);
    }

    private TrieNode MakeBranchSubNode(int start, int limit, int unitIndex, int length)
    {
        var middleUnits = new char[UCharsTrieFormat.MaxSplitBranchLevels];
        var lessThan = new TrieNode[UCharsTrieFormat.MaxSplitBranchLevels];
        var ltLength = 0;
        while (length > UCharsTrieFormat.MaxBranchLinearSubNodeLength)
        {
            // Branch on the middle unit.
            var i = SkipElementsBySomeUnits(start, unitIndex, length / 2);
            middleUnits[ltLength] = GetElementUnit(i, unitIndex);
            lessThan[ltLength] = MakeBranchSubNode(start, i, unitIndex, length / 2);
            ++ltLength;
            start = i;
            length -= length / 2;
        }
        var listNode = new ListBranchNode();
        var unitNumber = 0;
        do
        {
            var i = start;
            var unit = GetElementUnit(i++, unitIndex);
            i = IndexOfElementWithNextUnit(i, unitIndex, unit);
            if (start == i - 1 && unitIndex + 1 == GetElementStringLength(start))
            {
                listNode.Add(unit, GetElementValue(start));
            }
            else
            {
                listNode.Add(unit, MakeNode(start, i, unitIndex + 1));
            }
            start = i;
        }
        while (++unitNumber < length - 1);
        // unitNumber == length-1; the maxUnit elements range is [start..limit[.
        var lastUnit = GetElementUnit(start, unitIndex);
        if (start == limit - 1 && unitIndex + 1 == GetElementStringLength(start))
        {
            listNode.Add(lastUnit, GetElementValue(start));
        }
        else
        {
            listNode.Add(lastUnit, MakeNode(start, limit, unitIndex + 1));
        }
        var node = RegisterNode(listNode);
        while (ltLength > 0)
        {
            --ltLength;
            node = RegisterNode(new SplitBranchNode(middleUnits[ltLength], lessThan[ltLength], node));
        }
        return node;
    }

    private TrieNode RegisterNode(TrieNode newNode)
    {
        if (_nodes.TryGetValue(newNode, out var existing))
        {
            return existing;
        }
        _nodes.Add(newNode);
        return newNode;
    }

    private TrieNode RegisterFinalValue(int value)
    {
        var key = new FinalValueNode(value);
        if (_nodes.TryGetValue(key, out var existing))
        {
            return existing;
        }
        _nodes.Add(key);
        return key;
    }

    private TrieNode CreateLinearMatchNode(int i, int unitIndex, int length, TrieNode next)
    {
        var units = _elements[i].S.Substring(unitIndex, length).ToCharArray();
        return new LinearMatchNode(units, length, next);
    }

    // ---- element accessors ----

    private int GetElementStringLength(int i)
    {
        return _elements[i].S.Length;
    }

    private char GetElementUnit(int i, int unitIndex)
    {
        return _elements[i].S[unitIndex];
    }

    private int GetElementValue(int i)
    {
        return _elements[i].Value;
    }

    private int GetLimitOfLinearMatch(int first, int last, int unitIndex)
    {
        var firstString = _elements[first].S;
        var lastString = _elements[last].S;
        var minStringLength = firstString.Length;
        while (++unitIndex < minStringLength && firstString[unitIndex] == lastString[unitIndex])
        {
        }
        return unitIndex;
    }

    private int CountElementUnits(int start, int limit, int unitIndex)
    {
        var length = 0;
        var i = start;
        do
        {
            var unit = _elements[i++].S[unitIndex];
            while (i < limit && unit == _elements[i].S[unitIndex])
            {
                ++i;
            }
            ++length;
        }
        while (i < limit);
        return length;
    }

    private int SkipElementsBySomeUnits(int i, int unitIndex, int count)
    {
        do
        {
            var unit = _elements[i++].S[unitIndex];
            while (unit == _elements[i].S[unitIndex])
            {
                ++i;
            }
        }
        while (--count > 0);
        return i;
    }

    private int IndexOfElementWithNextUnit(int i, int unitIndex, char unit)
    {
        while (unit == _elements[i].S[unitIndex])
        {
            ++i;
        }
        return i;
    }

    // ---- char16 serialization (UCharsTrieBuilder write methods) ----

    public int WriteUnit(int unit)
    {
        _reversed.Add((char)unit);
        return _reversed.Count;
    }

    public int WriteUnits(char[] s, int length)
    {
        for (var k = length - 1; k >= 0; --k)
        {
            _reversed.Add(s[k]);
        }
        return _reversed.Count;
    }

    public int WriteValueAndFinal(int i, bool isFinal)
    {
        var finalBit = isFinal ? UCharsTrieFormat.ValueIsFinal : 0;
        if (0 <= i && i <= UCharsTrieFormat.MaxOneUnitValue)
        {
            return WriteUnit(i | finalBit);
        }
        var intUnits = new char[3];
        int length;
        if (i < 0 || i > UCharsTrieFormat.MaxTwoUnitValue)
        {
            intUnits[0] = (char)UCharsTrieFormat.ThreeUnitValueLead;
            intUnits[1] = (char)((uint)i >> 16);
            intUnits[2] = (char)i;
            length = 3;
        }
        else
        {
            intUnits[0] = (char)(UCharsTrieFormat.MinTwoUnitValueLead + (i >> 16));
            intUnits[1] = (char)i;
            length = 2;
        }
        intUnits[0] = (char)(intUnits[0] | finalBit);
        return WriteUnits(intUnits, length);
    }

    public int WriteValueAndType(bool hasValue, int value, int node)
    {
        if (!hasValue)
        {
            return WriteUnit(node);
        }
        var intUnits = new char[3];
        int length;
        if (value < 0 || value > UCharsTrieFormat.MaxTwoUnitNodeValue)
        {
            intUnits[0] = (char)UCharsTrieFormat.ThreeUnitNodeValueLead;
            intUnits[1] = (char)((uint)value >> 16);
            intUnits[2] = (char)value;
            length = 3;
        }
        else if (value <= UCharsTrieFormat.MaxOneUnitNodeValue)
        {
            intUnits[0] = (char)((value + 1) << 6);
            length = 1;
        }
        else
        {
            intUnits[0] = (char)(UCharsTrieFormat.MinTwoUnitNodeValueLead + ((value >> 10) & 0x7FC0));
            intUnits[1] = (char)value;
            length = 2;
        }
        intUnits[0] = (char)(intUnits[0] | node);
        return WriteUnits(intUnits, length);
    }

    public int WriteDeltaTo(int jumpTarget)
    {
        var i = _reversed.Count - jumpTarget;
        if (i <= UCharsTrieFormat.MaxOneUnitDelta)
        {
            return WriteUnit(i);
        }
        var intUnits = new char[3];
        int length;
        if (i <= UCharsTrieFormat.MaxTwoUnitDelta)
        {
            intUnits[0] = (char)(UCharsTrieFormat.MinTwoUnitDeltaLead + (i >> 16));
            length = 1;
        }
        else
        {
            intUnits[0] = (char)UCharsTrieFormat.ThreeUnitDeltaLead;
            intUnits[1] = (char)(i >> 16);
            length = 2;
        }
        intUnits[length++] = (char)i;
        return WriteUnits(intUnits, length);
    }
}
