// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Initialization entry points for libnls.
//
// libese's platform initialization calls one of these once, before any NLS API
// is used (it already serializes call ordering):
//   * InitializeNls()           — uses the data packages baked into libnls.so.
//   * InitializeNlsWithData(dir) — uses codepages.dat / cldr-<ver>.dat from dir.
// Both surface a missing or garbled codepages package at init rather than on the
// first MultiByteToWideChar; collation data is validated lazily on first use.

#include "Win32Types.h"
#include "NlsData.h"

extern "C" WINBASEAPI BOOL WINAPI InitializeNls()
{
    NlsData::UseEmbeddedData();

    if (!NlsData::LoadCodepages())
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }

    return TRUE;
}

extern "C" WINBASEAPI BOOL WINAPI InitializeNlsWithData(const char* dataDir)
{
    if (dataDir == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    NlsData::UseDataDirectory(dataDir);

    if (!NlsData::LoadCodepages())
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }

    return TRUE;
}
