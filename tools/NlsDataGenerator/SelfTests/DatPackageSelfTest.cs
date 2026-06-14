using System.Text;

namespace NlsDataGenerator.SelfTests;

// Round-trips the generated cldr-<version>.dat: re-parses the package out of the output directory
// and proves every ToC item is byte-for-byte the sidecar file it was built from, 16-aligned, with
// its own ICU DataHeader intact and 0xaa filler in the gaps. Guards the IcuDataPackageWriter format.
internal static class DatPackageSelfTest
{
    public static int Run(string outputDirectory)
    {
        var candidates = Directory.GetFiles(outputDirectory, "cldr-*.dat");
        if (candidates.Length != 1)
        {
            Console.Error.WriteLine($"FAIL: expected exactly one cldr-*.dat in {outputDirectory}, found {candidates.Length}");
            return 1;
        }
        var packagePath = candidates[0];
        // The package name is the .dat basename; ToC entries must be prefixed with "<name>/".
        var packageName = Path.GetFileNameWithoutExtension(packagePath);

        var dat = File.ReadAllBytes(packagePath);
        if (dat.Length < 4 || dat[2] != 0xDA || dat[3] != 0x27)
        {
            Console.Error.WriteLine("FAIL: package magic is not 0xDA 0x27");
            return 1;
        }
        if (dat[12] != 0x43 || dat[13] != 0x6D || dat[14] != 0x6E || dat[15] != 0x44)
        {
            Console.Error.WriteLine("FAIL: package dataFormat is not \"CmnD\"");
            return 1;
        }

        var tocBase = (int)ReadUInt16(dat, 0);
        var count = (int)ReadUInt32(dat, tocBase);

        var failures = 0;
        var previousName = string.Empty;
        for (var i = 0; i < count; i++)
        {
            var entry = tocBase + 4 + 8 * i;
            var nameOffset = (int)ReadUInt32(dat, entry);
            var dataOffset = (int)ReadUInt32(dat, entry + 4);
            var name = ReadCString(dat, tocBase + nameOffset);

            // Names must be ascending (gencmn sorts the ToC) and carry the package prefix.
            if (string.CompareOrdinal(name, previousName) <= 0)
            {
                Console.Error.WriteLine($"FAIL: ToC not sorted at {i}: {previousName!} then {name}");
                failures++;
            }
            previousName = name;

            var prefix = packageName + "/";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"FAIL: {name} lacks the {prefix} prefix");
                failures++;
                continue;
            }
            var treePath = name.Substring(prefix.Length);

            if (dataOffset % 16 != 0)
            {
                Console.Error.WriteLine($"FAIL: {treePath} dataOffset {dataOffset} is not 16-aligned");
                failures++;
            }

            var itemStart = tocBase + dataOffset;
            if (dat[itemStart + 2] != 0xDA || dat[itemStart + 3] != 0x27)
            {
                Console.Error.WriteLine($"FAIL: {treePath} item header is not 0xDA 0x27");
                failures++;
            }

            // The next entry's dataOffset (or EOF for the last item) bounds the 16-padded slot.
            var slotEnd = i + 1 < count
                ? tocBase + (int)ReadUInt32(dat, entry + 8 + 4)
                : dat.Length;

            var sidecarPath = Path.Combine(new[] { outputDirectory }.Concat(treePath.Split('/')).ToArray());
            var sidecar = File.ReadAllBytes(sidecarPath);
            if (!dat.AsSpan(itemStart, sidecar.Length).SequenceEqual(sidecar))
            {
                Console.Error.WriteLine($"FAIL: {treePath} item bytes differ from the sidecar file");
                failures++;
            }
            for (var p = itemStart + sidecar.Length; p < slotEnd; p++)
            {
                if (dat[p] != 0xAA)
                {
                    Console.Error.WriteLine($"FAIL: {treePath} padding at {p} is 0x{dat[p]:x2}, not 0xaa");
                    failures++;
                    break;
                }
            }
        }

        Console.WriteLine($"verify-dat: {count} items, {failures} failures ({packagePath})");
        return failures == 0 ? 0 : 1;
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }

    private static string ReadCString(byte[] data, int offset)
    {
        var end = offset;
        while (data[end] != 0)
        {
            end++;
        }
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }
}
