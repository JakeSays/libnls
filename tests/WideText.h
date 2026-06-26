// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// WCHAR helpers for the tests. libnls is built -fshort-wchar so WCHAR is 16-bit;
// the test target matches, and these avoid the libc wcs* builtins (which walk
// 4-byte wchar_t) just as libnls does.

#pragma once

#include "Win32Types.h"

namespace nls::tests
{
inline bool WideEquals(const WCHAR* left, const WCHAR* right)
{
    while (*left != 0 && *right != 0)
    {
        if (*left != *right)
        {
            return false;
        }
        ++left;
        ++right;
    }
    return *left == *right;
}
}
