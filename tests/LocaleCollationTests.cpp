// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Locale-specific collation. Each test pins a tailoring that visibly reorders
// letters relative to English/root and asserts both the locale's order and the
// contrasting en-US order, so the difference itself is what is verified.

#include "Check.h"

#include "Win32Types.h"

#include <winnls.h>

using namespace nls::tests;

// Swedish treats a-ring / a-diaeresis / o-diaeresis as distinct letters sorting
// AFTER z; English/root sorts them next to their base vowel.
NLS_TEST(LocaleCollation, SwedishUmlautsAfterZ)
{
    const LCID swedish = 0x041d;
    const LCID english = 0x0409;

    CHECK_EQ(CompareStringW(swedish, 0, L"ö", -1, L"z", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(swedish, 0, L"ä", -1, L"z", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(english, 0, L"ö", -1, L"z", -1), CSTR_LESS_THAN);
}

// Estonian places z between s and t (not at the end of the alphabet), so z sorts
// before t; in English z is last and sorts after t.
NLS_TEST(LocaleCollation, EstonianZBeforeT)
{
    const LCID estonian = 0x0425;
    const LCID english = 0x0409;

    CHECK_EQ(CompareStringW(estonian, 0, L"z", -1, L"t", -1), CSTR_LESS_THAN);
    CHECK_EQ(CompareStringW(english, 0, L"z", -1, L"t", -1), CSTR_GREATER_THAN);
}

// Czech sorts the digraph "ch" as one letter after h (between h and i), so "cha"
// sorts after any "h..." word; English compares c < h and puts "cha" first.
NLS_TEST(LocaleCollation, CzechChDigraphAfterH)
{
    const LCID czech = 0x0405;
    const LCID english = 0x0409;

    CHECK_EQ(CompareStringW(czech, 0, L"cha", -1, L"hz", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(english, 0, L"cha", -1, L"hz", -1), CSTR_LESS_THAN);
}

// Spanish sorts n-tilde as a distinct letter after n, so it follows every "n..."
// word; English/root treats it as n plus a diacritic, sorting it among them.
NLS_TEST(LocaleCollation, SpanishEnyeAfterN)
{
    const LCID spanish = 0x0c0a;
    const LCID english = 0x0409;

    CHECK_EQ(CompareStringW(spanish, 0, L"ñ", -1, L"nz", -1), CSTR_GREATER_THAN);
    CHECK_EQ(CompareStringW(english, 0, L"ñ", -1, L"nz", -1), CSTR_LESS_THAN);
}
