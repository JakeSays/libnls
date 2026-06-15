// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "Collator.h"
#include "NlsData.h"
#include "Win32Types.h"

#include <winnls.h>

#include <unicode/utypes.h>

const uint32_t Collator::CollationFlagMask =
    NORM_IGNORECASE | NORM_IGNORENONSPACE | NORM_IGNORESYMBOLS
    | LINGUISTIC_IGNORECASE | LINGUISTIC_IGNOREDIACRITIC
    | SORT_STRINGSORT | SORT_DIGITSASNUMBERS;

std::mutex Collator::_mutex;
std::unordered_map<std::string, UCollator*> Collator::_collators;

void Collator::ApplyFlags(UCollator* collator, uint32_t flags)
{
    UErrorCode status = U_ZERO_ERROR;

    // Strength: ignoring diacritics drops to primary, ignoring case to
    // secondary, otherwise the default tertiary.
    UColAttributeValue strength = UCOL_TERTIARY;
    if ((flags & (NORM_IGNORENONSPACE | LINGUISTIC_IGNOREDIACRITIC)) != 0)
    {
        strength = UCOL_PRIMARY;
    }
    else if ((flags & (NORM_IGNORECASE | LINGUISTIC_IGNORECASE)) != 0)
    {
        strength = UCOL_SECONDARY;
    }
    ucol_setAttribute(collator, UCOL_STRENGTH, strength, &status);

    // Punctuation/symbol handling. NORM_IGNORESYMBOLS shifts punctuation out of
    // the primary level; SORT_STRINGSORT forces it to sort as ordinary
    // characters (non-ignorable).
    if ((flags & NORM_IGNORESYMBOLS) != 0)
    {
        ucol_setAttribute(collator, UCOL_ALTERNATE_HANDLING, UCOL_SHIFTED, &status);
    }
    else if ((flags & SORT_STRINGSORT) != 0)
    {
        ucol_setAttribute(collator, UCOL_ALTERNATE_HANDLING, UCOL_NON_IGNORABLE, &status);
    }

    if ((flags & SORT_DIGITSASNUMBERS) != 0)
    {
        ucol_setAttribute(collator, UCOL_NUMERIC_COLLATION, UCOL_ON, &status);
    }
}

UCollator* Collator::Acquire(const char* locale, uint32_t flags)
{
    if (!NlsData::LoadCollation())
    {
        return nullptr;
    }

    const uint32_t collationFlags = flags & CollationFlagMask;
    std::string key = std::to_string(collationFlags);
    key.push_back(':');
    key.append(locale != nullptr ? locale : "");

    std::lock_guard lock(_mutex);
    const auto it = _collators.find(key);
    if (it != _collators.end())
    {
        return it->second;
    }

    UErrorCode status = U_ZERO_ERROR;
    UCollator* collator = ucol_open(locale, &status);
    if (U_FAILURE(status) || collator == nullptr)
    {
        if (collator != nullptr)
        {
            ucol_close(collator);
        }
        return nullptr;
    }

    ApplyFlags(collator, collationFlags);
    _collators.emplace(std::move(key), collator);
    return collator;
}
