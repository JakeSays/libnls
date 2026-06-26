// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// GetDateFormatW / GetTimeFormatW: en-US format-string expansion.

#include "Check.h"
#include "WideText.h"

#include "Win32Types.h"

#include <winnls.h>

using namespace nls::tests;

// Saturday, 8 March 2025, 14:05:09. The formatter takes wDayOfWeek as given
// rather than deriving it.
static SYSTEMTIME SampleTime()
{
    SYSTEMTIME time = {};
    time.wYear = 2025;
    time.wMonth = 3;
    time.wDayOfWeek = 6;
    time.wDay = 8;
    time.wHour = 14;
    time.wMinute = 5;
    time.wSecond = 9;
    return time;
}

NLS_TEST(DateFormat, ExplicitShortPattern)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    // "3/8/2025" is 8 chars; the count includes the terminator.
    CHECK_EQ(GetDateFormatW(0, 0, &time, L"M/d/yyyy", out, 32), 9);
    CHECK(WideEquals(out, L"3/8/2025"));
}

NLS_TEST(DateFormat, PaddedFields)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetDateFormatW(0, 0, &time, L"MM/dd/yyyy", out, 32);
    CHECK(WideEquals(out, L"03/08/2025"));
}

NLS_TEST(DateFormat, FullDayAndMonthNames)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[64] = {};
    GetDateFormatW(0, 0, &time, L"dddd, MMMM d, yyyy", out, 64);
    CHECK(WideEquals(out, L"Saturday, March 8, 2025"));
}

NLS_TEST(DateFormat, AbbreviatedNamesAndTwoDigitYear)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetDateFormatW(0, 0, &time, L"ddd MMM yy", out, 32);
    CHECK(WideEquals(out, L"Sat Mar 25"));
}

NLS_TEST(DateFormat, NullFormatShortDefault)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetDateFormatW(0, DATE_SHORTDATE, &time, nullptr, out, 32);
    CHECK(WideEquals(out, L"3/8/2025"));
}

NLS_TEST(DateFormat, NullFormatLongDefault)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[64] = {};
    GetDateFormatW(0, DATE_LONGDATE, &time, nullptr, out, 64);
    CHECK(WideEquals(out, L"Saturday, March 8, 2025"));
}

NLS_TEST(DateFormat, QuotedLiteral)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetDateFormatW(0, 0, &time, L"'Day:' M", out, 32);
    CHECK(WideEquals(out, L"Day: 3"));
}

NLS_TEST(DateFormat, SizingQuery)
{
    SYSTEMTIME time = SampleTime();
    CHECK_EQ(GetDateFormatW(0, 0, &time, L"M/d/yyyy", nullptr, 0), 9);
}

NLS_TEST(DateFormat, InsufficientBuffer)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[4] = {};
    CHECK_EQ(GetDateFormatW(0, 0, &time, L"M/d/yyyy", out, 4), 0);
}

NLS_TEST(DateFormat, NullDateRejected)
{
    WCHAR out[16] = {};
    SetLastError(0);
    CHECK_EQ(GetDateFormatW(0, 0, nullptr, L"M/d/yyyy", out, 16), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}

NLS_TEST(TimeFormat, TwelveHourWithMarker)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetTimeFormatW(0, 0, &time, L"h:mm:ss tt", out, 32);
    CHECK(WideEquals(out, L"2:05:09 PM"));
}

NLS_TEST(TimeFormat, TwentyFourHour)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetTimeFormatW(0, 0, &time, L"HH:mm:ss", out, 32);
    CHECK(WideEquals(out, L"14:05:09"));
}

NLS_TEST(TimeFormat, NullFormatDefault)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetTimeFormatW(0, 0, &time, nullptr, out, 32);
    CHECK(WideEquals(out, L"2:05:09 PM"));
}

NLS_TEST(TimeFormat, Force24HourDefault)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    GetTimeFormatW(0, TIME_FORCE24HOURFORMAT, &time, nullptr, out, 32);
    CHECK(WideEquals(out, L"14:05:09"));
}

NLS_TEST(TimeFormat, MidnightNoonAndEvening)
{
    SYSTEMTIME time = SampleTime();
    WCHAR out[32] = {};
    time.wHour = 0;
    GetTimeFormatW(0, 0, &time, L"h tt", out, 32);
    CHECK(WideEquals(out, L"12 AM"));
    time.wHour = 12;
    GetTimeFormatW(0, 0, &time, L"h tt", out, 32);
    CHECK(WideEquals(out, L"12 PM"));
    time.wHour = 23;
    GetTimeFormatW(0, 0, &time, L"h tt", out, 32);
    CHECK(WideEquals(out, L"11 PM"));
}

NLS_TEST(TimeFormat, NullTimeRejected)
{
    WCHAR out[16] = {};
    SetLastError(0);
    CHECK_EQ(GetTimeFormatW(0, 0, nullptr, L"HH:mm", out, 16), 0);
    CHECK_EQ(GetLastError(), ERROR_INVALID_PARAMETER);
}
