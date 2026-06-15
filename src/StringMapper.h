// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// The LCMapString operation, shared by LCMapStringEx (locale name) and
// LCMapStringW (LCID -> name). Resolves an LCMAP_ request against ICU
// collation / case mapping.

#pragma once

#include "Win32Types.h"

class StringMapper
{
public:
    // Maps src into dest per dwMapFlags for the given ICU locale id, returning
    // the produced length (bytes for LCMAP_SORTKEY, WCHARs otherwise) or 0 on
    // error. destLen == 0 is a sizing query.
    static INT Map(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen);

private:
    static INT SortKey(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen);
    static INT ChangeCase(const char* locale, DWORD dwMapFlags, LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen);
    static INT ByteReverse(LPCWSTR src, INT srcLen, LPWSTR dest, INT destLen);
};
