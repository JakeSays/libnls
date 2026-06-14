# Pinned CLDR data (48.2). The tarball is tracked in git; the extracted tree (common/, LICENSE,
# README-common.md) is gitignored (see .gitignore). On a fresh checkout only the tarball is
# present, so extract it in place if the data isn't there yet. The pin (48.2) is part of the
# collation version surfaced through GetNLSVersionEx — bump deliberately, with a reindex.
set(CLDR_VERSION 48.2)
set(CLDR_TARBALL ${CMAKE_CURRENT_SOURCE_DIR}/cldr-${CLDR_VERSION}.tar.xz)
set(CLDR_DATA_DIR ${CMAKE_CURRENT_SOURCE_DIR}/common)

if(NOT EXISTS ${CLDR_DATA_DIR})
    if(NOT EXISTS ${CLDR_TARBALL})
        message(FATAL_ERROR "CLDR ${CLDR_VERSION}: neither extracted data nor ${CLDR_TARBALL} found")
    endif()
    message(STATUS "CLDR ${CLDR_VERSION}: extracting ${CLDR_TARBALL} in place")
    file(ARCHIVE_EXTRACT INPUT ${CLDR_TARBALL} DESTINATION ${CMAKE_CURRENT_SOURCE_DIR})
else()
    message(STATUS "CLDR ${CLDR_VERSION}: data already extracted")
endif()

# Expose the data root to the rest of the build (the step-5 data-regen target consumes it).
set(CLDR_DATA_DIR ${CLDR_DATA_DIR} PARENT_SCOPE)
set(CLDR_VERSION ${CLDR_VERSION} PARENT_SCOPE)
