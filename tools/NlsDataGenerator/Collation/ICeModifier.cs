namespace NlsDataGenerator.Collation;

// Rewrites CE32/CE values while copying a CollationDataBuilder, ported from
// CollationDataBuilder::CEModifier. finalizeCEs uses it to replace the builder's temporary CEs (which
// point at weight-graph nodes) with the final allocated CEs. A return of Collation.NoCe means "leave
// the value unchanged".
internal interface ICeModifier
{
    long ModifyCe32(uint ce32);

    long ModifyCe(long ce);
}
