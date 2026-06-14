using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Collation;

// Builds the fast-Latin mini-table, ported from ICU's CollationFastLatinBuilder
// (collationfastlatinbuilder.cpp). It reads the finished CollationData, collects the unique CEs of
// the common Latin and punctuation characters, assigns each a compact 16-bit mini CE, and writes a
// table of per-character mini CEs plus expansion and contraction lists. Self-contained: it does its
// own CE32 expansion (no CollationIterator) and reads contraction suffixes via a UCharsTrie reader.
internal sealed class CollationFastLatinBuilder
{
    private const int NumSpecialGroups = 4; // space, punct, symbol, currency (not digit)
    private const int UscriptLatin = 25;
    private const int UcolReorderCodeDigit = CollationData.UcolReorderCodeFirst + 4;
    private const long ContractionFlag = 0x80000000L;

    private long _ce0;
    private long _ce1;
    private readonly long[,] _charCEs = new long[CollationFastLatinFormat.NumFastChars, 2];
    private readonly List<long> _contractionCEs = [];
    private readonly List<long> _uniqueCEs = [];
    private ushort[] _miniCEs = [];

    private readonly uint[] _lastSpecialPrimaries = new uint[NumSpecialGroups];
    private uint _firstDigitPrimary;
    private uint _firstLatinPrimary;
    private uint _lastLatinPrimary;
    private uint _firstShortPrimary;
    private bool _shortPrimaryOverflow;

    private readonly List<char> _result = [];
    private int _headerLength;

    public ushort[] GetTable()
    {
        var table = new ushort[_result.Count];
        for (var i = 0; i < _result.Count; ++i)
        {
            table[i] = _result[i];
        }
        return table;
    }

    public bool ForData(CollationData data)
    {
        if (!LoadGroups(data))
        {
            return false;
        }
        // Fast handling of digits.
        _firstShortPrimary = _firstDigitPrimary;
        GetCEs(data);
        if (!EncodeUniqueCEs())
        {
            return false;
        }
        if (_shortPrimaryOverflow)
        {
            // Give digits long mini primaries, leaving more short primaries for letters.
            _firstShortPrimary = _firstLatinPrimary;
            ResetCEs();
            GetCEs(data);
            if (!EncodeUniqueCEs())
            {
                return false;
            }
        }
        var ok = !_shortPrimaryOverflow && EncodeCharCEs() && EncodeContractions();
        return ok;
    }

    private bool LoadGroups(CollationData data)
    {
        _headerLength = 1 + NumSpecialGroups;
        var r0 = (uint)((CollationFastLatinFormat.Version << 8) | _headerLength);
        _result.Add((char)r0);
        // The first reordering groups are special (space, punct, symbol, currency).
        for (var i = 0; i < NumSpecialGroups; ++i)
        {
            _lastSpecialPrimaries[i] = data.GetLastPrimaryForGroup(CollationData.UcolReorderCodeFirst + i);
            if (_lastSpecialPrimaries[i] == 0)
            {
                return false; // missing data
            }
            _result.Add((char)0); // reserve a slot for this group
        }
        _firstDigitPrimary = data.GetFirstPrimaryForGroup(UcolReorderCodeDigit);
        _firstLatinPrimary = data.GetFirstPrimaryForGroup(UscriptLatin);
        _lastLatinPrimary = data.GetLastPrimaryForGroup(UscriptLatin);
        if (_firstDigitPrimary == 0 || _firstLatinPrimary == 0)
        {
            return false;
        }
        return true;
    }

    private bool InSameGroup(uint p, uint q)
    {
        // Both or neither encoded as short primaries.
        if (p >= _firstShortPrimary)
        {
            return q >= _firstShortPrimary;
        }
        if (q >= _firstShortPrimary)
        {
            return false;
        }
        // Both or neither potentially-variable.
        var lastVariablePrimary = _lastSpecialPrimaries[NumSpecialGroups - 1];
        if (p > lastVariablePrimary)
        {
            return q > lastVariablePrimary;
        }
        if (q > lastVariablePrimary)
        {
            return false;
        }
        // Both will be long mini primaries; they must be in the same special group.
        for (var i = 0; ; ++i)
        {
            var lastPrimary = _lastSpecialPrimaries[i];
            if (p <= lastPrimary)
            {
                return q <= lastPrimary;
            }
            if (q <= lastPrimary)
            {
                return false;
            }
        }
    }

    private void ResetCEs()
    {
        _contractionCEs.Clear();
        _uniqueCEs.Clear();
        _shortPrimaryOverflow = false;
        _result.RemoveRange(_headerLength, _result.Count - _headerLength);
    }

    private void GetCEs(CollationData data)
    {
        var i = 0;
        for (var c = 0; ; ++i, ++c)
        {
            if (c == CollationFastLatinFormat.LatinLimit)
            {
                c = CollationFastLatinFormat.PunctStart;
            }
            else if (c == CollationFastLatinFormat.PunctLimit)
            {
                break;
            }
            var ce32 = data.GetCe32(c);
            CollationData d;
            if (ce32 == Collator.FallbackCe32)
            {
                d = data.Base!;
                ce32 = d.GetCe32(c);
            }
            else
            {
                d = data;
            }
            if (GetCEsFromCE32(d, c, ce32))
            {
                _charCEs[i, 0] = _ce0;
                _charCEs[i, 1] = _ce1;
                AddUniqueCE(_ce0);
                AddUniqueCE(_ce1);
            }
            else
            {
                _charCEs[i, 0] = _ce0 = Collator.NoCe;
                _charCEs[i, 1] = _ce1 = 0;
            }
            if (c == 0 && !IsContractionCharCE(_ce0))
            {
                // Always map U+0000 to a contraction with only a default value.
                AddContractionEntry((int)CollationFastLatinFormat.ContrCharMask, _ce0, _ce1);
                _charCEs[0, 0] = ((long)Collator.NoCePrimary << 32) | ContractionFlag;
                _charCEs[0, 1] = 0;
            }
        }
        // Terminate the last contraction list.
        _contractionCEs.Add(CollationFastLatinFormat.ContrCharMask);
    }

    private bool GetCEsFromCE32(CollationData data, int c, uint ce32)
    {
        ce32 = data.GetFinalCe32(ce32);
        _ce1 = 0;
        if (Collator.IsSimpleOrLongCe32(ce32))
        {
            _ce0 = Collator.CeFromCe32(ce32);
        }
        else
        {
            switch (Collator.TagFromCe32(ce32))
            {
                case Collator.LatinExpansionTag:
                    _ce0 = Collator.LatinCe0FromCe32(ce32);
                    _ce1 = Collator.LatinCe1FromCe32(ce32);
                    break;
                case Collator.Expansion32Tag:
                {
                    var index = Collator.IndexFromCe32(ce32);
                    var length = Collator.LengthFromCe32(ce32);
                    if (length <= 2)
                    {
                        _ce0 = Collator.CeFromCe32(data.Ce32s[index]);
                        if (length == 2)
                        {
                            _ce1 = Collator.CeFromCe32(data.Ce32s[index + 1]);
                        }
                        break;
                    }
                    return false;
                }
                case Collator.ExpansionTag:
                {
                    var index = Collator.IndexFromCe32(ce32);
                    var length = Collator.LengthFromCe32(ce32);
                    if (length <= 2)
                    {
                        _ce0 = data.Ces[index];
                        if (length == 2)
                        {
                            _ce1 = data.Ces[index + 1];
                        }
                        break;
                    }
                    return false;
                }
                case Collator.ContractionTag:
                    return GetCEsFromContractionCE32(data, ce32);
                case Collator.OffsetTag:
                    _ce0 = data.GetCeFromOffsetCe32(c, ce32);
                    break;
                default:
                    return false;
            }
        }
        // A mapping can be completely ignorable.
        if (_ce0 == 0)
        {
            return _ce1 == 0;
        }
        // No ignorable ce0 unless completely ignorable.
        var p0 = (uint)(_ce0 >> 32);
        if (p0 == 0)
        {
            return false;
        }
        // Only primaries up to the Latin script.
        if (p0 > _lastLatinPrimary)
        {
            return false;
        }
        var lower32_0 = (uint)_ce0;
        if (p0 < _firstShortPrimary)
        {
            // Non-common secondary/case only with short primaries.
            if ((lower32_0 & Collator.SecondaryAndCaseMask) != Collator.CommonSecondaryCe)
            {
                return false;
            }
        }
        // No below-common tertiary weights.
        if ((lower32_0 & Collator.OnlyTertiaryMask) < Collator.CommonWeight16)
        {
            return false;
        }
        if (_ce1 != 0)
        {
            // Both primaries in the same group, or both short, or short followed by a secondary CE.
            var p1 = (uint)(_ce1 >> 32);
            if (p1 == 0 ? p0 < _firstShortPrimary : !InSameGroup(p0, p1))
            {
                return false;
            }
            var lower32_1 = (uint)_ce1;
            if ((lower32_1 >> 16) == 0)
            {
                return false; // no tertiary CEs
            }
            if (p1 != 0 && p1 < _firstShortPrimary)
            {
                if ((lower32_1 & Collator.SecondaryAndCaseMask) != Collator.CommonSecondaryCe)
                {
                    return false;
                }
            }
            if ((lower32_1 & Collator.OnlyTertiaryMask) < Collator.CommonWeight16)
            {
                return false;
            }
        }
        // No quaternary weights.
        if (((_ce0 | _ce1) & Collator.QuaternaryMask) != 0)
        {
            return false;
        }
        return true;
    }

    private bool GetCEsFromContractionCE32(CollationData data, uint ce32)
    {
        var index = Collator.IndexFromCe32(ce32);
        // The default ce32 (no suffix match); must not itself be a contraction.
        ce32 = CollationData.ReadCe32(data.Contexts, index);
        var contractionIndex = _contractionCEs.Count;
        if (GetCEsFromCE32(data, -1, ce32))
        {
            AddContractionEntry((int)CollationFastLatinFormat.ContrCharMask, _ce0, _ce1);
        }
        else
        {
            AddContractionEntry((int)CollationFastLatinFormat.ContrCharMask, Collator.NoCe, 0);
        }
        var prevX = -1;
        var addContraction = false;
        var suffixes = new UCharsTrieIterator(data.Contexts, index + 2, 0);
        while (suffixes.Next())
        {
            var suffix = suffixes.Str;
            var x = CollationFastLatinFormat.GetCharIndex(suffix[0]);
            if (x < 0)
            {
                continue; // ignore anything but fast Latin text
            }
            if (x == prevX)
            {
                if (addContraction)
                {
                    // Bail out for all contractions starting with this character.
                    AddContractionEntry(x, Collator.NoCe, 0);
                    addContraction = false;
                }
                continue;
            }
            if (addContraction)
            {
                AddContractionEntry(prevX, _ce0, _ce1);
            }
            ce32 = (uint)suffixes.Value;
            if (suffix.Length == 1 && GetCEsFromCE32(data, -1, ce32))
            {
                addContraction = true;
            }
            else
            {
                AddContractionEntry(x, Collator.NoCe, 0);
                addContraction = false;
            }
            prevX = x;
        }
        if (addContraction)
        {
            AddContractionEntry(prevX, _ce0, _ce1);
        }
        // Enter contraction handling even if there were no fast contractions, so we can bail out
        // when a non-fast-Latin character follows.
        _ce0 = ((long)Collator.NoCePrimary << 32) | ContractionFlag | (uint)contractionIndex;
        _ce1 = 0;
        return true;
    }

    private void AddContractionEntry(int x, long cce0, long cce1)
    {
        _contractionCEs.Add(x);
        _contractionCEs.Add(cce0);
        _contractionCEs.Add(cce1);
        AddUniqueCE(cce0);
        AddUniqueCE(cce1);
    }

    private void AddUniqueCE(long ce)
    {
        if (ce == 0 || (uint)(ce >> 32) == Collator.NoCePrimary)
        {
            return;
        }
        ce &= ~(long)Collator.CaseMask; // blank out case bits
        var i = BinarySearch(_uniqueCEs, ce);
        if (i < 0)
        {
            _uniqueCEs.Insert(~i, ce);
        }
    }

    private uint GetMiniCE(long ce)
    {
        ce &= ~(long)Collator.CaseMask;
        var index = BinarySearch(_uniqueCEs, ce);
        return _miniCEs[index];
    }

    private bool EncodeUniqueCEs()
    {
        _miniCEs = new ushort[_uniqueCEs.Count];
        var group = 0;
        var lastGroupPrimary = _lastSpecialPrimaries[group];
        uint prevPrimary = 0;
        uint prevSecondary = 0;
        uint pri = 0;
        uint sec = 0;
        var ter = CollationFastLatinFormat.CommonTer;
        for (var i = 0; i < _uniqueCEs.Count; ++i)
        {
            var ce = _uniqueCEs[i];
            var p = (uint)(ce >> 32);
            if (p != prevPrimary)
            {
                while (p > lastGroupPrimary)
                {
                    // Set the group's header to the last "long primary" in or before the group.
                    _result[1 + group] = (char)pri;
                    if (++group < NumSpecialGroups)
                    {
                        lastGroupPrimary = _lastSpecialPrimaries[group];
                    }
                    else
                    {
                        lastGroupPrimary = 0xFFFFFFFF;
                        break;
                    }
                }
                if (p < _firstShortPrimary)
                {
                    if (pri == 0)
                    {
                        pri = CollationFastLatinFormat.MinLong;
                    }
                    else if (pri < CollationFastLatinFormat.MaxLong)
                    {
                        pri += CollationFastLatinFormat.LongInc;
                    }
                    else
                    {
                        _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                        continue;
                    }
                }
                else
                {
                    if (pri < CollationFastLatinFormat.MinShort)
                    {
                        pri = CollationFastLatinFormat.MinShort;
                    }
                    else if (pri < (CollationFastLatinFormat.MaxShort - CollationFastLatinFormat.ShortInc))
                    {
                        // Reserve the highest primary weight for U+FFFF.
                        pri += CollationFastLatinFormat.ShortInc;
                    }
                    else
                    {
                        _shortPrimaryOverflow = true;
                        _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                        continue;
                    }
                }
                prevPrimary = p;
                prevSecondary = Collator.CommonWeight16;
                sec = CollationFastLatinFormat.CommonSec;
                ter = CollationFastLatinFormat.CommonTer;
            }
            var lower32 = (uint)ce;
            var s = lower32 >> 16;
            if (s != prevSecondary)
            {
                if (pri == 0)
                {
                    if (sec == 0)
                    {
                        sec = CollationFastLatinFormat.MinSecHigh;
                    }
                    else if (sec < CollationFastLatinFormat.MaxSecHigh)
                    {
                        sec += CollationFastLatinFormat.SecInc;
                    }
                    else
                    {
                        _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                        continue;
                    }
                    prevSecondary = s;
                    ter = CollationFastLatinFormat.CommonTer;
                }
                else if (s < Collator.CommonWeight16)
                {
                    if (sec == CollationFastLatinFormat.CommonSec)
                    {
                        sec = CollationFastLatinFormat.MinSecBefore;
                    }
                    else if (sec < CollationFastLatinFormat.MaxSecBefore)
                    {
                        sec += CollationFastLatinFormat.SecInc;
                    }
                    else
                    {
                        _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                        continue;
                    }
                }
                else if (s == Collator.CommonWeight16)
                {
                    sec = CollationFastLatinFormat.CommonSec;
                }
                else
                {
                    if (sec < CollationFastLatinFormat.MinSecAfter)
                    {
                        sec = CollationFastLatinFormat.MinSecAfter;
                    }
                    else if (sec < CollationFastLatinFormat.MaxSecAfter)
                    {
                        sec += CollationFastLatinFormat.SecInc;
                    }
                    else
                    {
                        _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                        continue;
                    }
                }
                prevSecondary = s;
                ter = CollationFastLatinFormat.CommonTer;
            }
            var t = lower32 & Collator.OnlyTertiaryMask;
            if (t > Collator.CommonWeight16)
            {
                if (ter < CollationFastLatinFormat.MaxTerAfter)
                {
                    ++ter;
                }
                else
                {
                    _miniCEs[i] = (ushort)CollationFastLatinFormat.BailOut;
                    continue;
                }
            }
            if (CollationFastLatinFormat.MinLong <= pri && pri <= CollationFastLatinFormat.MaxLong)
            {
                _miniCEs[i] = (ushort)(pri | ter);
            }
            else
            {
                _miniCEs[i] = (ushort)(pri | sec | ter);
            }
        }
        return true;
    }

    private bool EncodeCharCEs()
    {
        var miniCEsStart = _result.Count;
        for (var i = 0; i < CollationFastLatinFormat.NumFastChars; ++i)
        {
            _result.Add((char)0); // completely ignorable
        }
        var indexBase = _result.Count;
        for (var i = 0; i < CollationFastLatinFormat.NumFastChars; ++i)
        {
            var ce = _charCEs[i, 0];
            if (IsContractionCharCE(ce))
            {
                continue; // defer contraction
            }
            var miniCE = EncodeTwoCEs(ce, _charCEs[i, 1]);
            if (miniCE > 0xFFFF)
            {
                var expansionIndex = _result.Count - indexBase;
                if (expansionIndex > (int)CollationFastLatinFormat.IndexMask)
                {
                    miniCE = CollationFastLatinFormat.BailOut;
                }
                else
                {
                    _result.Add((char)(miniCE >> 16));
                    _result.Add((char)miniCE);
                    miniCE = CollationFastLatinFormat.Expansion | (uint)expansionIndex;
                }
            }
            _result[miniCEsStart + i] = (char)miniCE;
        }
        return true;
    }

    private bool EncodeContractions()
    {
        // Encode all contraction lists so the first word of a list terminates the previous one.
        var indexBase = _headerLength + CollationFastLatinFormat.NumFastChars;
        var firstContractionIndex = _result.Count;
        for (var i = 0; i < CollationFastLatinFormat.NumFastChars; ++i)
        {
            var ce = _charCEs[i, 0];
            if (!IsContractionCharCE(ce))
            {
                continue;
            }
            var contractionIndex = _result.Count - indexBase;
            if (contractionIndex > (int)CollationFastLatinFormat.IndexMask)
            {
                _result[_headerLength + i] = (char)CollationFastLatinFormat.BailOut;
                continue;
            }
            var firstTriple = true;
            for (var index = (int)ce & 0x7FFFFFFF; ; index += 3)
            {
                var x = (int)_contractionCEs[index];
                if ((uint)x == CollationFastLatinFormat.ContrCharMask && !firstTriple)
                {
                    break;
                }
                var cce0 = _contractionCEs[index + 1];
                var cce1 = _contractionCEs[index + 2];
                var miniCE = EncodeTwoCEs(cce0, cce1);
                if (miniCE == CollationFastLatinFormat.BailOut)
                {
                    _result.Add((char)(x | (1 << CollationFastLatinFormat.ContrLengthShift)));
                }
                else if (miniCE <= 0xFFFF)
                {
                    _result.Add((char)(x | (2 << CollationFastLatinFormat.ContrLengthShift)));
                    _result.Add((char)miniCE);
                }
                else
                {
                    _result.Add((char)(x | (3 << CollationFastLatinFormat.ContrLengthShift)));
                    _result.Add((char)(miniCE >> 16));
                    _result.Add((char)miniCE);
                }
                firstTriple = false;
            }
            _result[_headerLength + i] =
                (char)(CollationFastLatinFormat.Contraction | (uint)contractionIndex);
        }
        if (_result.Count > firstContractionIndex)
        {
            // Terminate the last contraction list.
            _result.Add((char)CollationFastLatinFormat.ContrCharMask);
        }
        return true;
    }

    private uint EncodeTwoCEs(long first, long second)
    {
        if (first == 0)
        {
            return 0; // completely ignorable
        }
        if (first == Collator.NoCe)
        {
            return CollationFastLatinFormat.BailOut;
        }

        var miniCE = GetMiniCE(first);
        if (miniCE == CollationFastLatinFormat.BailOut)
        {
            return miniCE;
        }
        if (miniCE >= CollationFastLatinFormat.MinShort)
        {
            // Copy the case bits, shifted from CE bits 15..14 to mini CE bits 4..3.
            var c = ((uint)first & Collator.CaseMask) >> (14 - 3);
            c += CollationFastLatinFormat.LowerCase;
            miniCE |= c;
        }
        if (second == 0)
        {
            return miniCE;
        }

        var miniCE1 = GetMiniCE(second);
        if (miniCE1 == CollationFastLatinFormat.BailOut)
        {
            return miniCE1;
        }

        var case1 = (uint)second & Collator.CaseMask;
        if (miniCE >= CollationFastLatinFormat.MinShort
            && (miniCE & CollationFastLatinFormat.SecondaryMask) == CollationFastLatinFormat.CommonSec)
        {
            // Try to combine the two mini CEs into one.
            var sec1 = miniCE1 & CollationFastLatinFormat.SecondaryMask;
            var ter1 = miniCE1 & CollationFastLatinFormat.TertiaryMask;
            if (sec1 >= CollationFastLatinFormat.MinSecHigh && case1 == 0
                && ter1 == CollationFastLatinFormat.CommonTer)
            {
                return (miniCE & ~CollationFastLatinFormat.SecondaryMask) | sec1;
            }
        }

        if (miniCE1 <= CollationFastLatinFormat.SecondaryMask || CollationFastLatinFormat.MinShort <= miniCE1)
        {
            // Secondary CE, or a CE with a short primary: copy the case bits.
            case1 = (case1 >> (14 - 3)) + CollationFastLatinFormat.LowerCase;
            miniCE1 |= case1;
        }
        return (miniCE << 16) | miniCE1;
    }

    private static bool IsContractionCharCE(long ce)
    {
        return (uint)(ce >> 32) == Collator.NoCePrimary && ce != Collator.NoCe;
    }

    private static int BinarySearch(List<long> list, long ce)
    {
        var limit = list.Count;
        if (limit == 0)
        {
            return ~0;
        }
        var start = 0;
        for (;;)
        {
            var i = (start + limit) / 2;
            var cmp = ((ulong)ce).CompareTo((ulong)list[i]);
            if (cmp == 0)
            {
                return i;
            }
            if (cmp < 0)
            {
                if (i == start)
                {
                    return ~start;
                }
                limit = i;
            }
            else
            {
                if (i == start)
                {
                    return ~(start + 1);
                }
                start = i;
            }
        }
    }
}
