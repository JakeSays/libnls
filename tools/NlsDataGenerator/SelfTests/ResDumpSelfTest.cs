using NlsDataGenerator.ResourceBundle;

namespace NlsDataGenerator.SelfTests;

// Dumps the root-table string values of an arbitrary .res (e.g. icuver.res), to read version metadata
// such as the CLDRVersion an ICU .dat package was built from.
internal static class ResDumpSelfTest
{
    public static int Run(string resPath)
    {
        var reader = new ResourceBundleReader(File.ReadAllBytes(resPath));
        foreach (var (key, word) in reader.ReadTable(reader.RootWord))
        {
            string value;
            try
            {
                value = ResourceType.GetType(word) == ResourceType.StringV2
                    ? $"\"{reader.ReadString(word)}\""
                    : $"(type {ResourceType.GetType(word)})";
            }
            catch (Exception error)
            {
                value = $"(unreadable: {error.Message})";
            }
            Console.WriteLine($"{key} = {value}");
        }
        return 0;
    }
}
