// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// LCMapStringEx / LCMapStringW entry points. The mapping itself lives in
// StringMapper; these resolve the locale (name or LCID) and delegate.

#include "Win32Types.h"
#include "StringMapper.h"
#include "LocaleTable.h"
#include "WideString.h"

#include <winnls.h>

#include <string>

extern "C" WINBASEAPI INT WINAPI LCMapStringEx(LPCWSTR lpLocaleName, DWORD dwMapFlags,
                                               LPCWSTR lpSrcStr, INT cchSrc,
                                               LPWSTR lpDestStr, INT cchDest,
                                               LPNLSVERSIONINFO lpVersionInformation,
                                               LPVOID lpReserved, LPARAM sortHandle)
{
    // The CLDR version is selected by the loaded package; the per-call version
    // hook is not wired on Linux yet (see project_ese_per_index_sort_version).
    (void)lpVersionInformation;
    (void)lpReserved;
    (void)sortHandle;

    std::string locale;
    if (lpLocaleName != nullptr)
    {
        auto ascii = WideString::ToAscii(lpLocaleName);
        if (!ascii)
        {
            SetLastError(ERROR_INVALID_PARAMETER);
            return 0;
        }
        locale = std::move(*ascii);
    }
    return StringMapper::Map(locale.c_str(), dwMapFlags, lpSrcStr, cchSrc, lpDestStr, cchDest);
}

extern "C" WINBASEAPI INT WINAPI LCMapStringW(LCID Locale, DWORD dwMapFlags,
                                              LPCWSTR lpSrcStr, INT cchSrc,
                                              LPWSTR lpDestStr, INT cchDest)
{
    const LocaleTable* table = LocaleTable::Instance();
    const std::string* name = (table != nullptr) ? table->NameForLcid(Locale) : nullptr;
    const char* locale = (name != nullptr) ? name->c_str() : "";
    return StringMapper::Map(locale, dwMapFlags, lpSrcStr, cchSrc, lpDestStr, cchDest);
}
