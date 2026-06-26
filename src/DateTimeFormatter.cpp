// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "DateTimeFormatter.h"

#include <winnls.h>

const WCHAR* const DateTimeFormatter::DayFull[7] =
{
    L"Sunday", L"Monday", L"Tuesday", L"Wednesday", L"Thursday", L"Friday", L"Saturday"
};

const WCHAR* const DateTimeFormatter::DayAbbreviated[7] =
{
    L"Sun", L"Mon", L"Tue", L"Wed", L"Thu", L"Fri", L"Sat"
};

const WCHAR* const DateTimeFormatter::MonthFull[12] =
{
    L"January", L"February", L"March", L"April", L"May", L"June",
    L"July", L"August", L"September", L"October", L"November", L"December"
};

const WCHAR* const DateTimeFormatter::MonthAbbreviated[12] =
{
    L"Jan", L"Feb", L"Mar", L"Apr", L"May", L"Jun",
    L"Jul", L"Aug", L"Sep", L"Oct", L"Nov", L"Dec"
};

void DateTimeFormatter::AppendChar(LPWSTR dest, INT cap, INT& used, WCHAR value)
{
    if (used < 0)
    {
        return;
    }
    if (cap > 0 && used + 1 >= cap)
    {
        used = -1;
        return;
    }
    if (dest != nullptr && cap > 0)
    {
        dest[used] = value;
    }
    ++used;
}

void DateTimeFormatter::AppendString(LPWSTR dest, INT cap, INT& used, LPCWSTR value)
{
    for (const WCHAR* s = value; *s != 0; ++s)
    {
        AppendChar(dest, cap, used, *s);
    }
}

void DateTimeFormatter::AppendPadded(LPWSTR dest, INT cap, INT& used, int value, int width)
{
    WCHAR digits[8];
    int count = 0;
    if (value == 0)
    {
        digits[count++] = L'0';
    }
    else
    {
        int remaining = value;
        while (remaining > 0 && count < 8)
        {
            digits[count++] = static_cast<WCHAR>(L'0' + (remaining % 10));
            remaining /= 10;
        }
    }
    for (int i = count; i < width; ++i)
    {
        AppendChar(dest, cap, used, L'0');
    }
    while (count > 0)
    {
        AppendChar(dest, cap, used, digits[--count]);
    }
}

int DateTimeFormatter::RunLength(LPCWSTR p)
{
    int n = 1;
    const WCHAR c = *p;
    while (p[n] == c)
    {
        ++n;
    }
    return n;
}

LPCWSTR DateTimeFormatter::DefaultDateFormat(DWORD flags)
{
    if ((flags & DATE_LONGDATE) != 0)
    {
        return L"dddd, MMMM d, yyyy";
    }
    if ((flags & DATE_YEARMONTH) != 0)
    {
        return L"MMMM, yyyy";
    }
    return L"M/d/yyyy";
}

LPCWSTR DateTimeFormatter::DefaultTimeFormat(DWORD flags)
{
    if ((flags & TIME_FORCE24HOURFORMAT) != 0)
    {
        if ((flags & TIME_NOMINUTESORSECONDS) != 0)
        {
            return L"HH";
        }
        if ((flags & TIME_NOSECONDS) != 0)
        {
            return L"HH:mm";
        }
        return L"HH:mm:ss";
    }
    if ((flags & TIME_NOMINUTESORSECONDS) != 0)
    {
        return L"h tt";
    }
    if ((flags & TIME_NOSECONDS) != 0)
    {
        return L"h:mm tt";
    }
    return L"h:mm:ss tt";
}

INT DateTimeFormatter::Expand(LPCWSTR format, const SYSTEMTIME& time, bool dateContext, LPWSTR dest, INT cap)
{
    INT used = 0;
    const int hour12 = (time.wHour == 0) ? 12
                     : (time.wHour > 12) ? time.wHour - 12
                                         : time.wHour;
    const bool pm = (time.wHour >= 12);

    for (const WCHAR* p = format; *p != 0; )
    {
        if (*p == L'\'')
        {
            ++p;
            while (*p != 0)
            {
                if (*p == L'\'')
                {
                    if (p[1] == L'\'')
                    {
                        AppendChar(dest, cap, used, L'\'');
                        p += 2;
                        continue;
                    }
                    ++p;
                    break;
                }
                AppendChar(dest, cap, used, *p++);
            }
            if (used < 0)
            {
                return 0;
            }
            continue;
        }

        if (dateContext && *p == L'd')
        {
            const int n = RunLength(p);
            p += n;
            if (n == 1)
            {
                AppendPadded(dest, cap, used, time.wDay, 1);
            }
            else if (n == 2)
            {
                AppendPadded(dest, cap, used, time.wDay, 2);
            }
            else if (n == 3)
            {
                AppendString(dest, cap, used, DayAbbreviated[time.wDayOfWeek % 7]);
            }
            else
            {
                AppendString(dest, cap, used, DayFull[time.wDayOfWeek % 7]);
            }
        }
        else if (dateContext && *p == L'M')
        {
            const int n = RunLength(p);
            p += n;
            const int month = (time.wMonth >= 1 && time.wMonth <= 12) ? time.wMonth : 1;
            if (n == 1)
            {
                AppendPadded(dest, cap, used, month, 1);
            }
            else if (n == 2)
            {
                AppendPadded(dest, cap, used, month, 2);
            }
            else if (n == 3)
            {
                AppendString(dest, cap, used, MonthAbbreviated[month - 1]);
            }
            else
            {
                AppendString(dest, cap, used, MonthFull[month - 1]);
            }
        }
        else if (dateContext && *p == L'y')
        {
            const int n = RunLength(p);
            p += n;
            if (n == 1)
            {
                AppendPadded(dest, cap, used, time.wYear % 10, 1);
            }
            else if (n == 2)
            {
                AppendPadded(dest, cap, used, time.wYear % 100, 2);
            }
            else
            {
                AppendPadded(dest, cap, used, time.wYear, 4);
            }
        }
        else if (dateContext && *p == L'g')
        {
            // Era marker; the engine never persists it, so emit "AD".
            p += RunLength(p);
            AppendString(dest, cap, used, L"AD");
        }
        else if (!dateContext && *p == L'h')
        {
            const int n = RunLength(p);
            p += n;
            AppendPadded(dest, cap, used, hour12, (n >= 2) ? 2 : 1);
        }
        else if (!dateContext && *p == L'H')
        {
            const int n = RunLength(p);
            p += n;
            AppendPadded(dest, cap, used, time.wHour, (n >= 2) ? 2 : 1);
        }
        else if (!dateContext && *p == L'm')
        {
            const int n = RunLength(p);
            p += n;
            AppendPadded(dest, cap, used, time.wMinute, (n >= 2) ? 2 : 1);
        }
        else if (!dateContext && *p == L's')
        {
            const int n = RunLength(p);
            p += n;
            AppendPadded(dest, cap, used, time.wSecond, (n >= 2) ? 2 : 1);
        }
        else if (!dateContext && *p == L't')
        {
            const int n = RunLength(p);
            p += n;
            const WCHAR* marker = pm ? L"PM" : L"AM";
            if (n == 1)
            {
                AppendChar(dest, cap, used, marker[0]);
            }
            else
            {
                AppendString(dest, cap, used, marker);
            }
        }
        else
        {
            AppendChar(dest, cap, used, *p++);
        }

        if (used < 0)
        {
            return 0;
        }
    }

    if (cap > 0 && dest != nullptr)
    {
        if (used < cap)
        {
            dest[used] = 0;
        }
        else
        {
            dest[cap - 1] = 0;
            return 0;
        }
    }
    // The returned count includes the terminating null.
    return used + 1;
}

INT DateTimeFormatter::FormatDate(DWORD flags, const SYSTEMTIME& time, LPCWSTR format, LPWSTR dest, INT cap)
{
    const WCHAR* effective = (format != nullptr) ? format : DefaultDateFormat(flags);
    return Expand(effective, time, true, dest, cap);
}

INT DateTimeFormatter::FormatTime(DWORD flags, const SYSTEMTIME& time, LPCWSTR format, LPWSTR dest, INT cap)
{
    const WCHAR* effective = (format != nullptr) ? format : DefaultTimeFormat(flags);
    return Expand(effective, time, false, dest, cap);
}
