namespace NlsDataGenerator.Collation;

// The reorder-range computation, ported from CollationData::makeReorderRanges (collationdata.cpp).
// Given a list of reorder codes (special groups, scripts, or "others"=USCRIPT_UNKNOWN), it permutes
// the primary-weight lead bytes and produces (limit, lead-byte-offset) range pairs that CollationSettings
// turns into the 256-byte reorder table. The script-group boundaries come from this data's
// scriptStarts/scriptsIndex (a tailoring shares the base's). Only used at build time by setReordering.
internal sealed partial class CollationData
{
    public void MakeReorderRanges(int[] reorder, int length, List<int> ranges)
    {
        MakeReorderRanges(reorder, length, false, ranges);
    }

    private void MakeReorderRanges(int[] reorder, int length, bool latinMustMove, List<int> ranges)
    {
        ranges.Clear();
        if (length == 0 || (length == 1 && reorder[0] == UscriptUnknown))
        {
            return;
        }

        // Maps each script-or-group range to a new lead byte.
        var table = new byte[MaxNumScriptRanges];

        // "Don't care" values for the reserved ranges around Latin.
        var idx = ScriptsIndex[NumScripts + ReorderReservedBeforeLatin - UcolReorderCodeFirst];
        if (idx != 0)
        {
            table[idx] = 0xff;
        }
        idx = ScriptsIndex[NumScripts + ReorderReservedAfterLatin - UcolReorderCodeFirst];
        if (idx != 0)
        {
            table[idx] = 0xff;
        }

        // Never reorder the special low and high primary lead bytes.
        var lowStart = (int)ScriptStarts[1];
        var highLimit = (int)ScriptStarts[ScriptStartsLength - 1];

        // The set of special reorder codes present in the input.
        uint specials = 0;
        for (var i = 0; i < length; ++i)
        {
            var reorderCode = reorder[i] - UcolReorderCodeFirst;
            if (reorderCode >= 0 && reorderCode < MaxNumSpecialReorderCodes)
            {
                specials |= 1u << reorderCode;
            }
        }

        // Start with the special low reorder codes that do not occur in the input.
        for (var i = 0; i < MaxNumSpecialReorderCodes; ++i)
        {
            var index = ScriptsIndex[NumScripts + i];
            if (index != 0 && (specials & (1u << i)) == 0)
            {
                lowStart = AddLowScriptRange(table, index, lowStart);
            }
        }

        // Skip the reserved range before Latin if Latin is first, to avoid moving it unnecessarily.
        var skippedReserved = 0;
        if (specials == 0 && reorder[0] == UscriptLatin && !latinMustMove)
        {
            var index = ScriptsIndex[UscriptLatin];
            var start = (int)ScriptStarts[index];
            skippedReserved = start - lowStart;
            lowStart = start;
        }

        var originalLength = length;
        var hasReorderToEnd = false;
        for (var i = 0; i < length;)
        {
            var script = reorder[i++];
            if (script == UscriptUnknown)
            {
                // Put the remaining scripts at the top.
                hasReorderToEnd = true;
                while (i < length)
                {
                    script = reorder[--length];
                    if (script == UscriptUnknown || script == UcolReorderCodeDefault)
                    {
                        throw new InvalidOperationException("invalid reorder code list");
                    }
                    var index2 = GetScriptIndex(script);
                    if (index2 == 0)
                    {
                        continue;
                    }
                    if (table[index2] != 0)
                    {
                        throw new InvalidOperationException("duplicate or equivalent reorder script");
                    }
                    highLimit = AddHighScriptRange(table, index2, highLimit);
                }
                break;
            }
            if (script == UcolReorderCodeDefault)
            {
                throw new InvalidOperationException("invalid reorder code list");
            }
            var index = GetScriptIndex(script);
            if (index == 0)
            {
                continue;
            }
            if (table[index] != 0)
            {
                throw new InvalidOperationException("duplicate or equivalent reorder script");
            }
            lowStart = AddLowScriptRange(table, index, lowStart);
        }

        // Put all remaining scripts into the middle.
        for (var i = 1; i < ScriptStartsLength - 1; ++i)
        {
            if (table[i] != 0)
            {
                continue;
            }
            var start = (int)ScriptStarts[i];
            if (!hasReorderToEnd && start > lowStart)
            {
                lowStart = start;
            }
            lowStart = AddLowScriptRange(table, i, lowStart);
        }
        if (lowStart > highLimit)
        {
            if (lowStart - (skippedReserved & 0xff00) <= highLimit)
            {
                // Try again, this time moving the before-Latin reserved range.
                MakeReorderRanges(reorder, originalLength, true, ranges);
                return;
            }
            throw new InvalidOperationException("too many reorder lead bytes needed");
        }

        // Turn lead bytes into (limit, lead-byte-offset) pairs: upper 16 bits = limit, lower 16 = offset.
        var offset = 0;
        for (var i = 1; ; ++i)
        {
            var nextOffset = offset;
            while (i < ScriptStartsLength - 1)
            {
                int newLeadByte = table[i];
                if (newLeadByte != 0xff)
                {
                    nextOffset = newLeadByte - (ScriptStarts[i] >> 8);
                    if (nextOffset != offset)
                    {
                        break;
                    }
                }
                ++i;
            }
            if (offset != 0 || i < ScriptStartsLength - 1)
            {
                ranges.Add((ScriptStarts[i] << 16) | (offset & 0xffff));
            }
            if (i == ScriptStartsLength - 1)
            {
                break;
            }
            offset = nextOffset;
        }
    }

    private int AddLowScriptRange(byte[] table, int index, int lowStart)
    {
        var start = (int)ScriptStarts[index];
        if ((start & 0xff) < (lowStart & 0xff))
        {
            lowStart += 0x100;
        }
        table[index] = (byte)(lowStart >> 8);
        var limit = (int)ScriptStarts[index + 1];
        return ((lowStart & 0xff00) + ((limit & 0xff00) - (start & 0xff00))) | (limit & 0xff);
    }

    private int AddHighScriptRange(byte[] table, int index, int highLimit)
    {
        var limit = (int)ScriptStarts[index + 1];
        if ((limit & 0xff) > (highLimit & 0xff))
        {
            highLimit -= 0x100;
        }
        var start = (int)ScriptStarts[index];
        highLimit = ((highLimit & 0xff00) - ((limit & 0xff00) - (start & 0xff00))) | (start & 0xff);
        table[index] = (byte)(highLimit >> 8);
        return highLimit;
    }
}
