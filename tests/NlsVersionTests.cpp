// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// GetNLSVersionEx: the packed CLDR version the collation data carries.

#include "Check.h"

#include "Win32Types.h"

#include <winnls.h>

using namespace nls::tests;

// The baked-in package is CLDR 48.2, packed as (major << 16) | minor.
NLS_TEST(NlsVersion, ReportsPackedCldrVersion)
{
    NLSVERSIONINFOEX version = {};
    version.dwNLSVersionInfoSize = sizeof(version);
    CHECK(GetNLSVersionEx(COMPARE_STRING, L"en-US", &version) == TRUE);
    CHECK_EQ(version.dwNLSVersion, 0x00300002);
    CHECK_EQ(version.dwDefinedVersion, 0x00300002);
    CHECK_EQ(version.dwEffectiveId, 0);
}

// The version is package-wide, so the locale does not change it.
NLS_TEST(NlsVersion, LocaleIndependent)
{
    NLSVERSIONINFOEX a = {};
    a.dwNLSVersionInfoSize = sizeof(a);
    NLSVERSIONINFOEX b = {};
    b.dwNLSVersionInfoSize = sizeof(b);
    CHECK(GetNLSVersionEx(COMPARE_STRING, L"en-US", &a) == TRUE);
    CHECK(GetNLSVersionEx(COMPARE_STRING, L"ja-JP", &b) == TRUE);
    CHECK_EQ(a.dwNLSVersion, b.dwNLSVersion);
}

NLS_TEST(NlsVersion, NullInfoRejected)
{
    SetLastError(0);
    CHECK(GetNLSVersionEx(COMPARE_STRING, L"en-US", nullptr) == FALSE);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(NlsVersion, UndersizedInfoRejected)
{
    NLSVERSIONINFOEX version = {};
    version.dwNLSVersionInfoSize = 4;
    SetLastError(0);
    CHECK(GetNLSVersionEx(COMPARE_STRING, L"en-US", &version) == FALSE);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}
