// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// CompareStringW over ICU collation, including the NORM_*/SORT_* flag handling.

#include "Check.h"

#include "Win32Types.h"

#include <winnls.h>

using namespace nls::tests;

NLS_TEST(CompareString, BasicOrdering)
{
    CHECK_EQ(CompareStringW(0x0409, 0, L"apple", -1, L"banana", -1), CSTR_LESS_THAN);
    CHECK_EQ(CompareStringW(0x0409, 0, L"banana", -1, L"apple", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(0x0409, 0, L"apple", -1, L"apple", -1), CSTR_EQUAL);
}

NLS_TEST(CompareString, CaseSensitiveByDefault)
{
    // Tertiary strength: case differences are not equal.
    CHECK(CompareStringW(0x0409, 0, L"apple", -1, L"APPLE", -1) != CSTR_EQUAL);
}

NLS_TEST(CompareString, IgnoreCase)
{
    CHECK_EQ(CompareStringW(0x0409, NORM_IGNORECASE, L"APPLE", -1, L"apple", -1), CSTR_EQUAL);
}

NLS_TEST(CompareString, IgnoreDiacritics)
{
    // resume with an acute e vs plain e.
    CHECK_EQ(CompareStringW(0x0409, NORM_IGNORENONSPACE, L"resumé", -1, L"resume", -1), CSTR_EQUAL);
    CHECK(CompareStringW(0x0409, 0, L"resumé", -1, L"resume", -1) != CSTR_EQUAL);
}

NLS_TEST(CompareString, ExplicitLengths)
{
    // Only the first five units of each are compared.
    CHECK_EQ(CompareStringW(0x0409, 0, L"applesauce", 5, L"apples", 5), CSTR_EQUAL);
}

NLS_TEST(CompareString, EmptyStrings)
{
    CHECK_EQ(CompareStringW(0x0409, 0, L"", 0, L"", 0), CSTR_EQUAL);
    CHECK_EQ(CompareStringW(0x0409, 0, L"", 0, L"a", -1), CSTR_LESS_THAN);
    CHECK_EQ(CompareStringW(0x0409, 0, L"a", -1, L"", 0), CSTR_GREATER_THAN);
}

NLS_TEST(CompareString, DigitsAsNumbers)
{
    // Lexical: "2" sorts after "10"; numeric: "2" sorts before "10".
    CHECK_EQ(CompareStringW(0x0409, 0, L"2", -1, L"10", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(0x0409, SORT_DIGITSASNUMBERS, L"2", -1, L"10", -1), CSTR_LESS_THAN);
}

NLS_TEST(CompareString, InvariantLocaleOrdersAscii)
{
    // Locale 0 has no name; the root collation still orders basic Latin.
    CHECK_EQ(CompareStringW(0, 0, L"a", -1, L"b", -1), CSTR_LESS_THAN);
}

NLS_TEST(CompareString, NullRejected)
{
    SetLastError(0);
    CHECK_EQ(CompareStringW(0x0409, 0, nullptr, -1, L"x", -1), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}
