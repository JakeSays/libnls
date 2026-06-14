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
# TODO: implement. One TU per concern.
#   src/lcmapstring.cpp   LCMapStringEx / LCMapStringW  -> ucol (+ the LCMAP_*/NORM_*/SORT_* map)
#   src/compare.cpp       CompareString*                -> ucol  (confirm CompareString is live)
#   src/localeinfo.cpp    GetLocaleInfo* / LocaleNameToLCID / LCIDToLocaleName / IsValidLocale
#   src/nlsversion.cpp    GetNLSVersionEx               -> ucol_getVersion / UCA version
#   src/codepage.cpp      MultiByteToWideChar / WideCharToMultiByte -> ucnv
#   src/datetime.cpp      GetDateFormatW / GetTimeFormatW -> udat
#   src/lasterror.cpp     Get/SetLastError (TLS) — mind the libese/libnls dual-slot footgun
file(GLOB NLS_SHIM_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp)

# icu is an OBJECT library: linking it folds its objects (and propagates its PUBLIC
# include dirs + UCONFIG/static defines to our shim TUs) into libnls.so.
add_library(nls SHARED ${NLS_SHIM_SOURCES})
target_link_libraries(nls PRIVATE icu)
target_include_directories(nls PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)

# --no-undefined: libnls must be self-contained — catch missing TUs (e.g. cleanup registries)
# at lib-build time, not consumer-link time. Revisit if an intentional callback into libese is
# added later. --gc-sections drops what's unreferenced.
target_link_options(nls PRIVATE -Wl,--gc-sections -Wl,--no-undefined)

# TODO(later): -fshort-wchar split. ESE builds with it (16-bit wchar_t) and winnls.h sits on
# that boundary; ICU uses char16_t/UChar internally and mostly avoids wchar_t. Apply
# -fshort-wchar to the shim TUs (src/) only — the OBJECT-library split keeps ICU clean of it.

# TODO(step 5): data-regen target (tools/) — CMake-build genrb/gennorm2/root build, run on
# pinned FractionalUCA/CLDR text, embed the result (replaces icu/stubdata). Until then there is
# no usable collation data and ucol_open fails with U_FILE_ACCESS_ERROR.
