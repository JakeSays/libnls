// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Locale / LCID surface over the LCID<->name table (LocaleTable).
//
// GetLocaleInfoW / GetLocaleInfoEx are stubs that fail: ESE calls them only
// for LOCALE_SLANGUAGE / LOCALE_SENGLANGUAGE to label a "language not
// supported" event, and falls back to "Unknown" on failure. The rest is real.

#include "Win32Types.h"
#include "LocaleTable.h"
#include "WideString.h"

#include <winnls.h>

#include <string>

extern "C" WINBASEAPI LCID WINAPI LocaleNameToLCID(LPCWSTR lpName, DWORD dwFlags)
{
    (void)dwFlags;
    if (lpName == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    const LocaleTable* table = LocaleTable::Instance();
    if (table == nullptr)
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return 0;
    }

    const auto name = WideString::ToAscii(lpName);
    if (!name)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    const LCID lcid = table->LcidForName(*name);
    if (lcid == 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    return lcid;
}

extern "C" WINBASEAPI INT WINAPI LCIDToLocaleName(LCID Locale, LPWSTR lpName, INT cchName, DWORD dwFlags)
{
    (void)dwFlags;
    const LocaleTable* table = LocaleTable::Instance();
    if (table == nullptr)
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return 0;
    }

    const std::string* name = table->NameForLcid(Locale);
    if (name == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    // The returned count includes the terminating null.
    const INT required = static_cast<INT>(name->length()) + 1;
    if (cchName == 0)
    {
        return required;
    }
    if (cchName < required)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return 0;
    }

    for (size_t i = 0; i < name->length(); ++i)
    {
        lpName[i] = static_cast<WCHAR>((*name)[i]);
    }
    lpName[name->length()] = 0;
    return required;
}

extern "C" WINBASEAPI BOOL WINAPI IsValidLocale(LCID Locale, DWORD dwFlags)
{
    (void)dwFlags;
    const LocaleTable* table = LocaleTable::Instance();
    if (table == nullptr)
    {
        return FALSE;
    }
    return table->IsValidLcid(Locale) ? TRUE : FALSE;
}

extern "C" WINBASEAPI INT WINAPI GetLocaleInfoW(LCID Locale, LCTYPE LCType, LPWSTR lpLCData, INT cchData)
{
    (void)Locale;
    (void)LCType;
    (void)lpLCData;
    (void)cchData;
    SetLastError(ERROR_NOT_SUPPORTED);
    return 0;
}

extern "C" WINBASEAPI INT WINAPI GetLocaleInfoEx(LPCWSTR lpLocaleName, LCTYPE LCType, LPWSTR lpLCData, INT cchData)
{
    (void)lpLocaleName;
    (void)LCType;
    (void)lpLCData;
    (void)cchData;
    SetLastError(ERROR_NOT_SUPPORTED);
    return 0;
}
