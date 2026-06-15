// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Single initialization entry point for libnls.
//
// libese's platform initialization calls InitializeNls(dataDir) once, before
// any NLS API is used (it already serializes call ordering). It records the
// data directory, points ICU's data loader at it (for cldr-<ver>.dat), and
// registers codepages.dat as ICU app data.

#include "Win32Types.h"
#include "NlsData.h"

#include <unicode/putil.h>

extern "C" WINBASEAPI BOOL WINAPI InitializeNls(const char* dataDir)
{
    if (dataDir == nullptr)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    NlsData::Configure(dataDir);

    // ICU resolves ucadata.icu / coll/*.res / the cldr-<ver> package from this
    // directory when a collator is opened.
    u_setDataDirectory(dataDir);

    // Surface a missing or garbled codepages.dat at init rather than on the
    // first MultiByteToWideChar.
    if (!NlsData::LoadCodepages())
    {
        SetLastError(ERROR_NOT_SUPPORTED);
        return FALSE;
    }

    return TRUE;
}
