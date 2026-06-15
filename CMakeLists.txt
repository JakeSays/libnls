# libnls (ICU edition) — Win32 NLS surface on a vendored, pinned ICU4C 78.3.
# The vendored ICU subset and all ICU-specific build configuration live in icu/CMakeLists.txt
# (the `icu` OBJECT library). This file builds libnls.so from our Win32 shims and
# folds the ICU objects in. See VENDORING.md and the design doc (/p/ese/localization-layer.md).
cmake_minimum_required(VERSION 3.20)
project(libnls CXX)

set(CMAKE_CXX_STANDARD 17)        # ICU 78 requires C++17
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Pinned raw data (each extracts its tarball in place on a fresh checkout).
#   data/cldr -> CLDR 48.2 (collation + locale data); sets CLDR_DATA_DIR / CLDR_VERSION
#   data/ucd  -> UCD 17.0.0 (character props + normalization base); sets UCD_DATA_DIR / UCD_VERSION
add_subdirectory(data/cldr)
add_subdirectory(data/ucd)

# Vendored ICU subset (OBJECT lib `icu` with all ICU flags/includes/sources).
add_subdirectory(icu)

# --- our Win32 shims over ICU ---------------------------------------------------------------
# One TU per concern; headers listed alongside so they appear in the project.
file(GLOB NLS_SHIM_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp)
file(GLOB NLS_SHIM_HEADERS CONFIGURE_DEPENDS
    ${CMAKE_CURRENT_SOURCE_DIR}/src/*.h
    ${CMAKE_CURRENT_SOURCE_DIR}/include/*.h)

# icu is an OBJECT library: linking it folds its objects (and propagates its PUBLIC
# include dirs + UCONFIG/static defines to our shim TUs) into libnls.so.
add_library(nls SHARED ${NLS_SHIM_SOURCES} ${NLS_SHIM_HEADERS})
target_link_libraries(nls PRIVATE icu)
target_include_directories(nls PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)

# The shim TUs are C++23 (Jake's standard); the vendored ICU OBJECT lib stays at the global
# C++17 it requires. -fshort-wchar makes WCHAR (wchar_t) 16-bit and ABI-compatible with the
# WCHAR libese hands across <winnls.h>; it applies to src/ only (the OBJECT split keeps ICU,
# which uses char16_t/UChar, clean of it). -fno-builtin-wcs* stops clang's idiom recognizer
# from folding our 16-bit-WCHAR loops into libc wcs* calls that walk 4-byte wchar_t.
set_target_properties(nls PROPERTIES CXX_STANDARD 23 CXX_STANDARD_REQUIRED ON)
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

# TODO(step 5): data-regen target (tools/) — CMake-build genrb/gennorm2/root build, run on
# pinned FractionalUCA/CLDR text, embed the result (replaces icu/stubdata). Until then there is
# no usable collation data and ucol_open fails with U_FILE_ACCESS_ERROR.
