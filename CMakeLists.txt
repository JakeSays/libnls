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
# One TU per concern, compiled once into an OBJECT library that both the shared and static products
# below reuse. The shim TUs are C++23 (Jake's standard); the vendored ICU OBJECT lib stays at the
# global C++17 it requires. -fshort-wchar makes WCHAR (wchar_t) 16-bit and ABI-compatible with the
# WCHAR libese hands across <winnls.h>; -fno-builtin-wcs* stops clang's idiom recognizer from
# folding our 16-bit-WCHAR loops into libc wcs* calls that walk 4-byte wchar_t. -fvisibility=hidden
# keeps every symbol but the WINBASEAPI-marked public surface internal -- the .so additionally has
# the version script, but the static archive (where no version script applies) relies on this so
# libnls.a does not leak the shims' or ICU's symbols into libese.
add_library(nls-shims OBJECT
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
# PRIVATE icu gives the shims ICU's public include dirs + UCONFIG/static defines to compile against
# (it does not fold ICU objects in -- the final products link icu for that). PIC so the same
# objects serve both the .so and a libnls.a linked into a shared libese.
target_link_libraries(nls-shims PRIVATE icu)
target_include_directories(nls-shims PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)
set_target_properties(nls-shims PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
    POSITION_INDEPENDENT_CODE ON)
target_compile_options(nls-shims PRIVATE
    -fshort-wchar
    -fno-builtin-wcslen
    -fno-builtin-wcscmp
    -fno-builtin-wcschr
    -fvisibility=hidden)
add_dependencies(nls-shims nls-data)

# Bind the embed TU to the regenerated packages: --embed-dir resolves the #embed file names, the
# NLS_EMBEDDED_* macros carry those names (cldr's is version-stamped), and OBJECT_DEPENDS rebuilds
# the object when either package changes. The data is compiled into the shim object once and is
# carried by both the .so and the .a.
set_source_files_properties(${CMAKE_CURRENT_SOURCE_DIR}/src/EmbeddedNlsData.cpp PROPERTIES
    OBJECT_DEPENDS "${NLS_COLLATION_DAT};${NLS_CODEPAGES_DAT}"
    COMPILE_OPTIONS "--embed-dir=${NLS_DATA_OUT}"
    COMPILE_DEFINITIONS "NLS_EMBEDDED_COLLATION=\"cldr-${CLDR_VERSION}.dat\";NLS_EMBEDDED_CODEPAGES=\"codepages.dat\"")

# ICU is private to libnls in both products. The .so enforces that with the version script; the
# static archive relies on visibility, so compile ICU hidden too -- U_STATIC_IMPLEMENTATION (set in
# icu/CMakeLists.txt) makes U_EXPORT inert, so nothing forces ICU's API back to default visibility.
# The .so's exported set is unchanged: the version script already localized everything but the 12
# public functions.
target_compile_options(icu PRIVATE -fvisibility=hidden)

# --- shared product: libnls.so.<version> ----------------------------------------------------
# Linking icu (OBJECT) folds its objects in alongside the shim objects. Ship a single artifact
# named libnls.so.<version> with no symlinks: naming the output via a custom suffix (rather than
# VERSION/SOVERSION, which generate the libnls.so / libnls.so.1 symlink chain) leaves CMake's
# default SONAME equal to that real file name, so libese records the full libnls.so.1.0.0 in
# DT_NEEDED and the loader opens exactly that file.
add_library(nls SHARED $<TARGET_OBJECTS:nls-shims>)
target_link_libraries(nls PRIVATE icu)
target_include_directories(nls PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
set_target_properties(nls PROPERTIES
    LINKER_LANGUAGE CXX
    SUFFIX ".so.${PROJECT_VERSION}")
# --gc-sections drops what's unreferenced. We intentionally do NOT pass --no-undefined: libnls
# imports Get/SetLastError from libese (the host owns the single last-error TLS slot; libese's
# definitions interpose), so those symbols are unresolved in a standalone libnls.so by design.
# --version-script restricts exports to the Win32 NLS surface; the vendored ICU is private.
target_link_options(nls PRIVATE
    -Wl,--gc-sections
    -Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/libnls.map)

# --- static product: libnls.a ---------------------------------------------------------------
# The same shim + ICU objects archived together, NLS data embedded, so libese can link libnls.a
# directly and InitializeNls() needs nothing else on disk. The hidden visibility above keeps the
# archive from leaking the shims' or ICU's symbols into whatever links it.
add_library(nls-static STATIC $<TARGET_OBJECTS:nls-shims>)
target_link_libraries(nls-static PRIVATE icu)
target_include_directories(nls-static PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
set_target_properties(nls-static PROPERTIES
    LINKER_LANGUAGE CXX
    OUTPUT_NAME nls)

# --- tests ----------------------------------------------------------------------------------
enable_testing()
add_subdirectory(tests)
