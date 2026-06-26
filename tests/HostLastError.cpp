// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// The single last-error slot libese normally owns. libnls.so leaves SetLastError
// undefined (it imports the host's), so the test executable must provide it; with
// ENABLE_EXPORTS the loader resolves libnls's import to these definitions, and the
// tests read the code back via GetLastError. Mirrors the dual-slot design where
// libese's exported Get/SetLastError interpose libnls's.

#include "Win32Types.h"

static thread_local DWORD gLastError = 0;

WINBASEAPI void WINAPI SetLastError(DWORD code)
{
    gLastError = code;
}

WINBASEAPI DWORD WINAPI GetLastError()
{
    return gLastError;
}
