using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Collation;

// CollationDataBuilder.copyFrom + optimize, ported from collationdatabuilder.cpp (the CopyHelper).
// finalizeCEs copies every mapping from the source builder into a fresh one, rewriting CEs through the
// modifier (which turns temporary CEs into the final allocated ones). Expansions are inspected element
// by element and only re-encoded if any CE changed; builder-context (contraction/prefix) lists are
// deep-copied. optimize forces specific code points to be copied from the base into the tailoring trie.
internal partial class CollationDataBuilder
{
    public void CopyFrom(CollationDataBuilder src, ICeModifier modifier)
    {
        src.Trie.EnumRanges((start, end, value) =>
        {
            if (value == Collator.UnassignedCe32 || value == Collator.FallbackCe32)
            {
                return;
            }
            CopyRangeCe32(src, modifier, start, end, value);
        });
        Modified |= src.Modified;
    }

    private void CopyRangeCe32(CollationDataBuilder src, ICeModifier modifier, int start, int end, uint ce32)
    {
        ce32 = CopyCe32(src, modifier, ce32);
        Trie.SetRange(start, end, ce32, true);
        if (IsBuilderContextCe32(ce32))
        {
            ContextChars.Add(start, end);
        }
    }

    private uint CopyCe32(CollationDataBuilder src, ICeModifier modifier, uint ce32)
    {
        if (!Collator.IsSpecialCe32(ce32))
        {
            var ce = modifier.ModifyCe32(ce32);
            if (ce != Collator.NoCe)
            {
                ce32 = EncodeOneCe(ce);
            }
            return ce32;
        }

        var tag = Collator.TagFromCe32(ce32);
        if (tag == Collator.Expansion32Tag)
        {
            var srcIndex = Collator.IndexFromCe32(ce32);
            var length = Collator.LengthFromCe32(ce32);
            var modifiedCEs = new long[Collator.MaxExpansionLength];
            var isModified = false;
            for (var i = 0; i < length; ++i)
            {
                var e32 = src.Ce32s[srcIndex + i];
                long ce;
                if (Collator.IsSpecialCe32(e32) || (ce = modifier.ModifyCe32(e32)) == Collator.NoCe)
                {
                    if (isModified)
                    {
                        modifiedCEs[i] = Collator.CeFromCe32(e32);
                    }
                }
                else
                {
                    if (!isModified)
                    {
                        for (var j = 0; j < i; ++j)
                        {
                            modifiedCEs[j] = Collator.CeFromCe32(src.Ce32s[srcIndex + j]);
                        }
                        isModified = true;
                    }
                    modifiedCEs[i] = ce;
                }
            }
            if (isModified)
            {
                return EncodeCEs(modifiedCEs, length);
            }
            var raw = new int[length];
            for (var i = 0; i < length; ++i)
            {
                raw[i] = (int)src.Ce32s[srcIndex + i];
            }
            return EncodeExpansion32(raw, length);
        }
        if (tag == Collator.ExpansionTag)
        {
            var srcIndex = Collator.IndexFromCe32(ce32);
            var length = Collator.LengthFromCe32(ce32);
            var modifiedCEs = new long[Collator.MaxExpansionLength];
            var isModified = false;
            for (var i = 0; i < length; ++i)
            {
                var srcCE = src.Ce64s[srcIndex + i];
                var ce = modifier.ModifyCe(srcCE);
                if (ce == Collator.NoCe)
                {
                    if (isModified)
                    {
                        modifiedCEs[i] = srcCE;
                    }
                }
                else
                {
                    if (!isModified)
                    {
                        for (var j = 0; j < i; ++j)
                        {
                            modifiedCEs[j] = src.Ce64s[srcIndex + j];
                        }
                        isModified = true;
                    }
                    modifiedCEs[i] = ce;
                }
            }
            if (isModified)
            {
                return EncodeCEs(modifiedCEs, length);
            }
            var raw = new long[length];
            for (var i = 0; i < length; ++i)
            {
                raw[i] = src.Ce64s[srcIndex + i];
            }
            return EncodeExpansion(raw, length);
        }
        if (tag == Collator.BuilderDataTag)
        {
            var cond = src.GetConditionalCe32ForCe32(ce32);
            var destIndex = AddConditionalCe32(cond.Context, CopyCe32(src, modifier, cond.Ce32));
            ce32 = MakeBuilderContextCe32(destIndex);
            while (cond.Next >= 0)
            {
                cond = src.GetConditionalCe32(cond.Next);
                var prevDestCond = GetConditionalCe32(destIndex);
                destIndex = AddConditionalCe32(cond.Context, CopyCe32(src, modifier, cond.Ce32));
                var suffixStart = cond.PrefixLength() + 1;
                UnsafeBackwardSet.AddAll(cond.Context[suffixStart..]);
                prevDestCond.Next = destIndex;
            }
            return ce32;
        }
        // LONG_PRIMARY / LONG_SECONDARY / LATIN_EXPANSION / HANGUL: copy as-is.
        return ce32;
    }

    public void Optimize(UnicodeSet set)
    {
        foreach (var c in set.CodePoints)
        {
            var ce32 = Trie.Get(c);
            if (ce32 == Collator.FallbackCe32)
            {
                ce32 = Base!.GetFinalCe32(Base.GetCe32(c));
                ce32 = CopyFromBaseCe32(c, ce32, true);
                Trie.Set(c, ce32);
            }
        }
        Modified = true;
    }

    // Copies a base CE32 (resolving expansions, prefixes, and contractions into the builder's own
    // arrays and conditional-CE32 lists) so a tailoring can modify the result.
    private uint CopyFromBaseCe32(int c, uint ce32, bool withContext)
    {
        if (!Collator.IsSpecialCe32(ce32))
        {
            return ce32;
        }
        switch (Collator.TagFromCe32(ce32))
        {
            case Collator.LongPrimaryTag:
            case Collator.LongSecondaryTag:
            case Collator.LatinExpansionTag:
                break;
            case Collator.Expansion32Tag:
            {
                var index = Collator.IndexFromCe32(ce32);
                var length = Collator.LengthFromCe32(ce32);
                var raw = new int[length];
                for (var i = 0; i < length; ++i)
                {
                    raw[i] = (int)Base!.Ce32s[index + i];
                }
                ce32 = EncodeExpansion32(raw, length);
                break;
            }
            case Collator.ExpansionTag:
            {
                var index = Collator.IndexFromCe32(ce32);
                var length = Collator.LengthFromCe32(ce32);
                var raw = new long[length];
                for (var i = 0; i < length; ++i)
                {
                    raw[i] = Base!.Ces[index + i];
                }
                ce32 = EncodeExpansion(raw, length);
                break;
            }
            case Collator.PrefixTag:
            {
                var p = Collator.IndexFromCe32(ce32);
                ce32 = CollationData.ReadCe32(Base!.Contexts, p);
                if (!withContext)
                {
                    return CopyFromBaseCe32(c, ce32, false);
                }
                var head = new ConditionalCE32("\0", 0);
                var context = "\0";
                int index;
                if (Collator.IsContractionCe32(ce32))
                {
                    index = CopyContractionsFromBaseCe32(ref context, c, ce32, head);
                }
                else
                {
                    ce32 = CopyFromBaseCe32(c, ce32, true);
                    head.Next = index = AddConditionalCe32(context, ce32);
                }
                var cond = GetConditionalCe32(index);
                var prefixes = new UCharsTrieIterator(Base.Contexts, p + 2, 0);
                while (prefixes.Next())
                {
                    var reversed = ReverseCodePoints(prefixes.Str);
                    context = (char)reversed.Length + reversed;
                    ce32 = (uint)prefixes.Value;
                    if (Collator.IsContractionCe32(ce32))
                    {
                        index = CopyContractionsFromBaseCe32(ref context, c, ce32, cond);
                    }
                    else
                    {
                        ce32 = CopyFromBaseCe32(c, ce32, true);
                        cond.Next = index = AddConditionalCe32(context, ce32);
                    }
                    cond = GetConditionalCe32(index);
                }
                ce32 = MakeBuilderContextCe32(head.Next);
                ContextChars.Add(c);
                break;
            }
            case Collator.ContractionTag:
            {
                if (!withContext)
                {
                    var p = Collator.IndexFromCe32(ce32);
                    ce32 = CollationData.ReadCe32(Base!.Contexts, p);
                    return CopyFromBaseCe32(c, ce32, false);
                }
                var head = new ConditionalCE32("\0", 0);
                var context = "\0";
                CopyContractionsFromBaseCe32(ref context, c, ce32, head);
                ce32 = MakeBuilderContextCe32(head.Next);
                ContextChars.Add(c);
                break;
            }
            case Collator.OffsetTag:
                ce32 = GetCe32FromOffsetCe32(true, c, ce32);
                break;
            case Collator.ImplicitTag:
                ce32 = EncodeOneCe(Collator.UnassignedCeFromCodePoint(c));
                break;
            default:
                throw new InvalidOperationException("unexpected base CE32 tag in copyFromBaseCE32");
        }
        return ce32;
    }

    private int CopyContractionsFromBaseCe32(ref string context, int c, uint ce32, ConditionalCE32 cond)
    {
        var p = Collator.IndexFromCe32(ce32);
        int index;
        if ((ce32 & Collator.ContractSingleCpNoMatch) != 0)
        {
            index = -1;
        }
        else
        {
            ce32 = CollationData.ReadCe32(Base!.Contexts, p);
            ce32 = CopyFromBaseCe32(c, ce32, true);
            cond.Next = index = AddConditionalCe32(context, ce32);
            cond = GetConditionalCe32(index);
        }

        // Each suffix maps the base prefix + that one suffix; reset to the prefix every iteration
        // (ICU appends then truncates back to suffixStart). Accumulating would build bogus multi-suffix
        // contractions (e.g. alef + maddah + hamza) carrying the later suffix's value.
        var suffixStart = context;
        var suffixes = new UCharsTrieIterator(Base!.Contexts, p + 2, 0);
        while (suffixes.Next())
        {
            ce32 = CopyFromBaseCe32(c, (uint)suffixes.Value, true);
            cond.Next = index = AddConditionalCe32(suffixStart + suffixes.Str, ce32);
            cond = GetConditionalCe32(index);
        }
        return index;
    }

    private static string ReverseCodePoints(string s)
    {
        var codePoints = new List<int>();
        var i = 0;
        while (i < s.Length)
        {
            var c = char.ConvertToUtf32(s, i);
            i += c > 0xFFFF ? 2 : 1;
            codePoints.Add(c);
        }
        codePoints.Reverse();
        var builder = new System.Text.StringBuilder(s.Length);
        foreach (var c in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(c));
        }
        return builder.ToString();
    }
}
