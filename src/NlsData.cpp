// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "NlsData.h"
#include "IcuDataItem.h"
#include "LittleEndian.h"

#include <unicode/udata.h>
#include <unicode/utypes.h>

#include <filesystem>
#include <fstream>

// Set once by InitializeNls before any NLS API runs, so no guard is needed
// (the init contract serializes the write against all reads).
std::string NlsData::_directory;
std::vector<uint8_t> NlsData::_codepagesData;
bool NlsData::_codepagesLoaded = false;
std::vector<uint8_t> NlsData::_collationData;
bool NlsData::_collationLoaded = false;
std::string NlsData::_collationVersion;

void NlsData::Configure(std::string_view directory)
{
    _directory.assign(directory);
}

const std::string& NlsData::Directory()
{
    return _directory;
}

bool NlsData::IsConfigured()
{
    return !_directory.empty();
}

std::optional<std::vector<uint8_t>> NlsData::ReadFile(std::string_view fileName)
{
    if (!IsConfigured())
    {
        return std::nullopt;
    }

    const auto path = std::filesystem::path(_directory) / std::filesystem::path(fileName);
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
    {
        return std::nullopt;
    }

    stream.seekg(0, std::ios::end);
    const auto end = stream.tellg();
    if (end < 0)
    {
        return std::nullopt;
    }
    stream.seekg(0, std::ios::beg);

    std::vector<uint8_t> bytes(static_cast<size_t>(end));
    if (!bytes.empty() && !stream.read(reinterpret_cast<char*>(bytes.data()), end))
    {
        return std::nullopt;
    }
    return bytes;
}

std::string NlsData::FindCollationPackage()
{
    std::error_code error;
    for (const auto& entry : std::filesystem::directory_iterator(_directory, error))
    {
        const auto name = entry.path().filename().string();
        if (name.starts_with("cldr-") && name.ends_with(".dat"))
        {
            return name;
        }
    }
    return {};
}

std::string NlsData::ExtractPackagePrefix(const std::vector<uint8_t>& data)
{
    // CmnD layout: MappedData { uint16 headerSize; uint8 0xDA; uint8 0x27 },
    // then the offset ToC at headerSize: uint32 count, then count entries of
    // { uint32 nameOffset; uint32 dataOffset } relative to the ToC base. The
    // first entry's name ("<prefix>/...") gives the package prefix.
    if (data.size() < 4 || data[2] != 0xDA || data[3] != 0x27)
    {
        return {};
    }
    const uint16_t headerSize = LittleEndian::ReadUInt16(data.data());
    if (data.size() < static_cast<size_t>(headerSize) + 8)
    {
        return {};
    }
    const uint8_t* const toc = data.data() + headerSize;
    if (LittleEndian::ReadUInt32(toc) == 0)
    {
        return {};
    }
    const uint32_t nameOffset = LittleEndian::ReadUInt32(toc + 4);
    if (static_cast<size_t>(headerSize) + nameOffset >= data.size())
    {
        return {};
    }
    std::string prefix;
    for (const char* c = reinterpret_cast<const char*>(toc + nameOffset); *c != 0 && *c != '/'; ++c)
    {
        prefix.push_back(*c);
    }
    return prefix;
}

bool NlsData::LoadCodepages()
{
    if (_codepagesLoaded)
    {
        return true;
    }

    auto bytes = ReadFile("codepages.dat");
    if (!bytes)
    {
        return false;
    }
    _codepagesData = std::move(*bytes);

    UErrorCode status = U_ZERO_ERROR;
    udata_setAppData("codepages", _codepagesData.data(), &status);
    if (U_FAILURE(status))
    {
        _codepagesData.clear();
        return false;
    }

    _codepagesLoaded = true;
    return true;
}

bool NlsData::LoadCollation()
{
    if (_collationLoaded)
    {
        return true;
    }

    const std::string package = FindCollationPackage();
    if (package.empty())
    {
        return false;
    }

    auto bytes = ReadFile(package);
    if (!bytes)
    {
        return false;
    }
    _collationData = std::move(*bytes);

    // The package's ToC entries are prefixed with its own name (read from the
    // package itself, not the file name). Tell ICU's loader to build its common
    // data lookups with that prefix instead of U_ICUDATA_NAME.
    const std::string prefix = ExtractPackagePrefix(_collationData);
    if (prefix.empty())
    {
        _collationData.clear();
        return false;
    }
    udata_setICUDataPackage(prefix.c_str());

    UErrorCode status = U_ZERO_ERROR;
    udata_setCommonData(_collationData.data(), &status);
    if (U_FAILURE(status))
    {
        _collationData.clear();
        return false;
    }

    // The CLDR version travels in the package's metadata/cldrversion.nls item
    // (dataFormat "NlsM"): a uint8 length followed by that many ASCII bytes. The
    // "ICUDATA-metadata" path resolves the "metadata" tree of the common data
    // under whatever prefix was just set.
    UDataMemory* meta = udata_openChoice(
        "ICUDATA-metadata", "nls", "cldrversion", IcuDataItem::AcceptDataFormat, const_cast<char*>("NlsM"), &status);
    if (U_SUCCESS(status) && meta != nullptr)
    {
        const auto* const payload = static_cast<const uint8_t*>(udata_getMemory(meta));
        const uint8_t length = payload[0];
        _collationVersion.assign(reinterpret_cast<const char*>(payload + 1), length);
        udata_close(meta);
    }

    _collationLoaded = true;
    return true;
}

const std::string& NlsData::CollationVersion()
{
    return _collationVersion;
}
