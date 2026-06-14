namespace NlsDataGenerator.Collation;

// Collation options/attributes, ported from ICU's CollationSettings (collationsettings.h). The base
// (root) build uses only the default-constructed value: no reordering, and the default encoded
// options the data writer stores in indexes[IX_OPTIONS]. Tailoring-specific reordering is added
// with Phase B.
internal sealed class CollationSettings
{
    public const int CheckFcd = 1;
    public const int Numeric = 2;
    public const int Shifted = 4;
    public const int AlternateMask = 0xC;
    public const int MaxVariableShift = 4;
    public const int MaxVariableMask = 0x70;
    public const int UpperFirst = 0x100;
    public const int CaseFirst = 0x200;
    public const int CaseFirstAndUpperMask = CaseFirst | UpperFirst;
    public const int CaseLevel = 0x400;
    public const int BackwardSecondary = 0x800;
    public const int StrengthShift = 12;
    public const int StrengthMask = 0xF000;

    // maxVariable values.
    public const int MaxVarSpace = 0;
    public const int MaxVarPunct = 1;
    public const int MaxVarSymbol = 2;
    public const int MaxVarCurrency = 3;

    // UCOL_DEFAULT_STRENGTH == UCOL_TERTIARY == 2.
    private const int DefaultStrength = 2;

    // Default options: tertiary strength, maxVariable = punctuation.
    public int Options { get; set; } = (DefaultStrength << StrengthShift) | (MaxVarPunct << MaxVariableShift);

    public int[] ReorderCodes { get; set; } = [];
    public int ReorderCodesLength { get; set; }

    // 256-entry reorder table, or null when there is no reordering.
    public byte[]? ReorderTable { get; set; }

    // The full (limit, lead-byte-offset) range pairs, needed only when the table has split lead bytes.
    public uint[] ReorderRanges { get; set; } = [];
    public int ReorderRangesLength { get; set; }

    // The lowest primary weight that is never reordered (top 16 bits significant).
    public uint MinHighNoReorder { get; set; }

    public bool HasReordering => ReorderTable is not null;

    // The variable-top primary weight (set by [maxVariable]); 0 until set.
    public uint VariableTop { get; set; }

    public void SetStrength(int value)
    {
        Options = (Options & ~StrengthMask) | (value << StrengthShift);
    }

    public void SetFlag(int bit, int value)
    {
        if (value == Ucol.On)
        {
            Options |= bit;
        }
        else if (value == Ucol.Off)
        {
            Options &= ~bit;
        }
        else
        {
            // Ucol.Default: reset the bit (the parser's default options are 0 for these flags).
            Options &= ~bit;
        }
    }

    public void SetAlternateHandling(int value)
    {
        Options &= ~AlternateMask;
        if (value == Ucol.Shifted)
        {
            Options |= Shifted;
        }
    }

    public void SetMaxVariable(int value)
    {
        Options = (Options & ~MaxVariableMask) | (value << MaxVariableShift);
    }

    public void SetCaseFirst(int value)
    {
        Options &= ~CaseFirstAndUpperMask;
        if (value == Ucol.LowerFirst)
        {
            Options |= CaseFirst;
        }
        else if (value == Ucol.UpperFirst)
        {
            Options |= CaseFirstAndUpperMask;
        }
    }

    public void ResetReordering()
    {
        ReorderCodes = [];
        ReorderCodesLength = 0;
        ReorderTable = null;
        ReorderRanges = [];
        ReorderRangesLength = 0;
        MinHighNoReorder = 0;
    }

    // Builds the lead-byte reorder table from the base script ranges, ported from
    // CollationSettings::setReordering (collationsettings.cpp). Maps each primary lead byte to its
    // permuted lead byte; lead bytes split mid-range get a 0 in the table and keep their full range
    // pairs for the writer/runtime.
    public void SetReordering(CollationData baseData, int[] codes)
    {
        var codesLength = codes.Length;
        if (codesLength == 0 || (codesLength == 1 && codes[0] == Ucol.ReorderCodeNone))
        {
            ResetReordering();
            return;
        }
        var rangesList = new List<int>();
        baseData.MakeReorderRanges(codes, codesLength, rangesList);
        var rangesLength = rangesList.Count;
        if (rangesLength == 0)
        {
            ResetReordering();
            return;
        }

        // ranges[] holds (limit, offset) pairs: first offset is 0, last offset is non-zero.
        MinHighNoReorder = (uint)rangesList[rangesLength - 1] & 0xffff0000;

        var table = new byte[256];
        var b = 0;
        var firstSplitByteRangeIndex = -1;
        for (var i = 0; i < rangesLength; ++i)
        {
            var pair = (uint)rangesList[i];
            var limit1 = (int)(pair >> 24);
            while (b < limit1)
            {
                table[b] = (byte)(b + pair);
                ++b;
            }
            // The limit's second byte being non-zero means a lead byte split mid-range.
            if ((pair & 0xff0000) != 0)
            {
                table[limit1] = 0;
                b = limit1 + 1;
                if (firstSplitByteRangeIndex < 0)
                {
                    firstSplitByteRangeIndex = i;
                }
            }
        }
        while (b <= 0xff)
        {
            table[b] = (byte)b;
            ++b;
        }

        var rangesStart = 0;
        if (firstSplitByteRangeIndex < 0)
        {
            // The lead byte permutation table alone suffices.
            rangesLength = 0;
        }
        else
        {
            // Keep only the ranges from the first split byte onward.
            rangesStart = firstSplitByteRangeIndex;
            rangesLength -= firstSplitByteRangeIndex;
        }

        ReorderCodes = codes;
        ReorderCodesLength = codesLength;
        ReorderTable = table;
        var ranges = new uint[rangesLength];
        for (var i = 0; i < rangesLength; ++i)
        {
            ranges[i] = (uint)rangesList[rangesStart + i];
        }
        ReorderRanges = ranges;
        ReorderRangesLength = rangesLength;
    }

    public static bool ReorderTableHasSplitBytes(byte[] table)
    {
        for (var i = 1; i < 256; ++i)
        {
            if (table[i] == 0)
            {
                return true;
            }
        }
        return false;
    }
}
