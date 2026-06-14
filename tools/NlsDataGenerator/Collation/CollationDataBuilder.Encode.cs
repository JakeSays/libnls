namespace NlsDataGenerator.Collation;

// CE/CE32 encoding for the builder, ported from collationdatabuilder.cpp: deduplicating stores of
// CEs and CE32 sequences, and the packing of one or more 64-bit CEs into a single CE32 or an
// expansion reference.
internal partial class CollationDataBuilder
{
    private int AddCe(long ce)
    {
        var length = Ce64s.Count;
        for (var i = 0; i < length; ++i)
        {
            if (ce == Ce64s[i])
            {
                return i;
            }
        }
        Ce64s.Add(ce);
        return length;
    }

    private int AddCe32(uint ce32)
    {
        var length = Ce32s.Count;
        for (var i = 0; i < length; ++i)
        {
            if (ce32 == Ce32s[i])
            {
                return i;
            }
        }
        Ce32s.Add(ce32);
        return length;
    }

    private int AddConditionalCe32(string context, uint ce32)
    {
        var index = ConditionalCe32s.Count;
        if (index > Collator.MaxIndex)
        {
            throw new InvalidOperationException("collation conditional-CE32 index overflow");
        }
        ConditionalCe32s.Add(new ConditionalCE32(context, ce32));
        return index;
    }

    private static uint EncodeOneCeAsCe32(long ce)
    {
        var p = (uint)(ce >> 32);
        var lower32 = (uint)ce;
        var t = (uint)(ce & 0xFFFF);
        if ((ce & 0xFFFF00FF00FFL) == 0)
        {
            // normal form ppppsstt
            return p | (lower32 >> 16) | (t >> 8);
        }
        if ((ce & 0xFFFFFFFFFFL) == Collator.CommonSecAndTerCe)
        {
            // long-primary form ppppppC1
            return Collator.MakeLongPrimaryCe32(p);
        }
        if (p == 0 && (t & 0xFF) == 0)
        {
            // long-secondary form ssssttC2
            return Collator.MakeLongSecondaryCe32(lower32);
        }
        return Collator.NoCe32;
    }

    private uint EncodeOneCe(long ce)
    {
        var ce32 = EncodeOneCeAsCe32(ce);
        if (ce32 != Collator.NoCe32)
        {
            return ce32;
        }
        var index = AddCe(ce);
        if (index > Collator.MaxIndex)
        {
            throw new InvalidOperationException("collation CE index overflow");
        }
        return Collator.MakeCe32FromTagIndexAndLength(Collator.ExpansionTag, index, 1);
    }

    public virtual uint EncodeCEs(long[] ces, int cesLength)
    {
        if (cesLength < 0 || cesLength > Collator.MaxExpansionLength)
        {
            throw new ArgumentOutOfRangeException(nameof(cesLength));
        }
        if (cesLength == 0)
        {
            // We cannot map to nothing, but we can map to a completely ignorable CE.
            return EncodeOneCeAsCe32(0);
        }
        if (cesLength == 1)
        {
            return EncodeOneCe(ces[0]);
        }
        if (cesLength == 2)
        {
            // Try to encode two CEs as one Latin mini expansion.
            var ce0 = ces[0];
            var ce1 = ces[1];
            var p0 = (uint)(ce0 >> 32);
            if ((ce0 & 0xFFFFFFFFFF00FFL) == Collator.CommonSecondaryCe
                && (ce1 & unchecked((long)0xFFFFFFFF00FFFFFFUL)) == Collator.CommonTertiaryCe
                && p0 != 0)
            {
                return p0
                    | (((uint)ce0 & 0xFF00) << 8)
                    | (uint)(ce1 >> 16)
                    | Collator.SpecialCe32LowByte
                    | Collator.LatinExpansionTag;
            }
        }
        // Try to encode two or more CEs as CE32s; otherwise store 64-bit CEs.
        var newCe32s = new int[Collator.MaxExpansionLength];
        for (var i = 0; ; ++i)
        {
            if (i == cesLength)
            {
                return EncodeExpansion32(newCe32s, cesLength);
            }
            var ce32 = EncodeOneCeAsCe32(ces[i]);
            if (ce32 == Collator.NoCe32)
            {
                break;
            }
            newCe32s[i] = (int)ce32;
        }
        return EncodeExpansion(ces, cesLength);
    }

    private uint EncodeExpansion(long[] ces, int length)
    {
        // Reuse an identical sequence if already stored.
        var first = ces[0];
        var ce64sMax = Ce64s.Count - length;
        for (var i = 0; i <= ce64sMax; ++i)
        {
            if (first == Ce64s[i])
            {
                if (i > Collator.MaxIndex)
                {
                    throw new InvalidOperationException("collation CE index overflow");
                }
                var match = true;
                for (var j = 1; j < length; ++j)
                {
                    if (Ce64s[i + j] != ces[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return Collator.MakeCe32FromTagIndexAndLength(Collator.ExpansionTag, i, length);
                }
            }
        }
        var start = Ce64s.Count;
        if (start > Collator.MaxIndex)
        {
            throw new InvalidOperationException("collation CE index overflow");
        }
        for (var j = 0; j < length; ++j)
        {
            Ce64s.Add(ces[j]);
        }
        return Collator.MakeCe32FromTagIndexAndLength(Collator.ExpansionTag, start, length);
    }

    private uint EncodeExpansion32(int[] newCe32s, int length)
    {
        var first = newCe32s[0];
        var ce32sMax = Ce32s.Count - length;
        for (var i = 0; i <= ce32sMax; ++i)
        {
            if (first == (int)Ce32s[i])
            {
                if (i > Collator.MaxIndex)
                {
                    throw new InvalidOperationException("collation CE index overflow");
                }
                var match = true;
                for (var j = 1; j < length; ++j)
                {
                    if ((int)Ce32s[i + j] != newCe32s[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return Collator.MakeCe32FromTagIndexAndLength(Collator.Expansion32Tag, i, length);
                }
            }
        }
        var start = Ce32s.Count;
        if (start > Collator.MaxIndex)
        {
            throw new InvalidOperationException("collation CE index overflow");
        }
        for (var j = 0; j < length; ++j)
        {
            Ce32s.Add((uint)newCe32s[j]);
        }
        return Collator.MakeCe32FromTagIndexAndLength(Collator.Expansion32Tag, start, length);
    }
}
