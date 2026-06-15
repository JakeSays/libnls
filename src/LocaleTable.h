// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// The LCID <-> locale-name mapping, loaded from the lcid item in codepages.dat
// (via ICU's udata layer). Backs LocaleNameToLCID / LCIDToLocaleName /
// IsValidLocale. LCID is a Windows concept with no ICU equivalent, so the
// mapping is data generated from the [MS-LCID] reference, not derived.

#pragma once

#include <stdint.h>

#include <string>
#include <string_view>
#include <unordered_map>

class LocaleTable
{
public:
    // The shared table, loaded on first use. Returns nullptr if the lcid item
    // is missing or malformed.
    static const LocaleTable* Instance();

    // The LCID for a locale name (case-insensitive), or 0 if unknown.
    uint32_t LcidForName(std::string_view name) const;

    // The canonical locale name for an LCID, or nullptr if unknown. Some LCIDs
    // map to several names (shared territories); the first in the table wins.
    const std::string* NameForLcid(uint32_t lcid) const;

    bool IsValidLcid(uint32_t lcid) const;

private:
    bool Load();

    // ASCII lower-cased copy, for case-insensitive name matching.
    static std::string ToLower(std::string_view value);

    // Key is the lower-cased name; locale names are matched case-insensitively.
    std::unordered_map<std::string, uint32_t> _lcidByName;
    std::unordered_map<uint32_t, std::string> _nameByLcid;
};
