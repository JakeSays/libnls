namespace NlsDataGenerator.Collation;

// Allocates n collation-element weights strictly between two limits at one level, ported from ICU's
// CollationWeights (collationweights.cpp). The tailoring builder uses it to assign primary, secondary,
// and tertiary weights to the CEs it inserts between two adjacent root CEs. A weight is a 4-byte big-
// endian value; allocWeights() computes a set of WeightRanges covering the gap and nextWeight() then
// hands out the individual weights in order.
internal sealed class CollationWeights
{
    private int _middleLength;
    private readonly uint[] _minBytes = new uint[5];
    private readonly uint[] _maxBytes = new uint[5];
    private readonly WeightRange[] _ranges = new WeightRange[7];
    private int _rangeIndex;
    private int _rangeCount;

    public static int LengthOfWeight(uint weight)
    {
        if ((weight & 0xffffff) == 0)
        {
            return 1;
        }
        if ((weight & 0xffff) == 0)
        {
            return 2;
        }
        if ((weight & 0xff) == 0)
        {
            return 3;
        }
        return 4;
    }

    public void InitForPrimary(bool compressible)
    {
        _middleLength = 1;
        _minBytes[1] = (uint)Collator.MergeSeparatorByte + 1;
        _maxBytes[1] = Collator.TrailWeightByte;
        if (compressible)
        {
            _minBytes[2] = (uint)Collator.PrimaryCompressionLowByte + 1;
            _maxBytes[2] = (uint)Collator.PrimaryCompressionHighByte - 1;
        }
        else
        {
            _minBytes[2] = 2;
            _maxBytes[2] = 0xff;
        }
        _minBytes[3] = 2;
        _maxBytes[3] = 0xff;
        _minBytes[4] = 2;
        _maxBytes[4] = 0xff;
    }

    public void InitForSecondary()
    {
        // Only the lower 16 bits are used for secondary weights.
        _middleLength = 3;
        _minBytes[1] = 0;
        _maxBytes[1] = 0;
        _minBytes[2] = 0;
        _maxBytes[2] = 0;
        _minBytes[3] = (uint)Collator.LevelSeparatorByte + 1;
        _maxBytes[3] = 0xff;
        _minBytes[4] = 2;
        _maxBytes[4] = 0xff;
    }

    public void InitForTertiary()
    {
        // Only the lower 16 bits are used for tertiary weights, and only 6 bits per byte; the other
        // bits carry case and quaternary weights.
        _middleLength = 3;
        _minBytes[1] = 0;
        _maxBytes[1] = 0;
        _minBytes[2] = 0;
        _maxBytes[2] = 0;
        _minBytes[3] = (uint)Collator.LevelSeparatorByte + 1;
        _maxBytes[3] = 0x3f;
        _minBytes[4] = 2;
        _maxBytes[4] = 0x3f;
    }

    public bool AllocWeights(uint lowerLimit, uint upperLimit, int n)
    {
        if (!GetWeightRanges(lowerLimit, upperLimit))
        {
            return false;
        }

        for (;;)
        {
            var minLength = _ranges[0].Length;
            if (AllocWeightsInShortRanges(n, minLength))
            {
                break;
            }
            if (minLength == 4)
            {
                return false;
            }
            if (AllocWeightsInMinLengthRanges(n, minLength))
            {
                break;
            }
            // No good match: lengthen all minLength ranges and iterate.
            for (var i = 0; i < _rangeCount && _ranges[i].Length == minLength; ++i)
            {
                LengthenRange(ref _ranges[i]);
            }
        }

        _rangeIndex = 0;
        return true;
    }

    public uint NextWeight()
    {
        if (_rangeIndex >= _rangeCount)
        {
            return 0xffffffff;
        }
        var weight = _ranges[_rangeIndex].Start;
        if (--_ranges[_rangeIndex].Count == 0)
        {
            ++_rangeIndex;
        }
        else
        {
            _ranges[_rangeIndex].Start = IncWeight(weight, _ranges[_rangeIndex].Length);
        }
        return weight;
    }

    private int CountBytes(int index)
    {
        return (int)(_maxBytes[index] - _minBytes[index] + 1);
    }

    private uint IncWeight(uint weight, int length)
    {
        for (;;)
        {
            var b = GetWeightByte(weight, length);
            if (b < _maxBytes[length])
            {
                return SetWeightByte(weight, length, b + 1);
            }
            // Roll over: set this byte to the minimum and increment the previous one.
            weight = SetWeightByte(weight, length, _minBytes[length]);
            --length;
        }
    }

    private uint IncWeightByOffset(uint weight, int length, int offset)
    {
        for (;;)
        {
            offset += (int)GetWeightByte(weight, length);
            if ((uint)offset <= _maxBytes[length])
            {
                return SetWeightByte(weight, length, (uint)offset);
            }
            // Split the offset between this byte and the previous one.
            offset -= (int)_minBytes[length];
            weight = SetWeightByte(weight, length, _minBytes[length] + (uint)(offset % CountBytes(length)));
            offset /= CountBytes(length);
            --length;
        }
    }

    private void LengthenRange(ref WeightRange range)
    {
        var length = range.Length + 1;
        range.Start = SetWeightTrail(range.Start, length, _minBytes[length]);
        range.End = SetWeightTrail(range.End, length, _maxBytes[length]);
        range.Count *= CountBytes(length);
        range.Length = length;
    }

    private bool GetWeightRanges(uint lowerLimit, uint upperLimit)
    {
        var lowerLength = LengthOfWeight(lowerLimit);
        var upperLength = LengthOfWeight(upperLimit);

        if (lowerLimit >= upperLimit)
        {
            return false;
        }

        // Reject if one limit is a prefix of the other.
        if (lowerLength < upperLength)
        {
            if (lowerLimit == TruncateWeight(upperLimit, lowerLength))
            {
                return false;
            }
        }

        // Indices [0] and [1] are unused; this simplifies indexing by byte length.
        var lower = new WeightRange[5];
        var middle = new WeightRange();
        var upper = new WeightRange[5];

        var weight = lowerLimit;
        for (var length = lowerLength; length > _middleLength; --length)
        {
            var trail = GetWeightTrail(weight, length);
            if (trail < _maxBytes[length])
            {
                lower[length].Start = IncWeightTrail(weight, length);
                lower[length].End = SetWeightTrail(weight, length, _maxBytes[length]);
                lower[length].Length = length;
                lower[length].Count = (int)(_maxBytes[length] - trail);
            }
            weight = TruncateWeight(weight, length - 1);
        }
        if (weight < 0xff000000)
        {
            middle.Start = IncWeightTrail(weight, _middleLength);
        }
        else
        {
            // Prevent overflow for primary lead byte FF, which would start the middle range at 0.
            middle.Start = 0xffffffff;
        }

        weight = upperLimit;
        for (var length = upperLength; length > _middleLength; --length)
        {
            var trail = GetWeightTrail(weight, length);
            if (trail > _minBytes[length])
            {
                upper[length].Start = SetWeightTrail(weight, length, _minBytes[length]);
                upper[length].End = DecWeightTrail(weight, length);
                upper[length].Length = length;
                upper[length].Count = (int)(trail - _minBytes[length]);
            }
            weight = TruncateWeight(weight, length - 1);
        }
        middle.End = DecWeightTrail(weight, _middleLength);

        middle.Length = _middleLength;
        if (middle.End >= middle.Start)
        {
            middle.Count = (int)((middle.End - middle.Start) >> (8 * (4 - _middleLength))) + 1;
        }
        else
        {
            // No middle range: eliminate overlaps between the lower and upper ranges.
            for (var length = 4; length > _middleLength; --length)
            {
                if (lower[length].Count > 0 && upper[length].Count > 0)
                {
                    var lowerEnd = lower[length].End;
                    var upperStart = upper[length].Start;
                    var merged = false;

                    if (lowerEnd > upperStart)
                    {
                        // The ranges collide; intersect them.
                        lower[length].End = upper[length].End;
                        lower[length].Count =
                            (int)GetWeightTrail(lower[length].End, length)
                            - (int)GetWeightTrail(lower[length].Start, length) + 1;
                        merged = true;
                    }
                    else if (lowerEnd == upperStart)
                    {
                        // Not possible unless minByte==maxByte, which is disallowed.
                    }
                    else
                    {
                        if (IncWeight(lowerEnd, length) == upperStart)
                        {
                            // Merge adjacent ranges.
                            lower[length].End = upper[length].End;
                            lower[length].Count += upper[length].Count;
                            merged = true;
                        }
                    }
                    if (merged)
                    {
                        // Remove all shorter ranges: there was no room for them between the merged ones.
                        upper[length].Count = 0;
                        while (--length > _middleLength)
                        {
                            lower[length].Count = 0;
                            upper[length].Count = 0;
                        }
                        break;
                    }
                }
            }
        }

        // Copy the ranges, shortest first, into the result array.
        _rangeCount = 0;
        if (middle.Count > 0)
        {
            _ranges[_rangeCount] = middle;
            _rangeCount = 1;
        }
        for (var length = _middleLength + 1; length <= 4; ++length)
        {
            // Copy upper first so the middle range is more likely the first one used.
            if (upper[length].Count > 0)
            {
                _ranges[_rangeCount] = upper[length];
                ++_rangeCount;
            }
            if (lower[length].Count > 0)
            {
                _ranges[_rangeCount] = lower[length];
                ++_rangeCount;
            }
        }
        return _rangeCount > 0;
    }

    private bool AllocWeightsInShortRanges(int n, int minLength)
    {
        // See if the first few minLength and minLength+1 ranges have enough weights.
        for (var i = 0; i < _rangeCount && _ranges[i].Length <= minLength + 1; ++i)
        {
            if (n <= _ranges[i].Count)
            {
                if (_ranges[i].Length > minLength)
                {
                    // Reduce the count from the last minLength+1 range, which might sort before some
                    // minLength ranges, so all minLength weights get used.
                    _ranges[i].Count = n;
                }
                _rangeCount = i + 1;
                if (_rangeCount > 1)
                {
                    Array.Sort(_ranges, 0, _rangeCount, RangeStartComparer);
                }
                return true;
            }
            n -= _ranges[i].Count;
        }
        return false;
    }

    private bool AllocWeightsInMinLengthRanges(int n, int minLength)
    {
        // See if the minLength ranges have enough weights when one is split and the rest lengthened.
        var count = 0;
        var minLengthRangeCount = 0;
        for (;
            minLengthRangeCount < _rangeCount && _ranges[minLengthRangeCount].Length == minLength;
            ++minLengthRangeCount)
        {
            count += _ranges[minLengthRangeCount].Count;
        }

        var nextCountBytes = CountBytes(minLength + 1);
        if (n > count * nextCountBytes)
        {
            return false;
        }

        // Merge the minLength ranges, then split again as necessary.
        var start = _ranges[0].Start;
        var end = _ranges[0].End;
        for (var i = 1; i < minLengthRangeCount; ++i)
        {
            if (_ranges[i].Start < start)
            {
                start = _ranges[i].Start;
            }
            if (_ranges[i].End > end)
            {
                end = _ranges[i].End;
            }
        }

        // Split the range between minLength (count1) and minLength+1 (count2):
        //   count1 + count2 * nextCountBytes = n
        //   count1 + count2 = count
        var count2 = (n - count) / (nextCountBytes - 1);
        var count1 = count - count2;
        if (count2 == 0 || count1 + count2 * nextCountBytes < n)
        {
            // Round up.
            ++count2;
            --count1;
        }

        _ranges[0].Start = start;

        if (count1 == 0)
        {
            // Make one long range.
            _ranges[0].End = end;
            _ranges[0].Count = count;
            LengthenRange(ref _ranges[0]);
            _rangeCount = 1;
        }
        else
        {
            // Split the range and lengthen the second part.
            _ranges[0].End = IncWeightByOffset(start, minLength, count1 - 1);
            _ranges[0].Count = count1;

            _ranges[1].Start = IncWeight(_ranges[0].End, minLength);
            _ranges[1].End = end;
            _ranges[1].Length = minLength;
            _ranges[1].Count = count2;
            LengthenRange(ref _ranges[1]);
            _rangeCount = 2;
        }
        return true;
    }

    private static readonly IComparer<WeightRange> RangeStartComparer =
        Comparer<WeightRange>.Create((left, right) => left.Start.CompareTo(right.Start));

    private static uint GetWeightTrail(uint weight, int length)
    {
        return (weight >> (8 * (4 - length))) & 0xff;
    }

    private static uint SetWeightTrail(uint weight, int length, uint trail)
    {
        length = 8 * (4 - length);
        return (weight & (0xffffff00u << length)) | (trail << length);
    }

    private static uint GetWeightByte(uint weight, int index)
    {
        return GetWeightTrail(weight, index);
    }

    private static uint SetWeightByte(uint weight, int index, uint b)
    {
        uint mask;
        index *= 8;
        if (index < 32)
        {
            mask = 0xffffffffu >> index;
        }
        else
        {
            mask = 0;
        }
        index = 32 - index;
        mask |= 0xffffff00u << index;
        return (weight & mask) | (b << index);
    }

    private static uint TruncateWeight(uint weight, int length)
    {
        return weight & (0xffffffffu << (8 * (4 - length)));
    }

    private static uint IncWeightTrail(uint weight, int length)
    {
        return weight + (1u << (8 * (4 - length)));
    }

    private static uint DecWeightTrail(uint weight, int length)
    {
        return weight - (1u << (8 * (4 - length)));
    }
}
