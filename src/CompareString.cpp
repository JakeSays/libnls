// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// CompareStringW over ICU collation (ucol_strcoll, no sort-key materialization).

#include "Win32Types.h"
#include "Collator.h"
#include "LocaleTable.h"

#include <winnls.h>

#include <unicode/ucol.h>

#include <string>

extern "C" WINBASEAPI INT WINAPI CompareStringW(LCID Locale, DWORD dwCmpFlags,
                                                LPCWSTR lpString1, INT cchCount1,
                                                LPCWSTR lpString2, INT cchCount2)
{
    if (lpString1 == nullptr || lpString2 == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    const LocaleTable* table = LocaleTable::Instance();
    const std::string* name = (table != nullptr) ? table->NameForLcid(Locale) : nullptr;
    const char* locale = (name != nullptr) ? name->c_str() : "";

    UCollator* collator = Collator::Acquire(locale, dwCmpFlags);
    if (collator == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    // ICU accepts -1 for a null-terminated string, matching the Win32 contract.
    const UCollationResult result = ucol_strcoll(
        collator,
        reinterpret_cast<const UChar*>(lpString1), cchCount1,
        reinterpret_cast<const UChar*>(lpString2), cchCount2);

    switch (result)
    {
        case UCOL_LESS:
            return CSTR_LESS_THAN;
        case UCOL_EQUAL:
            return CSTR_EQUAL;
        case UCOL_GREATER:
            return CSTR_GREATER_THAN;
    }

    SetLastError(ERROR_INVALID_PARAMETER);
    return 0;
}
