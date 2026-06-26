// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// LCMapStringEx / LCMapStringW: case mapping, sort keys, and byte reversal.

#include "Check.h"
#include "WideText.h"

#include "Win32Types.h"

#include <winnls.h>

#include <stdint.h>
#include <string.h>

using namespace nls::tests;

NLS_TEST(LcMapString, Uppercase)
{
    WCHAR out[8] = {};
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_UPPERCASE, L"aBcd", -1, out, 8, nullptr, nullptr, 0), 4);
    CHECK(WideEquals(out, L"ABCD"));
}

NLS_TEST(LcMapString, Lowercase)
{
    WCHAR out[8] = {};
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_LOWERCASE, L"AbCd", -1, out, 8, nullptr, nullptr, 0), 4);
    CHECK(WideEquals(out, L"abcd"));
}

NLS_TEST(LcMapString, CaseLengthQuery)
{
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_UPPERCASE, L"abc", -1, nullptr, 0, nullptr, nullptr, 0), 3);
}

NLS_TEST(LcMapString, CaseInsufficientBuffer)
{
    WCHAR out[2] = {};
    SetLastError(0);
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_UPPERCASE, L"abcd", -1, out, 2, nullptr, nullptr, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INSUFFICIENT_BUFFER);
}

// Sort keys are ICU byte strings; cchDest counts bytes. They are NUL-terminated
// with no embedded NULs, so strcmp over the keys reproduces the collation order
// of their sources.
NLS_TEST(LcMapString, SortKeyOrdering)
{
    char k1[64] = {};
    char k2[64] = {};
    const int n1 = LCMapStringEx(L"en-US", LCMAP_SORTKEY, L"apple", -1, reinterpret_cast<WCHAR*>(k1), 64, nullptr, nullptr, 0);
    const int n2 = LCMapStringEx(L"en-US", LCMAP_SORTKEY, L"banana", -1, reinterpret_cast<WCHAR*>(k2), 64, nullptr, nullptr, 0);
    CHECK(n1 > 0);
    CHECK(n2 > 0);
    CHECK(strcmp(k1, k2) < 0);
}

NLS_TEST(LcMapString, SortKeyLengthQuery)
{
    CHECK(LCMapStringEx(L"en-US", LCMAP_SORTKEY, L"apple", -1, nullptr, 0, nullptr, nullptr, 0) > 0);
}

NLS_TEST(LcMapString, SortKeyInsufficientBuffer)
{
    unsigned char key[2] = {};
    SetLastError(0);
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_SORTKEY, L"apple", -1, reinterpret_cast<WCHAR*>(key), 2, nullptr, nullptr, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INSUFFICIENT_BUFFER);
}

NLS_TEST(LcMapString, ByteReverseSwapsEachUnit)
{
    const WCHAR src[] = { 0x1234, 0x5678, 0 };
    WCHAR out[2] = {};
    CHECK_EQ(LCMapStringEx(L"", LCMAP_BYTEREV, src, 2, out, 2, nullptr, nullptr, 0), 2);
    CHECK_EQ(static_cast<uint16_t>(out[0]), 0x3412);
    CHECK_EQ(static_cast<uint16_t>(out[1]), 0x7856);
}

NLS_TEST(LcMapString, ByteReverseLengthQuery)
{
    const WCHAR src[] = { 0x1234, 0x5678, 0 };
    CHECK_EQ(LCMapStringEx(L"", LCMAP_BYTEREV, src, -1, nullptr, 0, nullptr, nullptr, 0), 2);
}

NLS_TEST(LcMapString, UnrecognizedFlagsRejected)
{
    WCHAR out[8] = {};
    SetLastError(0);
    CHECK_EQ(LCMapStringEx(L"en-US", 0, L"abc", -1, out, 8, nullptr, nullptr, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_FLAGS);
}

NLS_TEST(LcMapString, NullSourceRejected)
{
    WCHAR out[8] = {};
    SetLastError(0);
    CHECK_EQ(LCMapStringEx(L"en-US", LCMAP_UPPERCASE, nullptr, -1, out, 8, nullptr, nullptr, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

// LCMapStringW resolves the locale by LCID and delegates to the same mapper.
NLS_TEST(LcMapString, LcMapStringWByLcid)
{
    WCHAR out[8] = {};
    CHECK_EQ(LCMapStringW(0x0409, LCMAP_UPPERCASE, L"abc", -1, out, 8), 3);
    CHECK(WideEquals(out, L"ABC"));
}

// Turkish casing: with LCMAP_LINGUISTIC_CASING the locale is honored, so lowercase
// i uppercases to the dotted capital I (U+0130) rather than ASCII I.
NLS_TEST(LcMapString, TurkishLinguisticUppercaseDottedI)
{
    WCHAR out[4] = {};
    CHECK_EQ(LCMapStringEx(L"tr-TR", LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING, L"i", -1, out, 4, nullptr, nullptr, 0), 1);
    CHECK_EQ(static_cast<uint16_t>(out[0]), 0x0130);
}

// And capital I lowercases to the dotless small i (U+0131).
NLS_TEST(LcMapString, TurkishLinguisticLowercaseDotlessI)
{
    WCHAR out[4] = {};
    CHECK_EQ(LCMapStringEx(L"tr-TR", LCMAP_LOWERCASE | LCMAP_LINGUISTIC_CASING, L"I", -1, out, 4, nullptr, nullptr, 0), 1);
    CHECK_EQ(static_cast<uint16_t>(out[0]), 0x0131);
}

// Without LCMAP_LINGUISTIC_CASING the locale is ignored, so even tr-TR uses
// root casing: i uppercases to ASCII I (U+0049).
NLS_TEST(LcMapString, NonLinguisticCasingIgnoresLocale)
{
    WCHAR out[4] = {};
    CHECK_EQ(LCMapStringEx(L"tr-TR", LCMAP_UPPERCASE, L"i", -1, out, 4, nullptr, nullptr, 0), 1);
    CHECK_EQ(static_cast<uint16_t>(out[0]), 0x0049);
}
