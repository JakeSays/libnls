// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// MultiByteToWideChar / WideCharToMultiByte over the windows-1252 table and
// UTF-8. These reproduce the Win32 contract the ESE port relies on (see the
// engine's os/string.cxx call sites):
//
//   * CP_ACP / CP_THREAD_ACP / 1252 -> windows-1252 (CodePageTable).
//   * CP_UTF8                        -> UTF-8.
//   * anything else                  -> ERROR_INVALID_PARAMETER.
//
// Length contract:
//   count == -1: input is null-terminated; the output count includes the
//      terminating null.
//   count > 0: explicit element count; the output count excludes any
//      terminator.
//   output capacity == 0: sizing query; return the required count, write
//      nothing.

#include "Win32Types.h"
#include "CodePageTable.h"
#include "Utf.h"

#include <winnls.h>

#include <stdint.h>

namespace
{
    bool IsAnsiCodePage(UINT codePage)
    {
        return codePage == 0 || codePage == CP_ACP || codePage == CP_THREAD_ACP || codePage == 1252;
    }
}

extern "C" WINBASEAPI INT WINAPI MultiByteToWideChar(UINT CodePage, DWORD dwFlags,
                                                     LPCSTR lpMultiByteStr, INT cbMultiByte,
                                                     LPWSTR lpWideCharStr, INT cchWideChar)
{
    UINT effective = CodePage;
    const CodePageTable* table = nullptr;
    if (IsAnsiCodePage(effective))
    {
        effective = 1252;
        table = CodePageTable::Instance();
        if (table == nullptr)
        {
            SetLastError(ERROR_INVALID_PARAMETER);
            return 0;
        }
    }
    else if (effective != CP_UTF8)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    if (lpMultiByteStr == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    const bool errorOnInvalid = (dwFlags & MB_ERR_INVALID_CHARS) != 0;

    const bool nullTerminated = (cbMultiByte == -1);
    size_t byteCount;
    if (nullTerminated)
    {
        byteCount = 0;
        while (lpMultiByteStr[byteCount] != 0)
        {
            ++byteCount;
        }
        ++byteCount;
    }
    else if (cbMultiByte < 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    else
    {
        byteCount = static_cast<size_t>(cbMultiByte);
    }

    const auto* const begin = reinterpret_cast<const unsigned char*>(lpMultiByteStr);
    const auto* const end = begin + byteCount;
    const auto* p = begin;
    auto* const out = reinterpret_cast<char16_t*>(lpWideCharStr);

    int produced = 0;
    while (p < end)
    {
        const unsigned char byte = *p;
        uint32_t scalar;
        if (byte == 0 && nullTerminated)
        {
            scalar = 0;
            ++p;
        }
        else if (effective == CP_UTF8)
        {
            const auto* const before = p;
            scalar = Utf::DecodeUtf8(p, end);
            if (scalar == 0xFFFD && errorOnInvalid)
            {
                // A real U+FFFD encodes as EF BF BD; anything else producing
                // U+FFFD is malformed.
                const ptrdiff_t consumed = p - before;
                if (!(consumed == 3 && before[0] == 0xEF && before[1] == 0xBF && before[2] == 0xBD))
                {
                    SetLastError(ERROR_NO_UNICODE_TRANSLATION);
                    return 0;
                }
            }
        }
        else
        {
            // 1252: one byte, one scalar, always valid.
            scalar = table->ToUnicode(byte);
            ++p;
        }

        const int wrote = Utf::EncodeUtf16(scalar, nullptr);
        if (cchWideChar > 0)
        {
            if (produced + wrote > cchWideChar)
            {
                SetLastError(ERROR_INSUFFICIENT_BUFFER);
                return 0;
            }
            Utf::EncodeUtf16(scalar, out + produced);
        }
        produced += wrote;

        if (scalar == 0 && nullTerminated)
        {
            break;
        }
    }
    return produced;
}

extern "C" WINBASEAPI INT WINAPI WideCharToMultiByte(UINT CodePage, DWORD dwFlags,
                                                     LPCWSTR lpWideCharStr, INT cchWideChar,
                                                     LPSTR lpMultiByteStr, INT cbMultiByte,
                                                     LPCSTR lpDefaultChar, LPBOOL lpUsedDefaultChar)
{
    if (lpUsedDefaultChar != nullptr)
    {
        *lpUsedDefaultChar = FALSE;
    }

    UINT effective = CodePage;
    const CodePageTable* table = nullptr;
    if (IsAnsiCodePage(effective))
    {
        effective = 1252;
        table = CodePageTable::Instance();
        if (table == nullptr)
        {
            SetLastError(ERROR_INVALID_PARAMETER);
            return 0;
        }
    }
    else if (effective != CP_UTF8)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    if (lpWideCharStr == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }

    const bool errorOnInvalid = (dwFlags & WC_ERR_INVALID_CHARS) != 0;

    const bool nullTerminated = (cchWideChar == -1);
    size_t charCount;
    if (nullTerminated)
    {
        charCount = 0;
        while (lpWideCharStr[charCount] != 0)
        {
            ++charCount;
        }
        ++charCount;
    }
    else if (cchWideChar < 0)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return 0;
    }
    else
    {
        charCount = static_cast<size_t>(cchWideChar);
    }

    const auto* const begin = reinterpret_cast<const char16_t*>(lpWideCharStr);
    const auto* const end = begin + charCount;
    const auto* p = begin;

    // Fallback byte for scalars not representable in 1252. UTF-8 never needs it.
    // The caller may override '?' via lpDefaultChar.
    const char defaultChar = (lpDefaultChar != nullptr && lpDefaultChar[0] != '\0')
                                 ? lpDefaultChar[0]
                                 : '?';

    int produced = 0;
    while (p < end)
    {
        uint32_t scalar;
        if (*p == 0 && nullTerminated)
        {
            scalar = 0;
            ++p;
        }
        else
        {
            scalar = Utf::DecodeUtf16(p, end);
        }

        char buffer[4];
        int wrote;
        if (effective == CP_UTF8)
        {
            wrote = Utf::EncodeUtf8(scalar, buffer);
        }
        else
        {
            const int byte = table->FromUnicode(scalar);
            if (byte < 0)
            {
                if (errorOnInvalid)
                {
                    SetLastError(ERROR_NO_UNICODE_TRANSLATION);
                    return 0;
                }
                if (lpUsedDefaultChar != nullptr)
                {
                    *lpUsedDefaultChar = TRUE;
                }
                buffer[0] = defaultChar;
            }
            else
            {
                buffer[0] = static_cast<char>(byte);
            }
            wrote = 1;
        }

        if (cbMultiByte > 0)
        {
            if (produced + wrote > cbMultiByte)
            {
                SetLastError(ERROR_INSUFFICIENT_BUFFER);
                return 0;
            }
            for (int i = 0; i < wrote; ++i)
            {
                lpMultiByteStr[produced + i] = buffer[i];
            }
        }
        produced += wrote;

        if (scalar == 0 && nullTerminated)
        {
            break;
        }
    }
    return produced;
}
