// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// The sort-key bytes are ICU's format, not Windows'. The port dropped on-disk
// Windows compatibility, so only libnls-vs-libnls reproducibility matters, and
// that is pinned by the collation (CLDR) version.

#include "StringMapper.h"
#include "Collator.h"
#include "WideString.h"

#include <winnls.h>

#include <unicode/ucol.h>
#include <unicode/ustring.h>
#include <unicode/utypes.h>

INT StringMapper::Map(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen)
{
    if (src == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    if ((dwMapFlags & LCMAP_SORTKEY) != 0)
    {
        return SortKey(locale, dwMapFlags, src, srcLen, dest, destLen);
    }
    if ((dwMapFlags & (LCMAP_UPPERCASE | LCMAP_LOWERCASE)) != 0)
    {
        return ChangeCase(locale, dwMapFlags, src, srcLen, dest, destLen);
    }
    if ((dwMapFlags & LCMAP_BYTEREV) != 0)
    {
        return ByteReverse(src, srcLen, dest, destLen);
    }
    SetLastError(ERROR_INVALID_FLAGS);
    return 0;
}

INT StringMapper::SortKey(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen)
{
    UCollator* collator = Collator::Acquire(locale, dwMapFlags);
    if (collator == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    const auto* const source = reinterpret_cast<const UChar*>(src);
    // For sort keys cchDest counts bytes, not WCHARs.
    if (destLen == 0)
    {
        return ucol_getSortKey(collator, source, srcLen, nullptr, 0);
    }
    const int32_t need =
        ucol_getSortKey(collator, source, srcLen, reinterpret_cast<uint8_t*>(dest), destLen);
    if (need == 0 || need > destLen)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return 0;
    }
    return need;
}

INT StringMapper::ChangeCase(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen)
{
    const bool upper = (dwMapFlags & LCMAP_UPPERCASE) != 0;
    // Locale matters only for linguistic casing (e.g. Turkish dotted I).
    const char* caseLocale = ((dwMapFlags & LCMAP_LINGUISTIC_CASING) != 0) ? locale : "";
    const auto* const source = reinterpret_cast<const UChar*>(src);
    auto* const destination = reinterpret_cast<UChar*>(dest);
    UErrorCode status = U_ZERO_ERROR;
    const int32_t need = upper
        ? u_strToUpper(destLen != 0 ? destination : nullptr, destLen, source, srcLen, caseLocale, &status)
        : u_strToLower(destLen != 0 ? destination : nullptr, destLen, source, srcLen, caseLocale, &status);
    if (destLen == 0)
    {
        // Preflight: U_BUFFER_OVERFLOW_ERROR is expected; the length is valid.
        return need;
    }
    if (status == U_BUFFER_OVERFLOW_ERROR || need > destLen)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return 0;
    }
    if (U_FAILURE(status))
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    return need;
}

INT StringMapper::ByteReverse(LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen)
{
    const INT count = (srcLen < 0) ? WideString::Length(src) : srcLen;
    if (destLen == 0)
    {
        return count;
    }
    if (destLen < count)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return 0;
    }
    for (INT i = 0; i < count; ++i)
    {
        const uint16_t unit = static_cast<uint16_t>(src[i]);
        dest[i] = static_cast<WCHAR>((unit << 8) | (unit >> 8));
    }
    return count;
}
