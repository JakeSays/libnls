namespace NlsDataGenerator.Normalization;

// Overall normalization classification (the rows of ICU's custom-normalization chart), assigned
// late in processing. Drives the norm16 encoding.
internal enum NormType
{
    Unknown,
    Inert,
    YesYesCombinesFwd,
    YesNoCombinesFwd,
    YesNoMappingOnly,
    NoNoCompYes,
    NoNoCompBoundaryBefore,
    NoNoCompNoMaybeCc,
    NoNoEmpty,
    NoNoDelta,
    MaybeNoMappingOnly,
    MaybeNoCombinesFwd,
    MaybeYesCombinesFwd,
    MaybeYesSimple,
    YesYesWithCc,
}
