using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.Metadata;

// Emits the metadata/cldrversion.nls item (dataFormat "NlsM") for the cldr-<version> package: an
// ICU DataHeader followed by a uint8 length and that many ASCII bytes giving the CLDR version.
// libnls reads it via udata on open, so the version is not tied to the package file name.
internal static class NlsMetadataGenerator
{
    private const string DataFormat = "NlsM";
    private static readonly byte[] FormatVersion = [1, 0, 0, 0];
    private static readonly byte[] DataVersion = [0, 0, 0, 0];

    public static byte[] Generate(string cldrVersion)
    {
        var writer = new LittleEndianWriter();
        new IcuDataHeader(DataFormat, FormatVersion, DataVersion).Write(writer);
        writer.WriteByte((byte)cldrVersion.Length);
        writer.WriteAsciiString(cldrVersion);
        return writer.ToArray();
    }
}
