namespace NlsDataGenerator.Collation;

// The special reset positions a rule can name with &[first/last X] (or [top]/[variable top]), ported
// from CollationRuleParser::Position. The parser encodes a position as a contraction of U+FFFE and
// (U+2800 + this ordinal); the builder maps it to the corresponding boundary CE. The order matches
// ICU's enum and the parser's position-name table.
internal enum ResetPosition
{
    FirstTertiaryIgnorable,
    LastTertiaryIgnorable,
    FirstSecondaryIgnorable,
    LastSecondaryIgnorable,
    FirstPrimaryIgnorable,
    LastPrimaryIgnorable,
    FirstVariable,
    LastVariable,
    FirstRegular,
    LastRegular,
    FirstImplicit,
    LastImplicit,
    FirstTrailing,
    LastTrailing,
}
