// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// GetNLSVersionEx reports the collation version. ESE records it on an index and
// later validates that the engine's version still matches, so what matters is a
// stable, deterministic value tied to the loaded collation data. We use the
// CLDR version carried in the package metadata (read by NlsData on open).

#include "Win32Types.h"
#include "NlsData.h"

#include <winnls.h>

#include <string.h>

#include <string>

namespace
{
    // Packs a "major.minor" CLDR version (e.g. "48.2") into a DWORD as
    // (major << 16) | minor. Non-digit input yields 0.
    DWORD PackVersion(const std::string& version)
    {
        DWORD major = 0;
        DWORD minor = 0;
        size_t i = 0;
        for (; i < version.size() && version[i] != '.'; ++i)
        {
            if (version[i] < '0' || version[i] > '9')
            {
                return 0;
            }
            major = major * 10 + static_cast<DWORD>(version[i] - '0');
        }
        if (i < version.size())
        {
            ++i;
        }
        for (; i < version.size(); ++i)
        {
            if (version[i] < '0' || version[i] > '9')
            {
                return 0;
            }
            minor = minor * 10 + static_cast<DWORD>(version[i] - '0');
        }
        return (major << 16) | minor;
    }
}

extern "C" WINBASEAPI BOOL WINAPI GetNLSVersionEx(NLS_FUNCTION function, LPCWSTR lpLocaleName,
                                                  NLSVERSIONINFOEX* lpVersionInformation)
{
    // The CLDR version is package-wide, so the locale does not change it.
    (void)function;
    (void)lpLocaleName;
    if (lpVersionInformation == nullptr
        || lpVersionInformation->dwNLSVersionInfoSize < sizeof(NLSVERSIONINFO))
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    if (!NlsData::LoadCollation())
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }

    const DWORD packed = PackVersion(NlsData::CollationVersion());
    lpVersionInformation->dwNLSVersion = packed;
    lpVersionInformation->dwDefinedVersion = packed;
    lpVersionInformation->dwEffectiveId = 0;
    memset(&lpVersionInformation->guidCustomVersion, 0, sizeof(GUID));
    return TRUE;
}
