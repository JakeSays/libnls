namespace NlsDataGenerator.Collation;

// Accessor over the root collation elements table, ported from ICU's CollationRootElements
// (collationrootelements.h/.cpp). The base builder emits this table (an array of primary weights and
// secondary/tertiary deltas, prefixed by index entries) into ucadata.icu; the tailoring builder reads
// it through these methods to find the root weights adjacent to a reset position, so it can allocate
// new tailored weights in the gaps. The IX_/flag constants are also used by the base data writer.
internal sealed class CollationRootElements
{
    // Higher than any root primary.
    public const uint PrimarySentinel = 0xFFFFFF00;

    // Set in a root element that holds secondary & tertiary weights rather than a primary.
    public const uint SecTerDeltaFlag = 0x80;

    // Mask for the primary range step value in a primary-range-end element.
    public const byte PrimaryStepMask = 0x7F;

    // Indexes into the head of the table.
    public const int IxFirstTertiaryIndex = 0;
    public const int IxFirstSecondaryIndex = 1;
    public const int IxFirstPrimaryIndex = 2;
    public const int IxCommonSecAndTerCe = 3;
    public const int IxSecTerBoundaries = 4;
    public const int IxCount = 5;

    private readonly uint[] _elements;
    private readonly int _length;

    public CollationRootElements(uint[] elements)
    {
        _elements = elements;
        _length = elements.Length;
    }

    public uint GetTertiaryBoundary()
    {
        return (_elements[IxSecTerBoundaries] << 8) & 0xff00;
    }

    public uint GetFirstTertiaryCE()
    {
        return _elements[_elements[IxFirstTertiaryIndex]] & ~SecTerDeltaFlag;
    }

    public uint GetLastTertiaryCE()
    {
        return _elements[_elements[IxFirstSecondaryIndex] - 1] & ~SecTerDeltaFlag;
    }

    public uint GetLastCommonSecondary()
    {
        return (_elements[IxSecTerBoundaries] >> 16) & 0xff00;
    }

    public uint GetSecondaryBoundary()
    {
        return (_elements[IxSecTerBoundaries] >> 8) & 0xff00;
    }

    public uint GetFirstSecondaryCE()
    {
        return _elements[_elements[IxFirstSecondaryIndex]] & ~SecTerDeltaFlag;
    }

    public uint GetLastSecondaryCE()
    {
        return _elements[_elements[IxFirstPrimaryIndex] - 1] & ~SecTerDeltaFlag;
    }

    public uint GetFirstPrimary()
    {
        return _elements[_elements[IxFirstPrimaryIndex]];
    }

    public long GetFirstPrimaryCE()
    {
        return Collator.MakeCe(GetFirstPrimary());
    }

    public long LastCEWithPrimaryBefore(uint p)
    {
        if (p == 0)
        {
            return 0;
        }
        var index = FindP(p);
        var q = _elements[index];
        uint secTer;
        if (p == (q & 0xffffff00))
        {
            secTer = _elements[index - 1];
            if ((secTer & SecTerDeltaFlag) == 0)
            {
                p = secTer & 0xffffff00;
                secTer = Collator.CommonSecAndTerCe;
            }
            else
            {
                index -= 2;
                for (;;)
                {
                    p = _elements[index];
                    if ((p & SecTerDeltaFlag) == 0)
                    {
                        p &= 0xffffff00;
                        break;
                    }
                    --index;
                }
            }
        }
        else
        {
            p = q & 0xffffff00;
            secTer = Collator.CommonSecAndTerCe;
            for (;;)
            {
                q = _elements[++index];
                if ((q & SecTerDeltaFlag) == 0)
                {
                    break;
                }
                secTer = q;
            }
        }
        return ((long)p << 32) | (secTer & ~SecTerDeltaFlag);
    }

    public long FirstCEWithPrimaryAtLeast(uint p)
    {
        if (p == 0)
        {
            return 0;
        }
        var index = FindP(p);
        if (p != (_elements[index] & 0xffffff00))
        {
            for (;;)
            {
                p = _elements[++index];
                if ((p & SecTerDeltaFlag) == 0)
                {
                    break;
                }
            }
        }
        return ((long)p << 32) | Collator.CommonSecAndTerCe;
    }

    public uint GetPrimaryBefore(uint p, bool isCompressible)
    {
        var index = FindPrimary(p);
        int step;
        var q = _elements[index];
        if (p == (q & 0xffffff00))
        {
            step = (int)q & PrimaryStepMask;
            if (step == 0)
            {
                do
                {
                    p = _elements[--index];
                }
                while ((p & SecTerDeltaFlag) != 0);
                return p & 0xffffff00;
            }
        }
        else
        {
            var nextElement = _elements[index + 1];
            step = (int)nextElement & PrimaryStepMask;
        }
        if ((p & 0xffff) == 0)
        {
            return Collator.DecTwoBytePrimaryByOneStep(p, isCompressible, step);
        }
        return Collator.DecThreeBytePrimaryByOneStep(p, isCompressible, step);
    }

    public uint GetSecondaryBefore(uint p, uint s)
    {
        int index;
        uint previousSec;
        uint sec;
        if (p == 0)
        {
            index = (int)_elements[IxFirstSecondaryIndex];
            previousSec = 0;
            sec = _elements[index] >> 16;
        }
        else
        {
            index = FindPrimary(p) + 1;
            previousSec = Collator.BeforeWeight16;
            sec = GetFirstSecTerForPrimary(index) >> 16;
        }
        while (s > sec)
        {
            previousSec = sec;
            sec = _elements[index++] >> 16;
        }
        return previousSec;
    }

    public uint GetTertiaryBefore(uint p, uint s, uint t)
    {
        int index;
        uint previousTer;
        uint secTer;
        if (p == 0)
        {
            if (s == 0)
            {
                index = (int)_elements[IxFirstTertiaryIndex];
                previousTer = 0;
            }
            else
            {
                index = (int)_elements[IxFirstSecondaryIndex];
                previousTer = Collator.BeforeWeight16;
            }
            secTer = _elements[index] & ~SecTerDeltaFlag;
        }
        else
        {
            index = FindPrimary(p) + 1;
            previousTer = Collator.BeforeWeight16;
            secTer = GetFirstSecTerForPrimary(index);
        }
        var st = (s << 16) | t;
        while (st > secTer)
        {
            if ((secTer >> 16) == s)
            {
                previousTer = secTer;
            }
            secTer = _elements[index++] & ~SecTerDeltaFlag;
        }
        return previousTer & 0xffff;
    }

    public uint GetPrimaryAfter(uint p, int index, bool isCompressible)
    {
        var q = _elements[++index];
        int step;
        if ((q & SecTerDeltaFlag) == 0 && (step = (int)q & PrimaryStepMask) != 0)
        {
            if ((p & 0xffff) == 0)
            {
                return Collator.IncTwoBytePrimaryByOffset(p, isCompressible, step);
            }
            return Collator.IncThreeBytePrimaryByOffset(p, isCompressible, step);
        }
        while ((q & SecTerDeltaFlag) != 0)
        {
            q = _elements[++index];
        }
        return q;
    }

    public uint GetSecondaryAfter(int index, uint s)
    {
        uint secTer;
        uint secLimit;
        if (index == 0)
        {
            index = (int)_elements[IxFirstSecondaryIndex];
            secTer = _elements[index];
            secLimit = 0x10000;
        }
        else
        {
            secTer = GetFirstSecTerForPrimary(index + 1);
            secLimit = GetSecondaryBoundary();
        }
        for (;;)
        {
            var sec = secTer >> 16;
            if (sec > s)
            {
                return sec;
            }
            secTer = _elements[++index];
            if ((secTer & SecTerDeltaFlag) == 0)
            {
                return secLimit;
            }
        }
    }

    public uint GetTertiaryAfter(int index, uint s, uint t)
    {
        uint secTer;
        uint terLimit;
        if (index == 0)
        {
            if (s == 0)
            {
                index = (int)_elements[IxFirstTertiaryIndex];
                terLimit = 0x4000;
            }
            else
            {
                index = (int)_elements[IxFirstSecondaryIndex];
                terLimit = GetTertiaryBoundary();
            }
            secTer = _elements[index] & ~SecTerDeltaFlag;
        }
        else
        {
            secTer = GetFirstSecTerForPrimary(index + 1);
            terLimit = GetTertiaryBoundary();
        }
        var st = (s << 16) | t;
        for (;;)
        {
            if (secTer > st)
            {
                return secTer & 0xffff;
            }
            secTer = _elements[++index];
            if ((secTer & SecTerDeltaFlag) == 0 || (secTer >> 16) > s)
            {
                return terLimit;
            }
            secTer &= ~SecTerDeltaFlag;
        }
    }

    private uint GetFirstSecTerForPrimary(int index)
    {
        var secTer = _elements[index];
        if ((secTer & SecTerDeltaFlag) == 0)
        {
            return Collator.CommonSecAndTerCe;
        }
        secTer &= ~SecTerDeltaFlag;
        if (secTer > Collator.CommonSecAndTerCe)
        {
            return Collator.CommonSecAndTerCe;
        }
        return secTer;
    }

    public int FindPrimary(uint p)
    {
        return FindP(p);
    }

    private int FindP(uint p)
    {
        var start = (int)_elements[IxFirstPrimaryIndex];
        var limit = _length - 1;
        while (start + 1 < limit)
        {
            var i = (start + limit) / 2;
            var q = _elements[i];
            if ((q & SecTerDeltaFlag) != 0)
            {
                var j = i + 1;
                for (;;)
                {
                    if (j == limit)
                    {
                        break;
                    }
                    q = _elements[j];
                    if ((q & SecTerDeltaFlag) == 0)
                    {
                        i = j;
                        break;
                    }
                    ++j;
                }
                if ((q & SecTerDeltaFlag) != 0)
                {
                    j = i - 1;
                    for (;;)
                    {
                        if (j == start)
                        {
                            break;
                        }
                        q = _elements[j];
                        if ((q & SecTerDeltaFlag) == 0)
                        {
                            i = j;
                            break;
                        }
                        --j;
                    }
                    if ((q & SecTerDeltaFlag) != 0)
                    {
                        break;
                    }
                }
            }
            if (p < (q & 0xffffff00))
            {
                limit = i;
            }
            else
            {
                start = i;
            }
        }
        return start;
    }
}
