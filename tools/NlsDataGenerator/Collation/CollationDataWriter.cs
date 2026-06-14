using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Collation;

// Serializes the root CollationData, ported from CollationDataWriter::writeBase (the isBase path).
// Produces the body that follows the udata header: the indexes[] table then each data section in
// descending unit-size order (trie, CEs, CE32s, root elements, contexts, unsafe-backward set,
// fast-Latin table, scripts, compressible bytes). The caller prepends the "UCol" udata header.
internal static class CollationDataWriter
{

    // Serializes a tailoring CollationData into the %%CollationBin blob, ported from
    // CollationDataWriter::writeTailoring (the !isBase path). Unlike the base it writes an inline
    // 24-byte (or 28-byte, padded so the 64-bit CEs are 8-aligned) "UCol" header, a shortened
    // indexes[] (only up to the last present data item), only the new unsafe-backward set
    // (data minus base), and the fast-Latin table only if it differs from the base's.
    public static byte[] WriteTailoring(CollationData data, CollationSettings settings, byte[] rulesVersion)
    {
        if (data.Base is null)
        {
            // No tailored mappings: the tailoring's data aliases the root (CollationBuilder sets
            // tailoring->data = baseData). Only the options and any reordering are written.
            return WriteSettingsOnlyTailoring(data, settings, rulesVersion);
        }

        var baseData = data.Base!;
        var trieBytes = data.Trie.Freeze(Trie2ValueBits.Bits32);
        var fastLatinVersion = data.FastLatinTable is not null
            ? CollationFastLatinFormat.Version << 16
            : 0;

        var unsafeSet = new UnicodeSet();
        unsafeSet.AddAll(data.UnsafeBackwardSet!);
        unsafeSet.RemoveAll(baseData.UnsafeBackwardSet!);
        var unsafeSerialized = unsafeSet.IsEmpty ? [] : unsafeSet.Serialize();

        // Reorder codes, followed by the full range pairs when the lead-byte table has split bytes
        // (the runtime rebuilds the table from the ranges in that case). The ranges come from the base
        // script data (the tailoring shares it).
        var reorderCodes = settings.ReorderCodes;
        var reorderCodesLength = settings.ReorderCodesLength;
        if (settings.HasReordering && CollationSettings.ReorderTableHasSplitBytes(settings.ReorderTable!))
        {
            var codesAndRanges = new List<int>();
            baseData.MakeReorderRanges(settings.ReorderCodes, settings.ReorderCodesLength, codesAndRanges);
            for (var i = 0; i < settings.ReorderCodesLength; ++i)
            {
                codesAndRanges.Insert(i, settings.ReorderCodes[i]);
            }
            reorderCodes = [.. codesAndRanges];
            reorderCodesLength = reorderCodes.Length;
        }

        var fastLatinTableLength = 0;
        var indexesLength = CollationIndex.Ce32sOffset + 2;
        if (data.ContextsLength != 0)
        {
            indexesLength = CollationIndex.ContextsOffset + 2;
        }
        if (!unsafeSet.IsEmpty)
        {
            indexesLength = CollationIndex.UnsafeBwdOffset + 2;
        }
        // Matches ICU's plain pointer comparison `data.fastLatinTable != baseData->fastLatinTable`,
        // including the null case: a search collator disables fast Latin (null table) while the base
        // has one, so the (empty) fast-Latin section is still indexed — its offset AND limit must be
        // written. Gating on `data.FastLatinTable is not null` would drop the limit and truncate the
        // indexes for search tailorings.
        if (!ReferenceEquals(data.FastLatinTable, baseData.FastLatinTable))
        {
            fastLatinTableLength = data.FastLatinTableLength;
            indexesLength = CollationIndex.FastLatinTableOffset + 2;
        }

        var headerSize = 24;
        if (data.CesLength != 0)
        {
            // The CEs must be 8-aligned; the reorder codes precede them and are not auto-aligned.
            var sum = headerSize + (indexesLength + reorderCodesLength) * 4;
            if ((sum & 7) != 0)
            {
                headerSize += 4;
            }
        }

        var indexes = new int[CollationIndex.Count];
        indexes[CollationIndex.IndexesLength] = indexesLength;
        indexes[CollationIndex.Options] =
            (int)(data.NumericPrimary | (uint)fastLatinVersion | (uint)settings.Options);
        var totalSize = indexesLength * 4;
        indexes[CollationIndex.JamoCe32sStart] = data.JamoCe32sStart;
        indexes[CollationIndex.ReorderCodesOffset] = totalSize;
        totalSize += reorderCodesLength * 4;
        indexes[CollationIndex.ReorderTableOffset] = totalSize;
        if (settings.ReorderTable is not null)
        {
            totalSize += 256;
        }
        indexes[CollationIndex.TrieOffset] = totalSize;
        totalSize += trieBytes.Length;
        indexes[CollationIndex.Reserved8Offset] = totalSize;
        indexes[CollationIndex.CesOffset] = totalSize;
        totalSize += data.CesLength * 8;
        indexes[CollationIndex.Reserved10Offset] = totalSize;
        indexes[CollationIndex.Ce32sOffset] = totalSize;
        totalSize += data.Ce32sLength * 4;
        indexes[CollationIndex.RootElementsOffset] = totalSize;
        indexes[CollationIndex.ContextsOffset] = totalSize;
        totalSize += data.ContextsLength * 2;
        indexes[CollationIndex.UnsafeBwdOffset] = totalSize;
        totalSize += unsafeSerialized.Length * 2;
        indexes[CollationIndex.FastLatinTableOffset] = totalSize;
        totalSize += fastLatinTableLength * 2;
        indexes[CollationIndex.ScriptsOffset] = totalSize;
        indexes[CollationIndex.CompressibleBytesOffset] = totalSize;
        indexes[CollationIndex.Reserved18Offset] = totalSize;
        indexes[CollationIndex.TotalSize] = totalSize;

        var version = MakeTailoringVersion(baseData.Version, rulesVersion);
        var writer = new LittleEndianWriter();
        WriteUColHeader(writer, headerSize, version);

        writer.WriteUInt32Array(indexes.AsSpan(0, indexesLength));
        writer.WriteUInt32Array(reorderCodes.AsSpan(0, reorderCodesLength));
        if (settings.ReorderTable is not null)
        {
            writer.WriteBytes(settings.ReorderTable);
        }
        writer.WriteBytes(trieBytes);
        writer.WriteUInt64Array(data.Ces.AsSpan(0, data.CesLength));
        writer.WriteUInt32Array(data.Ce32s.AsSpan(0, data.Ce32sLength));
        writer.WriteCharArray(data.Contexts.AsSpan(0, data.ContextsLength));
        writer.WriteUInt16Array(unsafeSerialized);
        if (data.FastLatinTable is not null)
        {
            writer.WriteUInt16Array(data.FastLatinTable.AsSpan(0, fastLatinTableLength));
        }
        return writer.ToArray();
    }

    // The 24-byte (or 28-byte, padded) inline "UCol" udata header that precedes a tailoring blob.
    private static void WriteUColHeader(LittleEndianWriter writer, int headerSize, byte[] version)
    {
        writer.WriteUInt16((ushort)headerSize);
        writer.WriteByte(0xDA);
        writer.WriteByte(0x27);
        writer.WriteUInt16(20);
        writer.WriteUInt16(0);
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteByte(2);
        writer.WriteByte(0);
        writer.WriteAsciiString("UCol");
        writer.WriteBytes([5, 0, 0, 0]);
        writer.WriteBytes(version);
        writer.WritePadding(headerSize - 24);
    }

    // A tailoring with no mappings (only options/reordering); its data aliases the root. Ported from
    // CollationDataWriter::write's baseData==nullptr branch: a 24-byte header, a short indexes table
    // (options only, or up to the reorder table), and the reorder codes/table when reordering.
    private static byte[] WriteSettingsOnlyTailoring(CollationData root, CollationSettings settings,
        byte[] rulesVersion)
    {
        var reorderCodes = settings.ReorderCodes;
        var reorderCodesLength = settings.ReorderCodesLength;
        if (settings.HasReordering && CollationSettings.ReorderTableHasSplitBytes(settings.ReorderTable!))
        {
            var codesAndRanges = new List<int>();
            root.MakeReorderRanges(settings.ReorderCodes, settings.ReorderCodesLength, codesAndRanges);
            for (var i = 0; i < settings.ReorderCodesLength; ++i)
            {
                codesAndRanges.Insert(i, settings.ReorderCodes[i]);
            }
            reorderCodes = [.. codesAndRanges];
            reorderCodesLength = reorderCodes.Length;
        }

        var indexesLength = reorderCodesLength == 0
            ? CollationIndex.Options + 1
            : CollationIndex.ReorderTableOffset + 2;
        var fastLatinVersion = root.FastLatinTable is not null
            ? CollationFastLatinFormat.Version << 16
            : 0;

        var indexes = new int[CollationIndex.Count];
        indexes[CollationIndex.IndexesLength] = indexesLength;
        indexes[CollationIndex.Options] =
            (int)(root.NumericPrimary | (uint)fastLatinVersion | (uint)settings.Options);
        indexes[CollationIndex.JamoCe32sStart] = -1;
        var totalSize = indexesLength * 4;
        indexes[CollationIndex.ReorderCodesOffset] = totalSize;
        totalSize += reorderCodesLength * 4;
        indexes[CollationIndex.ReorderTableOffset] = totalSize;
        if (settings.ReorderTable is not null)
        {
            totalSize += 256;
        }
        indexes[CollationIndex.TrieOffset] = totalSize;
        indexes[CollationIndex.TotalSize] = totalSize;

        var version = MakeTailoringVersion(root.Version, rulesVersion);
        var writer = new LittleEndianWriter();
        WriteUColHeader(writer, 24, version);
        writer.WriteUInt32Array(indexes.AsSpan(0, indexesLength));
        writer.WriteUInt32Array(reorderCodes.AsSpan(0, reorderCodesLength));
        if (settings.ReorderTable is not null)
        {
            writer.WriteBytes(settings.ReorderTable);
        }
        return writer.ToArray();
    }

    // CollationTailoring::setVersion: builder version, base UCA version, and the rules version folded in.
    private static byte[] MakeTailoringVersion(byte[] baseVersion, byte[] rulesVersion)
    {
        return
        [
            9,
            baseVersion[1],
            (byte)((baseVersion[2] & 0xc0) + ((rulesVersion[0] + (rulesVersion[0] >> 6)) & 0x3f)),
            (byte)((rulesVersion[1] << 3) + (rulesVersion[1] >> 5) + rulesVersion[2]
                + (rulesVersion[3] << 4) + (rulesVersion[3] >> 4)),
        ];
    }

    public static byte[] WriteBase(CollationData data, CollationSettings settings, int[] rootElements)
    {
        var trieBytes = data.Trie.Freeze(Trie2ValueBits.Bits32);
        var unsafeSerialized = data.UnsafeBackwardSet!.Serialize();
        var scripts = BuildScripts(data);

        var indexes = new int[CollationIndex.Count];
        indexes[CollationIndex.IndexesLength] = CollationIndex.Count;
        var fastLatinVersion = data.FastLatinTable is not null
            ? CollationFastLatinFormat.Version << 16
            : 0;
        indexes[CollationIndex.Options] =
            (int)(data.NumericPrimary | (uint)fastLatinVersion | (uint)settings.Options);
        indexes[CollationIndex.JamoCe32sStart] = data.JamoCe32sStart;

        // Byte offsets, all from the start of the indexes (after the header). The base has no
        // reorder codes or reorder table, so those offsets are empty.
        var offset = CollationIndex.Count * 4;
        indexes[CollationIndex.ReorderCodesOffset] = offset;
        indexes[CollationIndex.ReorderTableOffset] = offset;
        indexes[CollationIndex.TrieOffset] = offset;
        offset += trieBytes.Length;
        indexes[CollationIndex.Reserved8Offset] = offset;
        indexes[CollationIndex.CesOffset] = offset;
        offset += data.CesLength * 8;
        indexes[CollationIndex.Reserved10Offset] = offset;
        indexes[CollationIndex.Ce32sOffset] = offset;
        offset += data.Ce32sLength * 4;
        indexes[CollationIndex.RootElementsOffset] = offset;
        offset += rootElements.Length * 4;
        indexes[CollationIndex.ContextsOffset] = offset;
        offset += data.ContextsLength * 2;
        indexes[CollationIndex.UnsafeBwdOffset] = offset;
        offset += unsafeSerialized.Length * 2;
        indexes[CollationIndex.FastLatinTableOffset] = offset;
        offset += data.FastLatinTableLength * 2;
        indexes[CollationIndex.ScriptsOffset] = offset;
        offset += scripts.Length * 2;
        indexes[CollationIndex.CompressibleBytesOffset] = offset;
        offset += 256;
        indexes[CollationIndex.Reserved18Offset] = offset;
        indexes[CollationIndex.TotalSize] = offset;

        var writer = new LittleEndianWriter();
        writer.WriteUInt32Array(indexes);
        writer.WriteBytes(trieBytes);
        writer.WriteUInt64Array(data.Ces.AsSpan(0, data.CesLength));
        writer.WriteUInt32Array(data.Ce32s.AsSpan(0, data.Ce32sLength));
        writer.WriteUInt32Array(rootElements);
        writer.WriteCharArray(data.Contexts.AsSpan(0, data.ContextsLength));
        writer.WriteUInt16Array(unsafeSerialized);
        if (data.FastLatinTable is not null)
        {
            writer.WriteUInt16Array(data.FastLatinTable.AsSpan(0, data.FastLatinTableLength));
        }
        writer.WriteUInt16Array(scripts);
        writer.WriteBoolBytes(data.CompressibleBytes);
        return writer.ToArray();
    }

    private static ushort[] BuildScripts(CollationData data)
    {
        var scripts = new List<ushort> { (ushort)data.NumScripts };
        for (var i = 0; i < data.NumScripts + 16; ++i)
        {
            scripts.Add(data.ScriptsIndex[i]);
        }
        for (var i = 0; i < data.ScriptStartsLength; ++i)
        {
            scripts.Add(data.ScriptStarts[i]);
        }
        return [.. scripts];
    }
}
