# libnls test executable. Links libnls.so and drives its Win32 NLS surface against
# the data baked into the library (the embedded InitializeNls() path). The tests
# provide the host's Set/GetLastError (HostLastError.cpp); ENABLE_EXPORTS publishes
# them so libnls.so's SetLastError import resolves back into this executable, just
# as libese's exported slot interposes once libnls is loaded into the engine.

add_executable(nls-tests
    framework/TestRegistry.cpp
    framework/TestAbort.h
    framework/RegisteredTest.h
    framework/TestRegistry.h
    framework/Check.h
    WideText.h
    HostLastError.cpp
    TestMain.cpp
    CodePageTests.cpp
    LocaleTests.cpp
    CompareStringTests.cpp
    LocaleCollationTests.cpp
    LcMapStringTests.cpp
    NlsVersionTests.cpp
    EmbeddedDataTests.cpp)

# C++23 to match the shims, with the same -fshort-wchar ABI (16-bit WCHAR) and the
# wcs* builtin suppression so WCHAR literals/loops are not folded into 4-byte libc calls.
set_target_properties(nls-tests PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
    ENABLE_EXPORTS ON)
target_compile_options(nls-tests PRIVATE
    -fshort-wchar
    -fno-builtin-wcslen
    -fno-builtin-wcscmp
    -fno-builtin-wcschr)

# include/ (the public <winnls.h>) comes from the nls target's PUBLIC include dir;
# src/ supplies Win32Types.h, the shared Win32 vocab the surface is written against.
target_include_directories(nls-tests PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/framework
    ${CMAKE_SOURCE_DIR}/src)
target_link_libraries(nls-tests PRIVATE nls)

add_test(NAME nls COMMAND nls-tests)
