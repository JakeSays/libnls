// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "LocaleTable.h"
#include "IcuDataItem.h"
#include "LittleEndian.h"
#include "NlsData.h"

#include <unicode/udata.h>
#include <unicode/utypes.h>

std::string LocaleTable::ToLower(std::string_view value)
{
    std::string result(value);
    for (auto& c : result)
    {
        if (c >= 'A' && c <= 'Z')
        {
            c = static_cast<char>(c - 'A' + 'a');
        }
    }
    return result;
}

bool LocaleTable::Load()
{
    if (!NlsData::LoadCodepages())
    {
        return false;
    }

    UErrorCode status = U_ZERO_ERROR;
    UDataMemory* data = udata_openChoice(
        "codepages", "nls", "lcid", IcuDataItem::AcceptDataFormat, const_cast<char*>("Lcid"), &status);
    if (U_FAILURE(status) || data == nullptr)
    {
        return false;
    }

    // Item body: uint32 count, then per entry a uint32 LCID, a uint8 name
    // byte-length, and that many ASCII name bytes.
    const auto* p = static_cast<const uint8_t*>(udata_getMemory(data));
    const uint32_t count = LittleEndian::ReadUInt32(p);
    p += 4;
    for (uint32_t i = 0; i < count; ++i)
    {
        const uint32_t lcid = LittleEndian::ReadUInt32(p);
        p += 4;
        const uint8_t length = *p;
        p += 1;
        std::string name(reinterpret_cast<const char*>(p), length);
        p += length;

        _lcidByName.emplace(ToLower(name), lcid);
        // First name for an LCID wins (shared-territory LCIDs).
        _nameByLcid.emplace(lcid, std::move(name));
    }

    udata_close(data);
    return true;
}

const LocaleTable* LocaleTable::Instance()
{
    static LocaleTable table;
    static const bool loaded = table.Load();
    if (!loaded)
    {
        return nullptr;
    }
    return &table;
}

uint32_t LocaleTable::LcidForName(std::string_view name) const
{
    const auto it = _lcidByName.find(ToLower(name));
    if (it == _lcidByName.end())
    {
        return 0;
    }
    return it->second;
}

const std::string* LocaleTable::NameForLcid(uint32_t lcid) const
{
    const auto it = _nameByLcid.find(lcid);
    if (it == _nameByLcid.end())
    {
        return nullptr;
    }
    return &it->second;
}

bool LocaleTable::IsValidLcid(uint32_t lcid) const
{
    return _nameByLcid.find(lcid) != _nameByLcid.end();
}
