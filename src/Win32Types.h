// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Win32 base-type vocabulary for libnls's internal translation units.
//
// The public surface (<winnls.h>) is written against the Win32 type names
// (DWORD, LCID, WCHAR, LPCWSTR, GUID, ...) and assumes they are already in
// scope, exactly as the Windows SDK does. On Windows that comes from
// <windows.h>; here it comes from this header. Every libnls .cpp includes
// this before <winnls.h>.
//
// libnls is built with -fshort-wchar so WCHAR (wchar_t) is 16 bits and
// binary-compatible with the WCHAR that libese passes across the boundary.

#pragma once

#include <stddef.h>
#include <stdint.h>
#include <wchar.h>

// <winnls.h> keeps its strongly-typed callback typedefs behind STRICT;
// without it those typedefs collapse to a generic FARPROC and the
// declarations taking them do not parse. We always want the typed form.
#ifndef STRICT
#define STRICT 1
#endif

#ifdef __cplusplus
extern "C"
{
#endif

typedef int32_t BOOL;
typedef uint8_t BYTE;
typedef char CHAR;
typedef uint32_t DWORD;
typedef uint64_t DWORDLONG;
typedef void* HANDLE;
typedef HANDLE HMODULE;
typedef int32_t INT;
typedef uint16_t LANGID;
typedef DWORD LCID;
typedef int32_t LONG;
typedef int64_t LONGLONG;
typedef short SHORT;
typedef wchar_t WCHAR;
typedef uint16_t WORD;
typedef uint16_t USHORT;
typedef unsigned int UINT;
typedef DWORD ULONG;
typedef uint64_t ULONGLONG;
typedef void VOID;

typedef CHAR* LPSTR;
typedef const CHAR* LPCSTR;
typedef WCHAR* LPWSTR;
typedef const WCHAR* LPCWSTR;
typedef CHAR* PSTR;
typedef const CHAR* PCSTR;
typedef WCHAR* PWSTR;
typedef const WCHAR* PCWSTR;
typedef LPCSTR LPCCH;
typedef LPCWSTR LPCWCH;
typedef LPSTR LPCH;
typedef LPWSTR LPWCH;
typedef void* LPVOID;
typedef LCID* LPLCID;
typedef WORD* LPWORD;
typedef BOOL* LPBOOL;
typedef DWORD* LPDWORD;

typedef intptr_t INT_PTR;
typedef uintptr_t UINT_PTR;
typedef intptr_t LONG_PTR;
typedef uintptr_t ULONG_PTR;
typedef intptr_t LPARAM;

// <winnls.h> guards these behind *_DEFINED so it does not redefine them;
// set the guards and own the definitions here.
#define PULONG_DEFINED
typedef ULONG* PULONG;
typedef ULONGLONG* PULONGLONG;
#define PZZWSTR_DEFINED
typedef WCHAR* PZZWSTR;
typedef const WCHAR* PCZZWSTR;
#define FARPROC_DEFINED
typedef INT_PTR (*FARPROC)(void);

#ifndef TRUE
#define TRUE 1
#endif
#ifndef FALSE
#define FALSE 0
#endif

// Calling-convention and export macros. On Linux x86-64/aarch64 there is a
// single ABI, so the convention macros expand to nothing; libnls carries the
// Win32 spellings for surface compatibility. WINBASEAPI gives every public
// NLS function default visibility so libese sees it when linking libnls.so.
#ifndef WINAPI
#define WINAPI
#endif
#ifndef CALLBACK
#define CALLBACK
#endif
#ifndef WINBASEAPI
#define WINBASEAPI __attribute__((visibility("default")))
#endif
#ifndef DECLSPEC_IMPORT
#define DECLSPEC_IMPORT
#endif

// Win32 GUID, embedded in NLSVERSIONINFO. Layout must match Win32; libnls
// never constructs one itself.
#ifndef GUID_DEFINED
#define GUID_DEFINED
typedef struct _GUID
{
    DWORD Data1;
    WORD Data2;
    WORD Data3;
    BYTE Data4[8];
} GUID;
#endif

#ifndef MAX_PATH
#define MAX_PATH 260
#endif

#ifndef LOCALE_NAME_MAX_LENGTH
#define LOCALE_NAME_MAX_LENGTH 85
#endif

#ifndef LOWORD
#define LOWORD(x) ((WORD)((DWORD)(x) & 0xffff))
#endif
#ifndef HIWORD
#define HIWORD(x) ((WORD)(((DWORD)(x) >> 16) & 0xffff))
#endif

// SAL annotations are inert off-MSVC; define them away so <winnls.h> parses.
#ifndef _In_
#define _In_
#endif
#ifndef _In_opt_
#define _In_opt_
#endif
#ifndef _Out_
#define _Out_
#endif
#ifndef _Out_opt_
#define _Out_opt_
#endif
#ifndef _Inout_
#define _Inout_
#endif
#ifndef _Reserved_
#define _Reserved_
#endif

// Win32 status codes libnls sets via SetLastError. Values match winerror.h.
#ifndef ERROR_SUCCESS
#define ERROR_SUCCESS 0L
#endif
#ifndef ERROR_INVALID_PARAMETER
#define ERROR_INVALID_PARAMETER 87L
#endif
#ifndef ERROR_INSUFFICIENT_BUFFER
#define ERROR_INSUFFICIENT_BUFFER 122L
#endif
#ifndef ERROR_INVALID_FLAGS
#define ERROR_INVALID_FLAGS 1004L
#endif
#ifndef ERROR_NO_UNICODE_TRANSLATION
#define ERROR_NO_UNICODE_TRANSLATION 1113L
#endif
#ifndef ERROR_NOT_SUPPORTED
#define ERROR_NOT_SUPPORTED 50L
#endif
#ifndef ERROR_CALL_NOT_IMPLEMENTED
#define ERROR_CALL_NOT_IMPLEMENTED 120L
#endif

// libese exports Get/SetLastError from libese.map so its TLS slot interposes
// libnls's at load time (the dual-slot fix). libnls declares and defines its
// own with default visibility for the standalone build; the interposition is
// what makes a single error slot shared once loaded into libese.
WINBASEAPI void WINAPI SetLastError(DWORD code);
WINBASEAPI DWORD WINAPI GetLastError(void);

#ifdef __cplusplus
}
#endif
