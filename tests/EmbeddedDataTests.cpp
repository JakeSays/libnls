// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Smoke checks that both packages baked into libnls.so are present and usable
// after the no-data InitializeNls(): the codepage table (codepages.dat) and the
// collation data (cldr-<ver>.dat).

#include "Check.h"

#include "Win32Types.h"

#include <winnls.h>

#include <stdint.h>

using namespace nls::tests;

// codepages.dat is embedded: the windows-1252 table resolves the EURO byte.
NLS_TEST(EmbeddedData, CodepagesPackagePresent)
{
    const char euro = static_cast<char>(0x80);
    WCHAR wide[1] = {};
    CHECK_EQ(MultiByteToWideChar(1252, 0, &euro, 1, wide, 1), 1);
    CHECK_EQ(static_cast<uint16_t>(wide[0]), 0x20AC);
}

// cldr-<ver>.dat is embedded: a collator opens and orders basic Latin.
NLS_TEST(EmbeddedData, CollationPackagePresent)
{
    CHECK_EQ(CompareStringW(0x0409, 0, L"a", -1, L"b", -1), CSTR_LESS_THAN);
}
