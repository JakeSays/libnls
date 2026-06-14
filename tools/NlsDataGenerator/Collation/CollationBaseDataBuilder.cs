using NlsDataGenerator.IcuFormat;
using NlsDataGenerator.Normalization;

namespace NlsDataGenerator.Collation;

// Builds the base (root) collation data, ported from genuca's CollationBaseDataBuilder. Extends the
// shared builder with the root-only pieces: the initial trie setup, compressible-byte flags, the
// algorithmic Han primary ranges, the script/reordering-group boundaries, the collected root
// elements, and the compact root-elements table the writer emits into ucadata.icu.
internal sealed class CollationBaseDataBuilder : CollationDataBuilder
{
    private const int UscriptCodeLimit = 213;
    private const int UscriptUnknown = 103;
    private const int ScriptsCapacity = UscriptCodeLimit + 16;

    private readonly bool[] _compressibleBytes = new bool[256];
    private uint _numericPrimary = 0x12000000;
    private uint _firstHanPrimary;
    private uint _lastHanPrimary;
    private int _hanStep = 2;
    private readonly List<long> _rootElements = [];
    private readonly ushort[] _scriptsIndex = new ushort[ScriptsCapacity];
    private readonly ushort[] _scriptStarts = new ushort[ScriptsCapacity];
    private int _scriptStartsLength = 1;

    public CollationBaseDataBuilder(
        Func<int, int> getFcd16,
        IReadOnlyList<(int CodePoint, int DigitValue)> decimalDigits)
        : base(getFcd16, decimalDigits)
    {
    }

    public void Init()
    {
        // Not compressible: digits, Latin, Han, trail weights. Some scripts are compressible.
        _compressibleBytes[Collator.UnassignedImplicitByte] = true;

        // The base default is an unassigned-character implicit CE (including surrogates).
        Trie = new Trie2Builder(Collator.UnassignedCe32, Collator.FffdCe32);

        // Preallocate the Latin trie blocks for locality.
        for (var c = 0; c < 0x180; ++c)
        {
            Trie.Set(c, Collator.UnassignedCe32);
        }

        Trie.Set(0xFFFE, Collator.MergeSeparatorCe32);

        var hangulCe32 = Collator.MakeCe32FromTagAndIndex(Collator.HangulTag, 0);
        Trie.SetRange(Hangul.HangulBase, Hangul.HangulEnd, hangulCe32, true);

        // The first-unassigned boundary (AlphabeticIndex overflow), a U+FDD1 U+FDD0 contraction.
        var ce = Collator.MakeCe(Collator.FirstUnassignedPrimary);
        Add("", "﷑﷐", [ce], 1);

        // A tailoring boundary, but not a mapping, for [first trailing].
        _rootElements.Add(Collator.MakeCe(Collator.FirstTrailingPrimary));

        // U+FFFD: third-highest primary, for predictable ill-formed-UTF-8 handling.
        var ce32 = Collator.FffdCe32;
        Trie.Set(Unicode.ReplacementCharacter, ce32);
        AddRootElement(Collator.CeFromSimpleCe32(ce32));

        // U+FFFF: highest primary.
        ce32 = Collator.MaxRegularCe32;
        Trie.Set(Unicode.MaxBmpCodePoint, ce32);
        AddRootElement(Collator.CeFromSimpleCe32(ce32));
    }

    public void SetNumericPrimary(uint np)
    {
        _numericPrimary = np;
    }

    public override bool IsCompressibleLeadByte(uint b)
    {
        return _compressibleBytes[b];
    }

    public void SetCompressibleLeadByte(uint b)
    {
        _compressibleBytes[b] = true;
    }

    // Sets the Han ranges as offset-CE32 ranges. ranges are (start, end) pairs in collation order.
    public void InitHanRanges(int[] ranges)
    {
        if (ranges.Length == 0)
        {
            return;
        }
        if ((ranges.Length & 1) != 0)
        {
            throw new ArgumentException("incomplete Han start/end pairs", nameof(ranges));
        }
        if (IsAssigned(0x4E00))
        {
            throw new InvalidOperationException("Han ranges already set");
        }
        var numHanCodePoints = 0;
        for (var i = 0; i < ranges.Length; i += 2)
        {
            numHanCodePoints += ranges[i + 1] - ranges[i] + 1;
        }
        // Multiply by (gap+1), plus hanStep+2 for tailoring after the last Han character.
        const int gap = 1;
        _hanStep = gap + 1;
        var numHan = numHanCodePoints * _hanStep + _hanStep + 2;
        var numHanPerLeadByte = 254 * 254;
        var numHanLeadBytes = (numHan + numHanPerLeadByte - 1) / numHanPerLeadByte;
        var hanPrimary = (uint)(Collator.UnassignedImplicitByte - numHanLeadBytes) << 24;
        hanPrimary |= 0x20200;
        _firstHanPrimary = hanPrimary;
        for (var i = 0; i < ranges.Length; i += 2)
        {
            hanPrimary = SetPrimaryRangeAndReturnNext(ranges[i], ranges[i + 1], hanPrimary, _hanStep);
        }
        // One past the actual last one; harmless for tailoring.
        _lastHanPrimary = hanPrimary;
    }

    public static int DiffTwoBytePrimaries(uint p1, uint p2, bool isCompressible)
    {
        if ((p1 & 0xFF000000) == (p2 & 0xFF000000))
        {
            return (int)(p2 - p1) >> 16;
        }
        int linear1;
        int linear2;
        int factor;
        if (isCompressible)
        {
            linear1 = (int)((p1 >> 16) & 0xFF) - 4;
            linear2 = (int)((p2 >> 16) & 0xFF) - 4;
            factor = 251;
        }
        else
        {
            linear1 = (int)((p1 >> 16) & 0xFF) - 2;
            linear2 = (int)((p2 >> 16) & 0xFF) - 2;
            factor = 254;
        }
        linear1 += factor * (int)((p1 >> 24) & 0xFF);
        linear2 += factor * (int)((p2 >> 24) & 0xFF);
        return linear2 - linear1;
    }

    public static int DiffThreeBytePrimaries(uint p1, uint p2, bool isCompressible)
    {
        if ((p1 & 0xFFFF0000) == (p2 & 0xFFFF0000))
        {
            return (int)(p2 - p1) >> 8;
        }
        var linear1 = (int)((p1 >> 8) & 0xFF) - 2;
        var linear2 = (int)((p2 >> 8) & 0xFF) - 2;
        int factor;
        if (isCompressible)
        {
            linear1 += 254 * ((int)((p1 >> 16) & 0xFF) - 4);
            linear2 += 254 * ((int)((p2 >> 16) & 0xFF) - 4);
            factor = 251 * 254;
        }
        else
        {
            linear1 += 254 * ((int)((p1 >> 16) & 0xFF) - 2);
            linear2 += 254 * ((int)((p2 >> 16) & 0xFF) - 2);
            factor = 254 * 254;
        }
        linear1 += factor * (int)((p1 >> 24) & 0xFF);
        linear2 += factor * (int)((p2 >> 24) & 0xFF);
        return linear2 - linear1;
    }

    // Each base mapping's CEs also become root elements.
    public override uint EncodeCEs(long[] ces, int cesLength)
    {
        AddRootElements(ces, cesLength);
        return base.EncodeCEs(ces, cesLength);
    }

    public void AddRootElements(long[] ces, int cesLength)
    {
        for (var i = 0; i < cesLength; ++i)
        {
            AddRootElement(ces[i]);
        }
    }

    public void AddRootElement(long ce)
    {
        if (ce == 0)
        {
            return;
        }
        // Remove case bits.
        ce &= unchecked((long)0xFFFFFFFFFFFF3FFFUL);
        var p = (uint)(ce >> 32);
        var secTer = (uint)ce;
        // Ignore a CE with a Han primary and common sec/ter; it is added with the Han ranges.
        if (_firstHanPrimary <= p && p <= _lastHanPrimary)
        {
            if (secTer < Collator.CommonSecAndTerCe)
            {
                throw new InvalidOperationException("Han primary with below-common sec/ter weights");
            }
            if (secTer == Collator.CommonSecAndTerCe)
            {
                return;
            }
        }
        if (secTer != Collator.CommonSecAndTerCe)
        {
            var s = secTer >> 16;
            var t = secTer & Collator.OnlyTertiaryMask;
            if ((s != 0 && s <= Collator.BeforeWeight16) || (t != 0 && t <= Collator.BeforeWeight16))
            {
                throw new InvalidOperationException("root element secondary/tertiary weight too low");
            }
        }
        if ((p & 0xFF) != 0)
        {
            throw new InvalidOperationException("root element primary has more than 3 bytes");
        }
        var i = BinarySearch(_rootElements, ce);
        if (i < 0)
        {
            _rootElements.Insert(~i, ce);
        }
    }

    public void AddScriptStart(int script, uint p)
    {
        // Round the primary down to its two-byte prefix.
        p >>= 8;
        p >>= 8;
        var lowestP2 = _compressibleBytes[p >> 8] ? 4u : 2u;
        if ((p & 0xFF) == lowestP2)
        {
            p &= 0xFF00;
        }
        // Script starts are added in ascending order.
        if (script < CollationData.UcolReorderCodeFirst)
        {
            // 0 <= script < UscriptCodeLimit
        }
        else
        {
            script = UscriptCodeLimit + script - CollationData.UcolReorderCodeFirst;
        }
        if (_scriptStartsLength != 0 && _scriptStarts[_scriptStartsLength - 1] == p)
        {
            // Two scripts share a range (e.g. Hira & Kana).
            _scriptsIndex[script] = (ushort)(_scriptStartsLength - 1);
        }
        else
        {
            _scriptsIndex[script] = (ushort)_scriptStartsLength;
            _scriptStarts[_scriptStartsLength++] = (ushort)p;
        }
        if (script == UscriptUnknown)
        {
            // The last script start is for unassigned code points; add the limit (start of the
            // trailing-weights range).
            _scriptStarts[_scriptStartsLength++] = (ushort)((Collator.FirstTrailingPrimary >> 16) & 0xFF00);
        }
    }

    public override void Build(CollationData data)
    {
        BuildMappings(data);
        data.NumericPrimary = _numericPrimary;
        data.CompressibleBytes = _compressibleBytes;

        var numScripts = UscriptCodeLimit;
        while (numScripts > 0 && _scriptsIndex[numScripts - 1] == 0)
        {
            --numScripts;
        }
        // Move the 16 special groups down for contiguous storage after the scripts.
        for (var i = 0; i < 16; ++i)
        {
            _scriptsIndex[numScripts + i] = _scriptsIndex[UscriptCodeLimit + i];
        }
        data.NumScripts = numScripts;
        data.ScriptsIndex = _scriptsIndex;
        data.ScriptStarts = _scriptStarts;
        data.ScriptStartsLength = _scriptStartsLength;
        BuildFastLatinTable(data);
    }

    // Builds the compact root-elements table, appending to the caller-prepared table (which already
    // holds IX_COUNT header slots).
    public void BuildRootElementsTable(List<int> table)
    {
        // Limit sentinel for root elements; reduces runtime range checks.
        _rootElements.Add(Collator.MakeCe(CollationRootElements.PrimarySentinel));
        var nextHanPrimary = _firstHanPrimary; // 0xffffffff after the last Han range
        uint prevPrimary = 0;
        var needCommonSecTerUnit = false;
        var hasDeltaUnit = false;
        for (var i = 0; i < _rootElements.Count; ++i)
        {
            var ce = _rootElements[i];
            var p = (uint)(ce >> 32);
            var secTer = (uint)ce & Collator.OnlySecTerMask;
            if ((p != prevPrimary || secTer > Collator.CommonSecAndTerCe) && needCommonSecTerUnit)
            {
                table.Add((int)(Collator.CommonSecAndTerCe | CollationRootElements.SecTerDeltaFlag));
            }
            if (p != prevPrimary)
            {
                if (p >= nextHanPrimary)
                {
                    // Add a Han primary or range.
                    if (p == nextHanPrimary)
                    {
                        table.Add((int)p);
                        nextHanPrimary = p < _lastHanPrimary
                            ? Collator.IncThreeBytePrimaryByOffset(p, false, _hanStep)
                            : 0xFFFFFFFF;
                    }
                    else
                    {
                        table.Add((int)nextHanPrimary);
                        if (nextHanPrimary == _lastHanPrimary)
                        {
                            nextHanPrimary = 0xFFFFFFFF;
                            table.Add((int)p);
                        }
                        else if (p < _lastHanPrimary)
                        {
                            table.Add((int)p | _hanStep);
                            nextHanPrimary = Collator.IncThreeBytePrimaryByOffset(p, false, _hanStep);
                        }
                        else if (p == _lastHanPrimary)
                        {
                            table.Add((int)p | _hanStep);
                            nextHanPrimary = 0xFFFFFFFF;
                        }
                        else
                        {
                            table.Add((int)_lastHanPrimary | _hanStep);
                            nextHanPrimary = 0xFFFFFFFF;
                            table.Add((int)p);
                        }
                    }
                }
                else if (prevPrimary != 0 && !hasDeltaUnit
                    && secTer == Collator.CommonSecAndTerCe)
                {
                    var end = WriteRootElementsRange(prevPrimary, p, i + 1, table);
                    if (end != 0)
                    {
                        ce = _rootElements[end];
                        p = (uint)(ce >> 32);
                        secTer = (uint)ce & Collator.OnlySecTerMask;
                        i = end;
                    }
                    else
                    {
                        table.Add((int)p);
                    }
                }
                else
                {
                    table.Add((int)p);
                }
                prevPrimary = p;
                needCommonSecTerUnit = false;
                hasDeltaUnit = false;
            }
            if (secTer == Collator.CommonSecAndTerCe && !needCommonSecTerUnit)
            {
                // The common sec/ter weights are implied in the primary unit.
            }
            else
            {
                if (secTer < Collator.CommonSecAndTerCe)
                {
                    needCommonSecTerUnit = p != 0;
                }
                else if (secTer == Collator.CommonSecAndTerCe)
                {
                    needCommonSecTerUnit = false;
                }
                table.Add((int)(secTer | CollationRootElements.SecTerDeltaFlag));
                hasDeltaUnit = true;
            }
        }
    }

    private int WriteRootElementsRange(uint prevPrimary, uint p, int i, List<int> table)
    {
        if (i >= _rootElements.Count)
        {
            return 0;
        }
        // No ranges of single-byte primaries.
        if ((p & prevPrimary & 0xFF0000) == 0)
        {
            return 0;
        }
        var isCompressible = IsCompressiblePrimary(p);
        if ((isCompressible || IsCompressiblePrimary(prevPrimary))
            && (p & 0xFF000000) != (prevPrimary & 0xFF000000))
        {
            return 0;
        }
        bool twoBytes;
        int step;
        if ((p & 0xFF00) == 0)
        {
            if ((prevPrimary & 0xFF00) != 0)
            {
                return 0; // length mismatch
            }
            twoBytes = true;
            step = DiffTwoBytePrimaries(prevPrimary, p, isCompressible);
        }
        else
        {
            if ((prevPrimary & 0xFF00) == 0)
            {
                return 0; // length mismatch
            }
            twoBytes = false;
            step = DiffThreeBytePrimaries(prevPrimary, p, isCompressible);
        }
        if (step > CollationRootElements.PrimaryStepMask)
        {
            return 0;
        }
        var end = 0;
        for (;;)
        {
            prevPrimary = p;
            var nextPrimary = twoBytes
                ? Collator.IncTwoBytePrimaryByOffset(p, isCompressible, step)
                : Collator.IncThreeBytePrimaryByOffset(p, isCompressible, step);
            var ce = _rootElements[i];
            p = (uint)(ce >> 32);
            var secTer = (uint)ce & Collator.OnlySecTerMask;
            if (p != nextPrimary
                || ((p & 0xFF000000) != (prevPrimary & 0xFF000000)
                    && (isCompressible || IsCompressiblePrimary(p))))
            {
                p = prevPrimary;
                break;
            }
            end = i++;
            if (secTer != Collator.CommonSecAndTerCe || i >= _rootElements.Count)
            {
                break;
            }
        }
        if (end != 0)
        {
            table.Add((int)p | step);
        }
        return end;
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
