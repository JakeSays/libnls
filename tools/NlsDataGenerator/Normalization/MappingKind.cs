namespace NlsDataGenerator.Normalization;

// The kind of normalization mapping a code point has.
internal enum MappingKind
{
    None,
    Removed,
    RoundTrip,
    OneWay,
}
