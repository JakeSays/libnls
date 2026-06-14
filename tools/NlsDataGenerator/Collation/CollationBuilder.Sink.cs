namespace NlsDataGenerator.Collation;

// Sink entry points and the parseAndBuild driver for CollationBuilder. parseAndBuild drives the rule
// parser (this sink receives the reset/relation/setting callbacks), then runs makeTailoredCEs,
// closeOverComposites, and finalizeCEs before building the runtime CollationData. The reset/relation
// method bodies (the weight-graph construction) live in the .Reset/.Relation/.Closure/.Finalize partials.
internal sealed partial class CollationBuilder
{
    // Parses the rules (driving this sink) and finalizes the tailoring CollationData.
    public CollationData ParseAndBuild(string ruleString, CollationSettings settings,
        ICollationImporter? importer)
    {
        _variableTop = settings.VariableTop;
        var parser = new CollationRuleParser(_baseData, _nfd);
        parser.Parse(ruleString, settings, this, importer);
        if (!_dataBuilder.HasMappings)
        {
            return _baseData;
        }
        MakeTailoredCEs();
        CloseOverComposites();
        FinalizeCEs();
        _optimizeSet.Add(0, 0x7f);
        _optimizeSet.Add(0xc0, 0xff);
        _optimizeSet.Remove(Normalization.Hangul.HangulBase, Normalization.Hangul.HangulEnd);
        _dataBuilder.Optimize(_optimizeSet);
        if (_fastLatinEnabled)
        {
            _dataBuilder.EnableFastLatin();
        }
        var data = new CollationData();
        _dataBuilder.Build(data);
        return data;
    }

    public override void SuppressContractions(UnicodeSet set)
    {
        _dataBuilder.SuppressContractions(set);
    }

    public override void Optimize(UnicodeSet set)
    {
        _optimizeSet.AddAll(set);
    }
}
