// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Owns libnls's runtime data: two ICU "CmnD" common-data packages, each loaded
// either from the copy baked into libnls.so (the default) or from a directory.
//   * codepages.dat  — the Windows fixed tables (windows-1252, LCID<->locale),
//                      not CLDR-versioned. Registered with ICU as app data under
//                      the package name "codepages".
//   * cldr-<ver>.dat — collation/normalization/case for a CLDR version, installed
//                      as ICU's common data.
// Everything libnls reads comes from one of these via ICU's udata layer.
//
// InitializeNls() selects the baked-in copies; InitializeNlsWithData(dir) selects
// a directory. Embedded is the default, so the NLS APIs work even if neither runs
// first. Both Load* methods are idempotent and lazy.

#pragma once

#include <stdint.h>

#include <filesystem>
#include <string>
#include <string_view>
#include <vector>

class NlsData
{
public:
    // Selects the packages baked into libnls.so (the default source).
    static void UseEmbeddedData();

    // Selects packages loaded from directory instead of the baked-in copies.
    // The contents are copied.
    static void UseDataDirectory(std::string_view directory);

    // Ensures codepages.dat is installed and registered with ICU as app data
    // under "codepages". Idempotent; returns false if the package is missing or
    // malformed. The codepage and locale tables call this before opening their
    // items with udata_open("codepages", ...).
    static bool LoadCodepages();

    // Ensures cldr-<ver>.dat is installed as ICU's common data (so ucol_open
    // resolves ucadata.icu / coll/*.res from it). Idempotent; returns false if no
    // package is found or it is malformed. Single-version for now: the directory
    // source uses the first cldr-*.dat it finds. Multi-CLDR coexistence (any
    // index -> any version) is a future requirement.
    static bool LoadCollation();

    // The CLDR version of the loaded collation package (e.g. "48.2"), read from
    // its metadata item. Empty until LoadCollation succeeds.
    static const std::string& CollationVersion();

private:
    // Reads a file from the data directory in full into bytes. Returns false if
    // it is unreadable.
    static bool ReadFile(std::string_view fileName, std::vector<uint8_t>& bytes);

    // The first cldr-*.dat in the data directory, or empty if none.
    static std::string FindCollationPackage();

    // The package's own ToC-entry prefix (e.g. "nlsdata"), read from the first
    // entry of a CmnD package image, or empty if the image is malformed.
    static std::string ExtractPackagePrefix(const uint8_t* data, size_t size);

    // Whether to load from _directory rather than the baked-in copies.
    static bool _useDirectory;
    static std::filesystem::path _directory;
    // Directory-loaded bytes are kept alive for the process: udata_setAppData /
    // udata_setCommonData reference the data, they do not copy it. (The baked-in
    // copies live in .rodata, so they need no buffer here.)
    static std::vector<uint8_t> _codepagesData;
    static bool _codepagesLoaded;
    static std::vector<uint8_t> _collationData;
    static bool _collationLoaded;
    static std::string _collationVersion;
};
