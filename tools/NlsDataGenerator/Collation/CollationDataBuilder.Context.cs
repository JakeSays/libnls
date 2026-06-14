using System.Text;
using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// Compiling per-code-point context lists into runtime prefix/contraction tries, ported from
// collationdatabuilder.cpp's buildContexts/buildContext/addContextTrie (non-icu4x). Each code point
// with context has a sorted ConditionalCE32 list; this groups it by prefix, builds a UCharsTrie of
// contraction suffixes per prefix (with the CONTRACT_* flags from FCD16), and a UCharsTrie of
// reversed prefixes, storing the serialized tries in the contexts array.
internal partial class CollationDataBuilder
{
    private void ClearContexts()
    {
        Contexts.Clear();
        // Incrementing the era invalidates the builtCE32 cached from before this call.
        ++_contextsEra;
    }

    private void BuildContexts()
    {
        // Build all contexts from scratch, ignoring abandoned lists and cached builtCE32.
        ClearContexts();
        foreach (var c in ContextChars.CodePoints)
        {
            var ce32 = Trie.Get(c);
            if (!IsBuilderContextCe32(ce32))
            {
                throw new InvalidOperationException($"missing context data for U+{c:X4}");
            }
            var cond = GetConditionalCe32ForCe32(ce32);
            if (!TryBuildContext(cond, out ce32))
            {
                throw new InvalidOperationException($"collation context index overflow building U+{c:X4}");
            }
            Trie.Set(c, ce32);
        }
    }

    // Builds the runtime prefix/contraction tries for one code point's conditional list. Returns false
    // (without a complete result) when a context trie index exceeds Collator.MaxIndex; the caller either
    // clears the abandoned intermediate contexts and retries, or treats it as fatal in the final pass.
    private bool TryBuildContext(ConditionalCE32 head, out uint result)
    {
        result = 0;
        // The list head has no context and is followed by nodes that all have context.
        var prefixBuilder = new UCharsTrieBuilder();
        var contractionBuilder = new UCharsTrieBuilder();
        // Outer loop: from each prefix to the next.
        for (var cond = head; ; cond = GetConditionalCe32(cond.Next))
        {
            var prefixLength = cond.PrefixLength();
            var prefix = cond.Context[..(prefixLength + 1)];
            // Collect all contraction suffixes for this one prefix (firstCond..lastCond).
            var firstCond = cond;
            ConditionalCE32 lastCond;
            do
            {
                lastCond = cond;
                // Clear leftover defaultCE32 fields before reading/setting new values.
                cond.DefaultCe32 = Collator.NoCe32;
            }
            while (cond.Next >= 0
                && (cond = GetConditionalCe32(cond.Next)).Context.StartsWith(prefix, StringComparison.Ordinal));
            uint ce32;
            var suffixStart = prefixLength + 1; // == prefix.Length
            if (lastCond.Context.Length == suffixStart)
            {
                // One prefix without a contraction suffix.
                ce32 = lastCond.Ce32;
                cond = lastCond;
            }
            else
            {
                // Build the contractions trie.
                contractionBuilder.Clear();
                uint emptySuffixCe32 = 0;
                uint flags = 0;
                if (firstCond.Context.Length == suffixStart)
                {
                    // There is a mapping for the prefix and the single character c (p|c).
                    emptySuffixCe32 = firstCond.Ce32;
                    cond = GetConditionalCe32(firstCond.Next);
                }
                else
                {
                    // No mapping for the prefix and just the single character (only p|cd, p|ce ...).
                    flags |= Collator.ContractSingleCpNoMatch;
                    // Fall back to the mappings with the next-longest prefix.
                    for (cond = head; ; cond = GetConditionalCe32(cond.Next))
                    {
                        var length = cond.PrefixLength();
                        if (length == prefixLength)
                        {
                            break;
                        }
                        if (cond.DefaultCe32 != Collator.NoCe32
                            && (length == 0 || EndsWith(prefix, cond.Context, 1, length)))
                        {
                            emptySuffixCe32 = cond.DefaultCe32;
                        }
                    }
                    cond = firstCond;
                }
                // Optimization: set CONTRACT_NEXT_CCC when the first character of every contraction
                // suffix has lccc!=0.
                flags |= Collator.ContractNextCcc;
                for (;;)
                {
                    var suffix = cond.Context[suffixStart..];
                    var fcd16 = _getFcd16(char.ConvertToUtf32(suffix, 0));
                    if (fcd16 <= 0xFF)
                    {
                        flags &= ~Collator.ContractNextCcc;
                    }
                    fcd16 = _getFcd16(Utf16.LastCodePoint(suffix));
                    if (fcd16 > 0xFF)
                    {
                        // The last suffix character has lccc!=0, allowing discontiguous contractions.
                        flags |= Collator.ContractTrailingCcc;
                    }
                    contractionBuilder.Add(suffix, (int)cond.Ce32);
                    if (cond == lastCond)
                    {
                        break;
                    }
                    cond = GetConditionalCe32(cond.Next);
                }
                var index = AddContextTrie(emptySuffixCe32, contractionBuilder);
                if (index > Collator.MaxIndex)
                {
                    return false;
                }
                ce32 = Collator.MakeCe32FromTagAndIndex(Collator.ContractionTag, index) | flags;
            }
            firstCond.DefaultCe32 = ce32;
            if (prefixLength == 0)
            {
                if (cond.Next < 0)
                {
                    // No non-empty prefixes, only contractions.
                    result = ce32;
                    return true;
                }
            }
            else
            {
                var reversed = Reverse(prefix[1..]);
                prefixBuilder.Add(reversed, (int)ce32);
                if (cond.Next < 0)
                {
                    break;
                }
            }
        }
        var prefixIndex = AddContextTrie(head.DefaultCe32, prefixBuilder);
        if (prefixIndex > Collator.MaxIndex)
        {
            return false;
        }
        result = Collator.MakeCe32FromTagAndIndex(Collator.PrefixTag, prefixIndex);
        return true;
    }

    private int AddContextTrie(uint defaultCe32, UCharsTrieBuilder trieBuilder)
    {
        var context = new StringBuilder();
        context.Append((char)(defaultCe32 >> 16));
        context.Append((char)defaultCe32);
        context.Append(trieBuilder.Build());
        var ctx = context.ToString();
        var index = IndexOfContext(ctx);
        if (index < 0)
        {
            index = Contexts.Count;
            Contexts.AddRange(ctx);
        }
        return index;
    }

    private int IndexOfContext(string s)
    {
        var limit = Contexts.Count - s.Length;
        for (var i = 0; i <= limit; ++i)
        {
            var match = true;
            for (var j = 0; j < s.Length; ++j)
            {
                if (Contexts[i + j] != s[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    // Whether prefix ends with the substring other[start..start+length).
    private static bool EndsWith(string prefix, string other, int start, int length)
    {
        if (length > prefix.Length)
        {
            return false;
        }
        var offset = prefix.Length - length;
        for (var i = 0; i < length; ++i)
        {
            if (prefix[offset + i] != other[start + i])
            {
                return false;
            }
        }
        return true;
    }
}
