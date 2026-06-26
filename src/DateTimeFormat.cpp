// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// GetDateFormatW / GetTimeFormatW entry points. The expansion lives in
// DateTimeFormatter; these validate the arguments and delegate.

#include "Win32Types.h"
#include "DateTimeFormatter.h"

#include <winnls.h>

extern "C" WINBASEAPI INT WINAPI GetDateFormatW(LCID Locale, DWORD dwFlags, const SYSTEMTIME* lpDate,
                                                LPCWSTR lpFormat, LPWSTR lpDateStr, INT cchDate)
{
    // The single fixed locale (en-US) is built into the formatter.
    (void)Locale;
    if (lpDate == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    return DateTimeFormatter::FormatDate(dwFlags, *lpDate, lpFormat, lpDateStr, cchDate);
}

extern "C" WINBASEAPI INT WINAPI GetTimeFormatW(LCID Locale, DWORD dwFlags, const SYSTEMTIME* lpTime,
                                                LPCWSTR lpFormat, LPWSTR lpTimeStr, INT cchTime)
{
    (void)Locale;
    if (lpTime == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    return DateTimeFormatter::FormatTime(dwFlags, *lpTime, lpFormat, lpTimeStr, cchTime);
}
