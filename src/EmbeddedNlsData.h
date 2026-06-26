// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// The NLS data packages baked into libnls.so. The build regenerates both ICU
// "CmnD" packages and embeds their bytes here, so a default InitializeNls()
// needs no data directory. The bytes live in .rodata for the process lifetime,
// which lets ICU's udata layer reference them without copying.

#pragma once

#include <stddef.h>
#include <stdint.h>

class EmbeddedNlsData
{
public:
    // The cldr-<ver>.dat package (collation, normalization, case, CLDR-version
    // metadata), installed as ICU common data.
    static const uint8_t* Collation();
    static size_t CollationSize();

    // The codepages.dat package (windows-1252, LCID<->locale), registered as ICU
    // app data under the package name "codepages".
    static const uint8_t* Codepages();
    static size_t CodepagesSize();
};
