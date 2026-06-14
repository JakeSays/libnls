using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Normalization;

// Assembles nfc.nrm, mirroring n2builder.cpp's writeBinaryFile: the udata header, the indexes[]
// table, the serialized norm16 UCPTrie, the uint16 extra data, and the 256-byte small-FCD table.
internal sealed partial class NormalizationDataBuilder
{
    public byte[] Generate()
    {
        ProcessData();

        var writer = new LittleEndianWriter();
        // dataFormat "Nrm2", formatVersion 5.0.0.0, dataVersion = Unicode 17.0.0.
        new IcuDataHeader("Nrm2", [5, 0, 0, 0], [17, 0, 0, 0]).Write(writer);

        foreach (var index in _indexes)
        {
            writer.WriteUInt32((uint)index);
        }
        writer.WriteBytes(_norm16TrieBytes);
        foreach (var unit in _extraData)
        {
            writer.WriteUInt16(unit);
        }
        writer.WriteBytes(_smallFcd);

        return writer.ToArray();
    }
}
