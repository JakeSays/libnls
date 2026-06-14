# Pinned UCD (Unicode 17.0.0 — the version CLDR 48.2's uca/ headers require:
# allkeys_CLDR.txt / FractionalUCA.txt declare "UCD Version: 17.0.0"). The tarball is tracked
# in git; the extracted UCD files are gitignored (see data/.gitignore). On a fresh checkout
# only the tarball is present, so extract it in place if the data isn't there yet.
#
# This is a third pinned source, independent of both CLDR and ICU: ICU supplies only tools
# (preparseucd.py / genprops / gennorm2 derive ppucd.txt + norm source + props/norm from these
# raw UCD files); no character/normalization data ever comes from ICU.
set(UCD_VERSION 17.0.0)
set(UCD_TARBALL ${CMAKE_CURRENT_SOURCE_DIR}/ucd.tar.xz)
set(UCD_DATA_DIR ${CMAKE_CURRENT_SOURCE_DIR})

if(NOT EXISTS ${UCD_DATA_DIR}/UnicodeData.txt)
    if(NOT EXISTS ${UCD_TARBALL})
        message(FATAL_ERROR "UCD ${UCD_VERSION}: neither extracted data nor ${UCD_TARBALL} found")
    endif()
    message(STATUS "UCD ${UCD_VERSION}: extracting ${UCD_TARBALL} in place")
    file(ARCHIVE_EXTRACT INPUT ${UCD_TARBALL} DESTINATION ${UCD_DATA_DIR})
else()
    message(STATUS "UCD ${UCD_VERSION}: data already extracted")
endif()

# Expose the data root to the rest of the build (the data-regen target consumes it).
set(UCD_DATA_DIR ${UCD_DATA_DIR} PARENT_SCOPE)
set(UCD_VERSION ${UCD_VERSION} PARENT_SCOPE)
