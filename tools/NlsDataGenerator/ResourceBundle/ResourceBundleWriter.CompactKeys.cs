namespace NlsDataGenerator.ResourceBundle;

// compactKeys(), ported from genrb. Key strings are appended to the pool with duplicates during
// tree construction; this collapses them. It drops keys no resource references, then sorts the
// survivors so that each key is immediately followed by all of its suffixes, makes each suffix point
// into the earlier longer key that contains it (marking its bytes deleted), squeezes the deleted
// bytes out, and records the old-offset -> new-offset map that the write16 pass uses.
internal sealed partial class ResourceBundleWriter
{
    private void CompactKeys()
    {
        var keysInUse = new HashSet<int>();
        _root.CollectKeys(key =>
        {
            if (key >= 0)
            {
                keysInUse.Add(key);
            }
        });

        var keysCount = keysInUse.Count;
        var map = new KeyMapEntry[keysCount];
        var i = 0;
        var pos = KeysBottom;
        while (i < keysCount)
        {
            var keyOffset = pos;
            var end = pos;
            while (_keys[end] != 0)
            {
                ++end;
            }
            if (!keysInUse.Contains(keyOffset))
            {
                // An unused key: mark its bytes (and NUL) deleted and skip it.
                for (var p = keyOffset; p <= end; ++p)
                {
                    _keys[p] = 1;
                }
            }
            else
            {
                map[i].OldPos = keyOffset;
                map[i].NewPos = 0;
                ++i;
            }
            pos = end + 1;
        }
        // Discard any unused keys trailing the last used one.
        if (pos != _keysTop)
        {
            _keysTop = pos;
        }

        Array.Sort(map, CompareKeySuffixes);

        for (var a = 0; a < keysCount;)
        {
            map[a].NewPos = map[a].OldPos;
            var key = map[a].OldPos;
            var keyEnd = key;
            while (_keys[keyEnd] != 0)
            {
                ++keyEnd;
            }
            var keyLength = keyEnd - key;

            var b = a + 1;
            for (; b < keysCount; ++b)
            {
                var suffix = map[b].OldPos;
                var suffixEnd = suffix;
                while (_keys[suffixEnd] != 0)
                {
                    ++suffixEnd;
                }
                var suffixLength = suffixEnd - suffix;
                var offset = keyLength - suffixLength;
                if (offset < 0)
                {
                    // A suffix cannot be longer than the key it would point into.
                    break;
                }
                if (!IsSuffixOf(key, offset, suffix, suffixLength))
                {
                    break;
                }
                // Point this suffix into the earlier key and mark its own bytes deleted.
                map[b].NewPos = map[a].OldPos + offset;
                for (var p = suffix; p <= suffixEnd; ++p)
                {
                    _keys[p] = 1;
                }
            }
            a = b;
        }

        Array.Sort(map, (x, y) => CompareInt32(x.NewPos, y.NewPos));

        var oldPosition = KeysBottom;
        var newPosition = KeysBottom;
        var limit = _keysTop;
        var index = 0;
        while (index < keysCount && map[index].NewPos < 0)
        {
            ++index;
        }
        if (index < keysCount)
        {
            while (oldPosition < limit)
            {
                if (_keys[oldPosition] == 1)
                {
                    ++oldPosition;
                }
                else
                {
                    while (index < keysCount && map[index].NewPos == oldPosition)
                    {
                        map[index++].NewPos = newPosition;
                    }
                    _keys[newPosition++] = _keys[oldPosition++];
                }
            }
        }
        _keysTop = newPosition;

        Array.Sort(map, (x, y) => CompareInt32(x.OldPos, y.OldPos));
        _keyMap = new Dictionary<int, int>(keysCount);
        foreach (var entry in map)
        {
            _keyMap[entry.OldPos] = entry.NewPos;
        }
    }

    // Whether the key string starting at suffix (suffixLength bytes) equals the tail of the key
    // string at key starting at key+offset.
    private bool IsSuffixOf(int key, int offset, int suffix, int suffixLength)
    {
        for (var t = 0; t < suffixLength; ++t)
        {
            if (_keys[key + offset + t] != _keys[suffix + t])
            {
                return false;
            }
        }
        return true;
    }

    // Sorts keys into reverse-character order so each key is followed by its suffixes; equal suffixes
    // come longest-first, and identical keys in ascending original-offset (parsing) order.
    private int CompareKeySuffixes(KeyMapEntry left, KeyMapEntry right)
    {
        var leftStart = left.OldPos;
        var leftPos = leftStart;
        while (_keys[leftPos] != 0)
        {
            ++leftPos;
        }
        var rightStart = right.OldPos;
        var rightPos = rightStart;
        while (_keys[rightPos] != 0)
        {
            ++rightPos;
        }
        while (leftPos > leftStart && rightPos > rightStart)
        {
            var diff = _keys[--leftPos] - _keys[--rightPos];
            if (diff != 0)
            {
                return diff;
            }
        }
        var lengthDiff = (rightPos - rightStart) - (leftPos - leftStart);
        if (lengthDiff != 0)
        {
            return lengthDiff;
        }
        return CompareInt32(leftStart, rightStart);
    }

    private static int CompareInt32(int left, int right)
    {
        if (left < right)
        {
            return -1;
        }
        if (left > right)
        {
            return 1;
        }
        return 0;
    }
}
