# libnls (ICU edition) — Win32 NLS surface on a vendored, pinned ICU4C 78.3.
# The vendored ICU subset and all ICU-specific build configuration live in icu/CMakeLists.txt
# (the `icu` OBJECT library). This file builds libnls.so from our Win32 shims and
# folds the ICU objects in. See VENDORING.md and the design doc (/p/ese/localization-layer.md).
cmake_minimum_required(VERSION 3.20)
project(libnls
    VERSION 1.0.0
    DESCRIPTION "Win32 NLS surface for Linux over vendored ICU"
    LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)        # ICU 78 requires C++17
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Pinned raw data (each extracts its tarball in place on a fresh checkout).
#   data/cldr -> CLDR 48.2 (collation + locale data); sets CLDR_DATA_DIR / CLDR_VERSION
#   data/ucd  -> UCD 17.0.0 (character props + normalization base); sets UCD_DATA_DIR / UCD_VERSION
add_subdirectory(data/cldr)
add_subdirectory(data/ucd)

# Vendored ICU subset (OBJECT lib `icu` with all ICU flags/includes/sources).
add_subdirectory(icu)

# --- regenerate the baked-in NLS data, then embed it ----------------------------------------
# The data generator (C# native-AOT) rebuilds both ICU "CmnD" packages from the pinned CLDR/UCD
# text; src/EmbeddedNlsData.cpp #embeds the result into libnls.so so a default InitializeNls()
# needs no external files. The command reruns only when the generator or the LCID map changes
# (a CLDR/UCD bump is a deliberate version edit that invalidates the build dir anyway).
set(NLS_DATA_GEN
    ${CMAKE_CURRENT_SOURCE_DIR}/tools/NlsDataGenerator/bin/Release/net10.0/nls-data-gen
    CACHE FILEPATH "nls-data-gen executable that builds the NLS data packages")
set(NLS_DATA_OUT ${CMAKE_CURRENT_BINARY_DIR}/nlsdata)
set(NLS_COLLATION_DAT ${NLS_DATA_OUT}/cldr-${CLDR_VERSION}.dat)
set(NLS_CODEPAGES_DAT ${NLS_DATA_OUT}/codepages.dat)

add_custom_command(
    OUTPUT ${NLS_COLLATION_DAT} ${NLS_CODEPAGES_DAT}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${NLS_DATA_OUT}
    COMMAND ${NLS_DATA_GEN}
        --cldr ${CMAKE_CURRENT_SOURCE_DIR}/data/cldr
        --ucd ${UCD_DATA_DIR}
        --out ${NLS_DATA_OUT}
        --cldr-version ${CLDR_VERSION}
        --lcidmap ${CMAKE_CURRENT_SOURCE_DIR}/data/lcidmap.txt
    DEPENDS ${NLS_DATA_GEN} ${CMAKE_CURRENT_SOURCE_DIR}/data/lcidmap.txt
    COMMENT "Generating NLS data packages (cldr-${CLDR_VERSION}.dat, codepages.dat)"
    VERBATIM)
add_custom_target(nls-data DEPENDS ${NLS_COLLATION_DAT} ${NLS_CODEPAGES_DAT})

# --- our Win32 shims over ICU ---------------------------------------------------------------
# One TU per concern; headers listed alongside so they appear in the project.
# icu is an OBJECT library: linking it (below) folds its objects (and propagates its PUBLIC
# include dirs + UCONFIG/static defines to our shim TUs) into libnls.so.
add_library(nls SHARED
    src/CodePage.cpp
    src/CodePageTable.cpp
    src/Collator.cpp
    src/CompareString.cpp
    src/EmbeddedNlsData.cpp
    src/IcuDataItem.cpp
    src/InitializeNls.cpp
    src/LcMapString.cpp
    src/Locale.cpp
    src/LocaleTable.cpp
    src/NlsData.cpp
    src/NlsVersion.cpp
    src/StringMapper.cpp
    src/CodePageTable.h
    src/Collator.h
    src/EmbeddedNlsData.h
    src/IcuDataItem.h
    src/LittleEndian.h
    src/LocaleTable.h
    src/NlsData.h
    src/StringMapper.h
    src/Utf.h
    src/WideString.h
    src/Win32Types.h
    include/winnls.h)
target_link_libraries(nls PRIVATE icu)
target_include_directories(nls PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)

# The shim TUs are C++23 (Jake's standard); the vendored ICU OBJECT lib stays at the global
# C++17 it requires. -fshort-wchar makes WCHAR (wchar_t) 16-bit and ABI-compatible with the
# WCHAR libese hands across <winnls.h>; it applies to src/ only (the OBJECT split keeps ICU,
# which uses char16_t/UChar, clean of it). -fno-builtin-wcs* stops clang's idiom recognizer
# from folding our 16-bit-WCHAR loops into libc wcs* calls that walk 4-byte wchar_t.
# SOVERSION stamps the SONAME as libnls.so.<major> (real file libnls.so.1.0.0), so libese binds
# to the versioned name in its DT_NEEDED, not the unversioned libnls.so dev symlink.
set_target_properties(nls PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
    SOVERSION ${PROJECT_VERSION_MAJOR}
    VERSION ${PROJECT_VERSION})
target_compile_options(nls PRIVATE
    -fshort-wchar
    -fno-builtin-wcslen
    -fno-builtin-wcscmp
    -fno-builtin-wcschr)

# --gc-sections drops what's unreferenced. We intentionally do NOT pass --no-undefined: libnls
# imports Get/SetLastError from libese (the host owns the single last-error TLS slot; libese's
# definitions interpose), so those symbols are unresolved in a standalone libnls.so by design.
# --version-script restricts exports to the Win32 NLS surface; the vendored ICU is private.
target_link_options(nls PRIVATE
    -Wl,--gc-sections
    -Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/libnls.map)

# Bind the embed TU to the regenerated packages: --embed-dir resolves the #embed file names,
# the NLS_EMBEDDED_* macros carry those names (cldr's is version-stamped), and OBJECT_DEPENDS
# rebuilds the object when either package changes. add_dependencies orders the generator first.
set_source_files_properties(${CMAKE_CURRENT_SOURCE_DIR}/src/EmbeddedNlsData.cpp PROPERTIES
    OBJECT_DEPENDS "${NLS_COLLATION_DAT};${NLS_CODEPAGES_DAT}"
    COMPILE_OPTIONS "--embed-dir=${NLS_DATA_OUT}"
    COMPILE_DEFINITIONS "NLS_EMBEDDED_COLLATION=\"cldr-${CLDR_VERSION}.dat\";NLS_EMBEDDED_CODEPAGES=\"codepages.dat\"")
add_dependencies(nls nls-data)

# --- tests ----------------------------------------------------------------------------------
enable_testing()
add_subdirectory(tests)
