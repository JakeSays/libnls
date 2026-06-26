// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Entry point for the libnls test executable. Initializes libnls from the data
// baked into libnls.so (the embedded path), then runs the registered tests.
// An optional argument filters by substring of the dotted test name; --list
// prints the registered names without running.

#include "TestRegistry.h"

#include "Win32Types.h"

#include <print>
#include <string_view>

// libnls's no-data init: installs the packages embedded in libnls.so.
extern "C" WINBASEAPI BOOL WINAPI InitializeNls();

int main(int argc, char** argv)
{
    std::string_view filter;
    bool list = false;
    for (int i = 1; i < argc; ++i)
    {
        const std::string_view arg = argv[i];
        if (arg == "--list")
        {
            list = true;
        }
        else
        {
            filter = arg;
        }
    }

    if (list)
    {
        nls::tests::TestRegistry::Instance().ListTests();
        return 0;
    }

    if (!InitializeNls())
    {
        std::print("InitializeNls() failed: the baked-in NLS data did not load\n");
        return 2;
    }

    return nls::tests::TestRegistry::Instance().RunAll(filter) ? 0 : 1;
}
