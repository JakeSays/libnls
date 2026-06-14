using System.Text;

namespace NlsDataGenerator.Collation;

// Parses CLDR's FractionalUCA.txt into the base collation builder, ported from genuca's
// parseFractionalUCA/readAnElement/readAnOption/parseCE. Mapping lines (code points -> CEs) are
// added to the builder; option lines carry the Han ordering, compressible lead bytes, the fixed
// secondary/tertiary boundary bytes, and the UCA version. After the lines are read it detects
// implicit primary ranges and sets them as offset CEs.
internal sealed partial class FractionalUcaParser
{
    private readonly CollationBaseDataBuilder _builder;
    private readonly Dictionary<int, int> _sampleCharToScript;
    private readonly HanOrderKind _hanOrderKind;

    private readonly int _invalidScript;
    private readonly int _unknownScript;
    private readonly int _hiraganaScript;
    private readonly int _katakanaOrHiraganaScript;
    private readonly int _hanScript;
    private readonly int _simplifiedHanScript;
    private readonly int _traditionalHanScript;

    private readonly Dictionary<string, int> _fixedBytes = new(StringComparer.Ordinal);
    private HanOrder? _implicitHanOrder;
    private HanOrder? _radicalStrokeOrder;
    private int _maxCodePoint;

    public int[] UcaVersion { get; } = [0, 0, 0, 0];

    public FractionalUcaParser(
        CollationBaseDataBuilder builder,
        Dictionary<int, int> sampleCharToScript,
        IReadOnlyDictionary<string, int> uscript,
        HanOrderKind hanOrderKind)
    {
        _builder = builder;
        _sampleCharToScript = sampleCharToScript;
        _hanOrderKind = hanOrderKind;
        _invalidScript = uscript["USCRIPT_INVALID_CODE"];
        _unknownScript = uscript["USCRIPT_UNKNOWN"];
        _hiraganaScript = uscript["USCRIPT_HIRAGANA"];
        _katakanaOrHiraganaScript = uscript["USCRIPT_KATAKANA_OR_HIRAGANA"];
        _hanScript = uscript["USCRIPT_HAN"];
        _simplifiedHanScript = uscript["USCRIPT_SIMPLIFIED_HAN"];
        _traditionalHanScript = uscript["USCRIPT_TRADITIONAL_HAN"];
    }

    public int FixedByte(string name)
    {
        return _fixedBytes[name];
    }

    public void Parse(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            ReadElement(rawLine);
        }
        DetectImplicitRanges();
    }

    private void ReadElement(string line)
    {
        if (line.Length == 0 || line[0] == '#')
        {
            return;
        }
        if (line[0] == '[')
        {
            ReadOption(line);
            return;
        }

        var semi = line.IndexOf(';');
        if (semi < 0)
        {
            return; // probably a blank/whitespace line
        }
        var codePointField = line[..semi];
        var prefix = "";
        string chars;
        var pipe = codePointField.IndexOf('|');
        if (pipe >= 0)
        {
            prefix = ParseHexCodePoints(codePointField[..pipe]);
            chars = ParseHexCodePoints(codePointField[(pipe + 1)..]);
        }
        else
        {
            chars = ParseHexCodePoints(codePointField);
        }

        var rest = line[(semi + 1)..];
        var hash = rest.IndexOf('#');
        if (hash >= 0)
        {
            rest = rest[..hash];
        }
        var ces = new List<long>();
        var pos = 0;
        for (;;)
        {
            pos = SkipSpaces(rest, pos);
            if (pos >= rest.Length)
            {
                break;
            }
            ces.Add(ParseCe(rest, ref pos));
        }

        ApplyElement(prefix, chars, ces);
    }

    // Decides what to do with a parsed (prefix, chars, CEs): script/group boundaries (FDD1),
    // inverse-only root elements (FDD0), or a normal mapping.
    private void ApplyElement(string prefix, string chars, List<long> ces)
    {
        var cesArray = ces.ToArray();
        var p = (uint)(ces[0] >> 32);

        if (chars.Length > 1 && chars[0] == 0xFDD0)
        {
            // Contractions starting with U+FDD0 only feed the inverse table (root elements).
            _builder.AddRootElements(cesArray, cesArray.Length);
            if (chars.Length == 2 && ces.Count == 1)
            {
                switch (chars[1])
                {
                    case (char)0x34:
                        _builder.SetNumericPrimary(p);
                        break;
                    case (char)0xFF21:
                        _builder.AddScriptStart(CollationData.ReorderReservedBeforeLatin, p);
                        break;
                    case (char)0xFF3A:
                        _builder.AddScriptStart(CollationData.ReorderReservedAfterLatin, p);
                        break;
                }
            }
            return;
        }

        var c = char.ConvertToUtf32(chars, 0);
        if (c > _maxCodePoint)
        {
            _maxCodePoint = c;
        }
        // U+FFFD..U+FFFF and the unassigned first primary are set by the builder's init().
        if (c is >= 0xFFFD and <= Unicode.MaxBmpCodePoint)
        {
            return;
        }

        if (chars.Length >= 2 && c == 0xFDD1)
        {
            var sample = char.ConvertToUtf32(chars, 1);
            var script = GetCharScript(sample);
            if (script == _unknownScript)
            {
                // FDD1 FDD0: the first unassigned-implicit primary.
                _builder.AddScriptStart(script, Collator.FirstUnassignedPrimary);
                return;
            }
            _builder.AddScriptStart(script, p);
            if (script == _hiraganaScript)
            {
                _builder.AddScriptStart(_katakanaOrHiraganaScript, p);
            }
            else if (script == _hanScript)
            {
                _builder.AddScriptStart(_simplifiedHanScript, p);
                _builder.AddScriptStart(_traditionalHanScript, p);
            }
        }

        _builder.Add(prefix, chars, cesArray, cesArray.Length);
    }

    private int GetCharScript(int c)
    {
        return _sampleCharToScript.TryGetValue(c, out var script) ? script : _invalidScript;
    }

    // Detects ranges of code points with 3-byte primaries increasing by a consistent step, and sets
    // them as offset CE ranges (genuca's second pass over the parsed data).
    private void DetectImplicitRanges()
    {
        var rangeFirst = -1;
        var rangeLast = -1;
        uint rangeFirstPrimary = 0;
        uint rangeLastPrimary = 0;
        var rangeStep = -1;

        for (var c = 0x180; c <= _maxCodePoint + 1; ++c)
        {
            int action;
            var p = _builder.GetLongPrimaryIfSingleCe(c);
            if (p != 0)
            {
                if (rangeFirst >= 0 && c == rangeLast + 1 && p > rangeLastPrimary)
                {
                    var step = CollationBaseDataBuilder.DiffThreeBytePrimaries(
                        rangeLastPrimary, p, _builder.IsCompressiblePrimary(p));
                    if (rangeFirst == rangeLast && step >= 2)
                    {
                        rangeStep = step;
                        rangeLast = c;
                        rangeLastPrimary = p;
                        action = 0;
                    }
                    else if (rangeStep == step)
                    {
                        rangeLast = c;
                        rangeLastPrimary = p;
                        action = 0;
                    }
                    else
                    {
                        action = 1;
                    }
                }
                else
                {
                    action = 1;
                }
            }
            else
            {
                action = -1;
            }
            if (action != 0 && rangeFirst >= 0)
            {
                _builder.MaybeSetPrimaryRange(rangeFirst, rangeLast, rangeFirstPrimary, rangeStep);
                rangeFirst = -1;
                rangeStep = -1;
            }
            if (action > 0)
            {
                rangeFirst = c;
                rangeLast = c;
                rangeFirstPrimary = p;
                rangeLastPrimary = p;
            }
        }
    }
}
