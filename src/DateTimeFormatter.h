// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// en-US date/time formatting behind GetDateFormatW / GetTimeFormatW. libnls ships
// a single fixed locale (en-US, matching its CP_ACP=1252 posture), so these
// reproduce the Win32 format-string expansion the engine's diagnostic output
// relies on rather than a full locale-aware formatter.

#pragma once

#include "Win32Types.h"

class DateTimeFormatter
{
public:
    // Format time per a Win32 date (FormatDate) or time (FormatTime) format
    // string into dest, which holds cap WCHARs. A null format selects the Win32
    // en-US default for the flags. Returns the WCHAR count including the
    // terminator, or 0 on overflow; cap == 0 returns the required count.
    static INT FormatDate(DWORD flags, const SYSTEMTIME& time, LPCWSTR format, LPWSTR dest, INT cap);
    static INT FormatTime(DWORD flags, const SYSTEMTIME& time, LPCWSTR format, LPWSTR dest, INT cap);

private:
    // Expand a date/time format string. Tokens: d dd ddd dddd / M MM MMM MMMM /
    // y yy yyyy / gg for dates; h hh H HH / m mm / s ss / t tt for times;
    // 'literal' quoting ('' for a single quote).
    static INT Expand(LPCWSTR format, const SYSTEMTIME& time, bool dateContext, LPWSTR dest, INT cap);
    static LPCWSTR DefaultDateFormat(DWORD flags);
    static LPCWSTR DefaultTimeFormat(DWORD flags);
    // Append helpers advance used; on overflow they set used to -1 and every
    // later append is a no-op, so callers can defer the check.
    static void AppendChar(LPWSTR dest, INT cap, INT& used, WCHAR value);
    static void AppendString(LPWSTR dest, INT cap, INT& used, LPCWSTR value);
    static void AppendPadded(LPWSTR dest, INT cap, INT& used, int value, int width);
    // The run of identical format letters starting at p (disambiguates d/dd/ddd).
    static int RunLength(LPCWSTR p);

    static const WCHAR* const DayFull[7];
    static const WCHAR* const DayAbbreviated[7];
    static const WCHAR* const MonthFull[12];
    static const WCHAR* const MonthAbbreviated[12];
};
