// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// LocaleNameToLCID / LCIDToLocaleName / IsValidLocale, plus the GetLocaleInfo stubs.

#include "Check.h"
#include "WideText.h"

#include "Win32Types.h"

#include <winnls.h>

using namespace nls::tests;

NLS_TEST(Locale, EnUsRoundTrip)
{
    CHECK_EQ(LocaleNameToLCID(L"en-US", 0), 0x0409);

    WCHAR name[LOCALE_NAME_MAX_LENGTH] = {};
    // The returned count includes the terminating null.
    CHECK_EQ(LCIDToLocaleName(0x0409, name, LOCALE_NAME_MAX_LENGTH, 0), 6);
    CHECK(WideEquals(name, L"en-US"));
}

// Known Windows LCIDs from data/lcidmap.txt, exercised as a round-trip property.
NLS_TEST(Locale, KnownLocalesRoundTrip)
{
    const WCHAR* names[] = { L"en-US", L"en-GB", L"de-DE", L"fr-FR", L"ja-JP" };
    const LCID lcids[] = { 0x0409, 0x0809, 0x0407, 0x040c, 0x0411 };
    for (int i = 0; i < 5; ++i)
    {
        CHECK_EQ(LocaleNameToLCID(names[i], 0), lcids[i]);
        CHECK(IsValidLocale(lcids[i], 0) == TRUE);

        WCHAR back[LOCALE_NAME_MAX_LENGTH] = {};
        const int n = LCIDToLocaleName(lcids[i], back, LOCALE_NAME_MAX_LENGTH, 0);
        CHECK(n > 0);
        CHECK_EQ(LocaleNameToLCID(back, 0), lcids[i]);
    }
}

NLS_TEST(Locale, NameLookupIsCaseInsensitive)
{
    CHECK_EQ(LocaleNameToLCID(L"EN-us", 0), 0x0409);
    CHECK_EQ(LocaleNameToLCID(L"De-De", 0), 0x0407);
}

NLS_TEST(Locale, UnknownNameRejected)
{
    SetLastError(0);
    CHECK_EQ(LocaleNameToLCID(L"zz-ZZ", 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(Locale, NullNameRejected)
{
    SetLastError(0);
    CHECK_EQ(LocaleNameToLCID(nullptr, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(Locale, LcidToNameSizeQuery)
{
    CHECK_EQ(LCIDToLocaleName(0x0409, nullptr, 0, 0), 6);
}

NLS_TEST(Locale, LcidToNameInsufficientBuffer)
{
    WCHAR name[3] = {};
    SetLastError(0);
    CHECK_EQ(LCIDToLocaleName(0x0409, name, 3, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INSUFFICIENT_BUFFER);
}

NLS_TEST(Locale, UnknownLcidRejected)
{
    WCHAR name[16] = {};
    SetLastError(0);
    CHECK_EQ(LCIDToLocaleName(0xDEAD, name, 16, 0), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(Locale, IsValidLocale)
{
    CHECK(IsValidLocale(0x0409, 0) == TRUE);
    CHECK(IsValidLocale(0xDEAD, 0) == FALSE);
}

// ESE uses GetLocaleInfo only for diagnostic labels and tolerates failure; the
// Linux surface stubs both, failing with ERROR_NOT_SUPPORTED.
NLS_TEST(Locale, GetLocaleInfoStubsFail)
{
    WCHAR buffer[16] = {};
    SetLastError(0);
    CHECK_EQ(GetLocaleInfoW(0x0409, LOCALE_SLANGUAGE, buffer, 16), 0);
    CHECK_EQ(GetLastError(), ERROR_NOT_SUPPORTED);

    SetLastError(0);
    CHECK_EQ(GetLocaleInfoEx(L"en-US", LOCALE_SLANGUAGE, buffer, 16), 0);
    CHECK_EQ(GetLastError(), ERROR_NOT_SUPPORTED);
}
