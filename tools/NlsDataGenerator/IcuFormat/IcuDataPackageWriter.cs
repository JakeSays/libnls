namespace NlsDataGenerator.IcuFormat;

// Packages the generated ICU data items into a single common-data file (<packageName>.dat), the
// "CmnD" Table-of-Contents format the vendored ICU loads via udata. Byte-faithful to ICU's
// gencmn / udata_create (icu4c tools/gencmn + tools/toolutil/unewdata.cpp): each item keeps its
// own DataHeader and is stored verbatim, 16-aligned, in sorted name order, behind an offset ToC.
//
// The package is named for the CLDR data version it carries ("cldr-48.2"), not the vendored ICU
// code version: the data artifact's identity tracks the data, so upgrading the (owned) ICU code
// does not force a rename. Tree-entry names are prefixed with "<packageName>/".
internal sealed class IcuDataPackageWriter
{
    // ICU's standard data header copyright comment (uvernum.h U_COPYRIGHT_STRING, leading and
    // trailing space included). It fills the package header padding and fixes its size at 144.
    private const string CopyrightComment =
        " Copyright (C) 2016 and later: Unicode, Inc. and others. License & terms of use: http://www.unicode.org/copyright.html ";

    // Every data item is 16-aligned within the package (udata mmap requirement).
    private const int ItemAlignment = 16;

    private readonly string _packageName;

    // Tree path (e.g. "ucadata.icu", "coll/de.res") -> item bytes, in insertion order. Build()
    // sorts by the prefixed name, so insertion order does not matter.
    private readonly List<(string TreePath, byte[] Data)> _items = [];

    public IcuDataPackageWriter(string packageName)
    {
        _packageName = packageName;
    }

    // The package name (and .dat basename) for a CLDR major.minor version, e.g. "cldr-48.2".
    public static string PackageNameForCldrVersion(string cldrVersion)
    {
        return "cldr-" + cldrVersion;
    }

    // Adds one data item under its tree path (relative to the package, without the package-name
    // prefix). The bytes are stored verbatim and must already carry their own ICU DataHeader.
    public void Add(string treePath, byte[] data)
    {
        _items.Add((treePath, data));
    }

    public int Count => _items.Count;

    // Serializes the whole package to the .dat byte image.
    public byte[] Build()
    {
        // gencmn sorts the items by their full prefixed basename via C strcmp; for ASCII names
        // that is ordinal byte order.
        var sorted = _items
            .Select(item => (Name: _packageName + "/" + item.TreePath, item.Data))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        var count = (uint)sorted.Count;

        // Offsets are relative to the ToC base (the byte after the header): count (4) then the
        // entry array (8 per item), then the name pool, then the 16-aligned data items.
        var nameBase = 4u + 8u * count;
        var nameOffsets = new uint[count];
        var nameCursor = nameBase;
        for (var i = 0; i < count; i++)
        {
            nameOffsets[i] = nameCursor;
            // basenameLength includes the terminating NUL.
            nameCursor += (uint)sorted[i].Name.Length + 1u;
        }

        var dataOffsets = new uint[count];
        var dataCursor = AlignUp(nameCursor);
        for (var i = 0; i < count; i++)
        {
            dataOffsets[i] = dataCursor;
            dataCursor += AlignUp((uint)sorted[i].Data.Length);
        }

        var writer = new LittleEndianWriter();
        WriteHeader(writer);
        // The header is 144 bytes (a multiple of 16), so the writer's absolute length stays
        // congruent to the ToC-relative offsets; aligning the writer aligns the items.

        // 1. ToC table.
        writer.WriteUInt32(count);
        for (var i = 0; i < count; i++)
        {
            writer.WriteUInt32(nameOffsets[i]);
            writer.WriteUInt32(dataOffsets[i]);
        }

        // 2. Name pool: each prefixed name as NUL-terminated ASCII.
        for (var i = 0; i < count; i++)
        {
            writer.WriteAsciiString(sorted[i].Name);
            writer.WriteByte(0);
        }

        // 3. Data items, each padded to 16 with 0xaa filler (including the last, per gencmn).
        writer.AlignToFiller(ItemAlignment);
        for (var i = 0; i < count; i++)
        {
            writer.WriteBytes(sorted[i].Data);
            writer.AlignToFiller(ItemAlignment);
        }

        return writer.ToArray();
    }

    // Writes the 144-byte package DataHeader: MappedData (headerSize, magic 0xda 0x27), the
    // UDataInfo for dataFormat "CmnD", the copyright comment, NUL-padded to a multiple of 16.
    private static void WriteHeader(LittleEndianWriter writer)
    {
        const int udataInfoSize = 20;
        var commentLength = CopyrightComment.Length + 1;
        var rawHeaderSize = 4 + udataInfoSize + commentLength;
        var headerSize = (rawHeaderSize + 15) & ~15;

        // MappedData.
        writer.WriteUInt16((ushort)headerSize);
        writer.WriteMagic();

        // UDataInfo (20 bytes).
        writer.WriteUInt16(udataInfoSize);
        writer.WriteUInt16(0);
        // isBigEndian = 0 (little-endian), charsetFamily = 0 (ASCII).
        writer.WriteByte(0);
        writer.WriteByte(0);
        // sizeof(UChar) = 2, then a reserved byte.
        writer.WriteByte(2);
        writer.WriteByte(0);
        // dataFormat "CmnD", formatVersion {1,0,0,0}, dataVersion {3,0,0,0}.
        writer.WriteAsciiString("CmnD");
        writer.WriteBytes([1, 0, 0, 0]);
        writer.WriteBytes([3, 0, 0, 0]);

        // Comment + NUL, then NUL padding out to headerSize.
        writer.WriteAsciiString(CopyrightComment);
        writer.WriteByte(0);
        while (writer.Length < headerSize)
        {
            writer.WriteByte(0);
        }
    }

    private static uint AlignUp(uint value)
    {
        return (value + (ItemAlignment - 1)) & ~((uint)ItemAlignment - 1);
    }
}
