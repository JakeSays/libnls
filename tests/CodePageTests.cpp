// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// MultiByteToWideChar / WideCharToMultiByte: windows-1252 and UTF-8.

#include "Check.h"

#include "Win32Types.h"

#include <winnls.h>

#include <stdint.h>

using namespace nls::tests;

// Every cp1252 byte maps to a single UTF-16 unit and back to the same byte (the
// table is injective, including the five "undefined" positions that map to the
// matching C1 control).
NLS_TEST(CodePage, Cp1252RoundTripAllBytes)
{
    for (int b = 0; b <= 0xFF; ++b)
    {
        const char mb[1] = { static_cast<char>(b) };
        WCHAR wide[2] = {};
        const int n = MultiByteToWideChar(1252, 0, mb, 1, wide, 2);
        CHECK_EQ(n, 1);

        char back[2] = {};
        BOOL usedDefault = TRUE;
        const int m = WideCharToMultiByte(1252, 0, wide, 1, back, 2, nullptr, &usedDefault);
        CHECK_EQ(m, 1);
        CHECK_EQ(static_cast<unsigned char>(back[0]), b);
        CHECK_FALSE(usedDefault);
    }
}

// ASCII (0x00-0x7F) and Latin-1 (0xA0-0xFF) are identity; only 0x80-0x9F differ.
NLS_TEST(CodePage, Cp1252IdentityRanges)
{
    const int points[] = { 0x00, 0x41, 0x7F, 0xA0, 0xA9, 0xFF };
    for (const int b : points)
    {
        const char mb[1] = { static_cast<char>(b) };
        WCHAR wide[1] = {};
        CHECK_EQ(MultiByteToWideChar(1252, 0, mb, 1, wide, 1), 1);
        CHECK_EQ(static_cast<uint16_t>(wide[0]), b);
    }
}

// The 0x80-0x9F block, including the undefined positions (0x81/0x8D/0x8F/0x90/0x9D).
NLS_TEST(CodePage, Cp1252HighRange)
{
    const unsigned char bytes[] = { 0x80, 0x91, 0x92, 0x99, 0x9F, 0x81, 0x8D, 0x8F, 0x90, 0x9D };
    const uint16_t scalars[] = { 0x20AC, 0x2018, 0x2019, 0x2122, 0x0178, 0x0081, 0x008D, 0x008F, 0x0090, 0x009D };
    for (int i = 0; i < 10; ++i)
    {
        const char mb[1] = { static_cast<char>(bytes[i]) };
        WCHAR wide[1] = {};
        CHECK_EQ(MultiByteToWideChar(1252, 0, mb, 1, wide, 1), 1);
        CHECK_EQ(static_cast<uint16_t>(wide[0]), scalars[i]);
    }
}

// CP_ACP and CP_THREAD_ACP both resolve to windows-1252.
NLS_TEST(CodePage, AnsiCodePagesAliasTo1252)
{
    const char mb[1] = { static_cast<char>(0x80) };
    WCHAR acp[1] = {};
    WCHAR thread[1] = {};
    CHECK_EQ(MultiByteToWideChar(CP_ACP, 0, mb, 1, acp, 1), 1);
    CHECK_EQ(static_cast<uint16_t>(acp[0]), 0x20AC);
    CHECK_EQ(MultiByteToWideChar(CP_THREAD_ACP, 0, mb, 1, thread, 1), 1);
    CHECK_EQ(static_cast<uint16_t>(thread[0]), 0x20AC);
}

// count == -1 includes the terminator; an explicit count excludes it; capacity 0
// is a sizing query.
NLS_TEST(CodePage, MultiByteLengthContract)
{
    const char mb[] = "hello";
    CHECK_EQ(MultiByteToWideChar(1252, 0, mb, -1, nullptr, 0), 6);
    CHECK_EQ(MultiByteToWideChar(1252, 0, mb, 5, nullptr, 0), 5);
}

NLS_TEST(CodePage, MultiByteInsufficientBuffer)
{
    const char mb[] = "hello";
    WCHAR wide[3] = {};
    SetLastError(0);
    CHECK_EQ(MultiByteToWideChar(1252, 0, mb, 5, wide, 3), 0);
    CHECK_EQ(GetLastError(), ERROR_INSUFFICIENT_BUFFER);
}

NLS_TEST(CodePage, UnsupportedCodePageRejected)
{
    const char mb[] = "x";
    WCHAR wide[2] = {};
    SetLastError(0);
    CHECK_EQ(MultiByteToWideChar(932, 0, mb, 1, wide, 2), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(CodePage, NullInputRejected)
{
    WCHAR wide[2] = {};
    SetLastError(0);
    CHECK_EQ(MultiByteToWideChar(1252, 0, nullptr, 1, wide, 2), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(CodePage, NegativeCountRejected)
{
    const char mb[] = "x";
    WCHAR wide[2] = {};
    SetLastError(0);
    CHECK_EQ(MultiByteToWideChar(1252, 0, mb, -2, wide, 2), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

// "A" (1 byte) + EURO (3) + e-acute (2) + grinning-face (4, a surrogate pair),
// decoded then re-encoded.
NLS_TEST(CodePage, Utf8MixedWidthsRoundTrip)
{
    const char utf8[] = {
        'A',
        static_cast<char>(0xE2), static_cast<char>(0x82), static_cast<char>(0xAC),
        static_cast<char>(0xC3), static_cast<char>(0xA9),
        static_cast<char>(0xF0), static_cast<char>(0x9F), static_cast<char>(0x98), static_cast<char>(0x80),
        0
    };
    WCHAR wide[8] = {};
    const int n = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, wide, 8);
    // A + EURO + e-acute + (high, low) + null = 6 units.
    CHECK_EQ(n, 6);
    CHECK_EQ(static_cast<uint16_t>(wide[0]), 0x0041);
    CHECK_EQ(static_cast<uint16_t>(wide[1]), 0x20AC);
    CHECK_EQ(static_cast<uint16_t>(wide[2]), 0x00E9);
    CHECK_EQ(static_cast<uint16_t>(wide[3]), 0xD83D);
    CHECK_EQ(static_cast<uint16_t>(wide[4]), 0xDE00);
    CHECK_EQ(static_cast<uint16_t>(wide[5]), 0x0000);

    char back[16] = {};
    const int m = WideCharToMultiByte(CP_UTF8, 0, wide, 5, back, 16, nullptr, nullptr);
    CHECK_EQ(m, 10);
}

NLS_TEST(CodePage, Utf8InvalidWithErrorFlag)
{
    const char bad[] = { static_cast<char>(0xFF), static_cast<char>(0xFE) };
    WCHAR wide[8] = {};
    SetLastError(0);
    CHECK_EQ(MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bad, 2, wide, 8), 0);
    CHECK_EQ(GetLastError(), ERROR_NO_UNICODE_TRANSLATION);
}

// A genuine U+FFFD (EF BF BD) is valid input even with MB_ERR_INVALID_CHARS.
NLS_TEST(CodePage, Utf8RealReplacementCharAccepted)
{
    const char repl[] = { static_cast<char>(0xEF), static_cast<char>(0xBF), static_cast<char>(0xBD) };
    WCHAR wide[4] = {};
    CHECK_EQ(MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, repl, 3, wide, 4), 1);
    CHECK_EQ(static_cast<uint16_t>(wide[0]), 0xFFFD);
}

// A scalar absent from cp1252 falls back to '?' and flags the substitution.
NLS_TEST(CodePage, WideToCp1252DefaultChar)
{
    const WCHAR aMacron[] = { 0x0100, 0 };
    char out[2] = {};
    BOOL usedDefault = FALSE;
    CHECK_EQ(WideCharToMultiByte(1252, 0, aMacron, 1, out, 2, nullptr, &usedDefault), 1);
    CHECK_EQ(static_cast<unsigned char>(out[0]), static_cast<unsigned char>('?'));
    CHECK(usedDefault == TRUE);

    const char custom = '#';
    char out2[2] = {};
    BOOL usedDefault2 = FALSE;
    CHECK_EQ(WideCharToMultiByte(1252, 0, aMacron, 1, out2, 2, &custom, &usedDefault2), 1);
    CHECK_EQ(static_cast<unsigned char>(out2[0]), static_cast<unsigned char>('#'));
    CHECK(usedDefault2 == TRUE);
}

NLS_TEST(CodePage, WideToCp1252ErrorOnUnmappable)
{
    const WCHAR aMacron[] = { 0x0100, 0 };
    char out[2] = {};
    SetLastError(0);
    CHECK_EQ(WideCharToMultiByte(1252, WC_ERR_INVALID_CHARS, aMacron, 1, out, 2, nullptr, nullptr), 0);
    CHECK_EQ(GetLastError(), ERROR_NO_UNICODE_TRANSLATION);
}

NLS_TEST(CodePage, WideToMultiByteLengthQuery)
{
    const WCHAR euro[] = { 0x20AC, 0 };
    // EURO is 3 UTF-8 bytes; -1 adds the terminator.
    CHECK_EQ(WideCharToMultiByte(CP_UTF8, 0, euro, -1, nullptr, 0, nullptr, nullptr), 4);
}

NLS_TEST(CodePage, WideToMultiByteInsufficientBuffer)
{
    const WCHAR euro[] = { 0x20AC, 0 };
    char out[2] = {};
    SetLastError(0);
    CHECK_EQ(WideCharToMultiByte(CP_UTF8, 0, euro, 1, out, 2, nullptr, nullptr), 0);
    CHECK_EQ(GetLastError(), ERROR_INSUFFICIENT_BUFFER);
}
