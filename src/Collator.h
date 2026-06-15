// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Caches ICU collators keyed by (locale, Win32 collation flags). LCMapStringEx
// (sort keys), LCMapStringW and CompareStringW all share the cache so a given
// locale/flags combination opens its UCollator once.

#pragma once

#include <stdint.h>

#include <mutex>
#include <string>
#include <unordered_map>

#include <unicode/ucol.h>

class Collator
{
public:
    // A collator for the ICU locale id and the Win32 LCMAP_/NORM_/SORT_ flags
    // that affect collation, or nullptr on failure. The returned collator is
    // owned by the cache and shared; ICU collators are safe for concurrent
    // compare / sort-key use as long as they are not modified.
    static UCollator* Acquire(const char* locale, uint32_t flags);

private:
    // Only these flags change the collator and so participate in the cache key.
    static const uint32_t CollationFlagMask;

    static void ApplyFlags(UCollator* collator, uint32_t flags);

    static std::mutex _mutex;
    static std::unordered_map<std::string, UCollator*> _collators;
};
