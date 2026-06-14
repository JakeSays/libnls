namespace NlsDataGenerator.Collation;

// Which Han ordering the root collation uses, selecting between the two FractionalUCA sections:
// [Unified_Ideograph] (code-point/implicit order) and [radical] (radical-stroke order).
internal enum HanOrderKind
{
    Implicit,
    RadicalStroke,
}
