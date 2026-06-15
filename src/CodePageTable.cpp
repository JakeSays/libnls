// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "CodePageTable.h"
#include "IcuDataItem.h"
#include "LittleEndian.h"
#include "NlsData.h"

#include <unicode/udata.h>
#include <unicode/utypes.h>

bool CodePageTable::Load()
{
    if (!NlsData::LoadCodepages())
    {
        return false;
    }

    UErrorCode status = U_ZERO_ERROR;
    UDataMemory* data = udata_openChoice(
        "codepages", "nls", "cp1252", IcuDataItem::AcceptDataFormat, const_cast<char*>("Cp52"), &status);
    if (U_FAILURE(status) || data == nullptr)
    {
        return false;
    }

    // The item body is 256 little-endian uint16 (the ICU header precedes the
    // pointer udata_getMemory returns).
    const auto* const scalars = static_cast<const uint8_t*>(udata_getMemory(data));
    for (uint32_t b = 0; b < EntryCount; ++b)
    {
        const uint16_t scalar = LittleEndian::ReadUInt16(scalars + b * 2);
        _toUnicode[b] = scalar;
        // cp1252's byte->scalar map is injective, so the first byte claiming a
        // scalar owns the reverse mapping.
        _fromUnicode.emplace(scalar, static_cast<uint8_t>(b));
    }

    udata_close(data);
    return true;
}

const CodePageTable* CodePageTable::Instance()
{
    static CodePageTable table;
    static const bool loaded = table.Load();
    if (!loaded)
    {
        return nullptr;
    }
    return &table;
}

uint16_t CodePageTable::ToUnicode(unsigned char byte) const
{
    return _toUnicode[byte];
}

int CodePageTable::FromUnicode(uint32_t scalar) const
{
    if (scalar > 0xFFFF)
    {
        return -1;
    }
    const auto it = _fromUnicode.find(static_cast<uint16_t>(scalar));
    if (it == _fromUnicode.end())
    {
        return -1;
    }
    return it->second;
}
