// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Owns libnls's runtime data: the directory the data lives in and the ICU
// data packages loaded from it.
//
// libese's platform initialization calls InitializeNls(dataDir) once, before
// any NLS API. There are two packages, both ICU "CmnD" common-data files:
//   * codepages.dat  — the Windows fixed tables (windows-1252, LCID<->locale),
//                      not CLDR-versioned. Registered with ICU as app data
//                      under the package name "codepages".
//   * cldr-<ver>.dat — collation/normalization/case for a CLDR version.
// Everything libnls reads comes from one of these via ICU's udata layer; there
// are no loose sidecar files.

#pragma once

#include <stdint.h>

#include <optional>
#include <string>
#include <string_view>
#include <vector>

class NlsData
{
public:
    // Records the directory libnls loads its data packages from. Called by
    // InitializeNls; the contents are copied.
    static void Configure(std::string_view directory);

    // The recorded directory. Empty until InitializeNls runs.
    static const std::string& Directory();

    // Ensures codepages.dat is loaded from the data directory and registered
    // with ICU as app data under "codepages". Idempotent; returns false if the
    // package is missing or malformed. The codepage and locale tables call
    // this before opening their items with udata_open("codepages", ...).
    static bool LoadCodepages();

    // Ensures a cldr-<ver>.dat is loaded from the data directory and installed
    // as ICU's common data (so ucol_open resolves ucadata.icu / coll/*.res from
    // it). Idempotent; returns false if no package is found or it is malformed.
    // Single-version for now: the first cldr-*.dat in the directory. Multi-CLDR
    // coexistence (any index -> any version) is a future requirement.
    static bool LoadCollation();

    // The CLDR version of the loaded collation package (e.g. "48.2"), read from
    // its metadata item. Empty until LoadCollation succeeds.
    static const std::string& CollationVersion();

private:
    static bool IsConfigured();

    // Reads a file from the data directory in full, or nullopt if it is
    // unreadable.
    static std::optional<std::vector<uint8_t>> ReadFile(std::string_view fileName);

    // The first cldr-*.dat in the data directory, or empty if none.
    static std::string FindCollationPackage();

    // The package's own ToC-entry prefix (e.g. "nlsdata"), read from the first
    // entry of a CmnD package image, or empty if the image is malformed.
    static std::string ExtractPackagePrefix(const std::vector<uint8_t>& data);

    static std::string _directory;
    // Package bytes are kept alive for the process: udata_setAppData /
    // udata_setCommonData reference the data, they do not copy it.
    static std::vector<uint8_t> _codepagesData;
    static bool _codepagesLoaded;
    static std::vector<uint8_t> _collationData;
    static bool _collationLoaded;
    static std::string _collationVersion;
};
