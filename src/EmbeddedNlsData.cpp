// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "EmbeddedNlsData.h"

// alignas(16) satisfies the alignment ICU's common-data header check requires of
// the image udata_setCommonData / udata_setAppData receives. The file names come
// from the build (NLS_EMBEDDED_COLLATION / NLS_EMBEDDED_CODEPAGES), resolved
// against the generated-data directory passed as --embed-dir. #embed is C23, an
// extension in C++23, so the diagnostic is scoped off only around the directives.
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wc23-extensions"

alignas(16) static const unsigned char CollationBytes[] = {
#embed NLS_EMBEDDED_COLLATION
};

alignas(16) static const unsigned char CodepagesBytes[] = {
#embed NLS_EMBEDDED_CODEPAGES
};

#pragma clang diagnostic pop

const uint8_t* EmbeddedNlsData::Collation()
{
    return CollationBytes;
}

size_t EmbeddedNlsData::CollationSize()
{
    return sizeof CollationBytes;
}

const uint8_t* EmbeddedNlsData::Codepages()
{
    return CodepagesBytes;
}

size_t EmbeddedNlsData::CodepagesSize()
{
    return sizeof CodepagesBytes;
}
