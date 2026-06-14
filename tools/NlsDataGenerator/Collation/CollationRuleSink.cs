namespace NlsDataGenerator.Collation;

// The callback surface the rule parser drives, ported from CollationRuleParser::Sink. The collation
// tailoring builder implements it: each reset (&str / &[before n]str) and relation
// (prefix | str / extension at a given strength) becomes a tailored mapping, and the optional
// [suppressContractions]/[optimize] sets adjust the build. Strengths are Ucol.Primary..Identical.
internal abstract class CollationRuleSink
{
    // Adds a reset. strength is Ucol.Identical for &str, or Ucol.Primary/Secondary/Tertiary for
    // &[before n]str with n = 1/2/3.
    public abstract void AddReset(int strength, string str);

    // Adds a relation with the given strength and prefix | str / extension (prefix and extension may
    // be empty).
    public abstract void AddRelation(int strength, string prefix, string str, string extension);

    public virtual void SuppressContractions(UnicodeSet set)
    {
    }

    public virtual void Optimize(UnicodeSet set)
    {
    }
}
