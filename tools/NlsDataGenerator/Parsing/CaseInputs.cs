namespace NlsDataGenerator.Parsing;

// Aggregates every UCD source the ucase value computation reads, exposing per-code-point accessors
// with the UCD defaults (notably: a missing titlecase mapping defaults to the uppercase mapping).
// Simple mappings return -1 when absent; full mappings return an empty array.
internal sealed class CaseInputs
{
    private readonly Dictionary<int, UnicodeDataRecord> _unicodeData = new();
    private readonly CaseFolding _folding;
    private readonly SpecialCasing _casing;
    private readonly CodePointSet _lowercase;
    private readonly CodePointSet _uppercase;
    private readonly CodePointSet _caseIgnorable;
    private readonly CodePointSet _softDotted;

    public CaseInputs(string ucdDirectory)
    {
        foreach (var record in new UnicodeDataReader(ucdDirectory).Read())
        {
            if (!record.IsRange)
            {
                _unicodeData[record.CodePoint] = record;
            }
        }

        _folding = CaseFoldingReader.Read(Path.Combine(ucdDirectory, "CaseFolding.txt"));
        _casing = SpecialCasingReader.Read(Path.Combine(ucdDirectory, "SpecialCasing.txt"));

        var derived = PropertyListReader.Read(Path.Combine(ucdDirectory, "DerivedCoreProperties.txt"));
        var propList = PropertyListReader.Read(Path.Combine(ucdDirectory, "PropList.txt"));
        _lowercase = derived.GetValueOrDefault("Lowercase") ?? new CodePointSet();
        _uppercase = derived.GetValueOrDefault("Uppercase") ?? new CodePointSet();
        _caseIgnorable = derived.GetValueOrDefault("Case_Ignorable") ?? new CodePointSet();
        _softDotted = propList.GetValueOrDefault("Soft_Dotted") ?? new CodePointSet();
    }

    // The assigned single code points, sorted — the set the builder iterates. Code points inside
    // UnicodeData ranges (CJK, Hangul, …) carry no case data and need not be visited.
    public IEnumerable<int> AssignedCodePoints
    {
        get
        {
            var keys = _unicodeData.Keys.ToList();
            keys.Sort();
            return keys;
        }
    }

    public bool IsLowercase(int codePoint)
    {
        return _lowercase.Contains(codePoint);
    }

    public bool IsUppercase(int codePoint)
    {
        return _uppercase.Contains(codePoint);
    }

    public bool IsCaseIgnorable(int codePoint)
    {
        return _caseIgnorable.Contains(codePoint);
    }

    public bool IsSoftDotted(int codePoint)
    {
        return _softDotted.Contains(codePoint);
    }

    public string GeneralCategory(int codePoint)
    {
        if (_unicodeData.TryGetValue(codePoint, out var record))
        {
            return record.GeneralCategory;
        }
        return "";
    }

    public int CombiningClass(int codePoint)
    {
        if (_unicodeData.TryGetValue(codePoint, out var record))
        {
            return record.CombiningClass;
        }
        return 0;
    }

    public int SimpleUpper(int codePoint)
    {
        if (_unicodeData.TryGetValue(codePoint, out var record) && record.SimpleUppercase is int value)
        {
            return value;
        }
        return -1;
    }

    public int SimpleLower(int codePoint)
    {
        if (_unicodeData.TryGetValue(codePoint, out var record) && record.SimpleLowercase is int value)
        {
            return value;
        }
        return -1;
    }

    // Titlecase defaults to the uppercase mapping when the UCD titlecase field is empty.
    public int SimpleTitle(int codePoint)
    {
        if (!_unicodeData.TryGetValue(codePoint, out var record))
        {
            return -1;
        }
        if (record.SimpleTitlecase is int title)
        {
            return title;
        }
        if (record.SimpleUppercase is int upper)
        {
            return upper;
        }
        return -1;
    }

    public int SimpleFold(int codePoint)
    {
        if (_folding.SimpleFold.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return -1;
    }

    public int[] FullLower(int codePoint)
    {
        if (_casing.FullLower.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return [];
    }

    public int[] FullUpper(int codePoint)
    {
        if (_casing.FullUpper.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return [];
    }

    public int[] FullTitle(int codePoint)
    {
        if (_casing.FullTitle.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return [];
    }

    public int[] FullFold(int codePoint)
    {
        if (_folding.FullFold.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return [];
    }

    public int[] TurkicFold(int codePoint)
    {
        if (_folding.TurkicFold.TryGetValue(codePoint, out var value))
        {
            return value;
        }
        return [];
    }

    public bool HasConditional(int codePoint)
    {
        return _casing.HasConditional.Contains(codePoint);
    }
}
