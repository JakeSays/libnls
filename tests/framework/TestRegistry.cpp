// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "TestRegistry.h"
#include "TestAbort.h"

#include <print>

namespace nls::tests
{
TestRegistry& TestRegistry::Instance()
{
    static TestRegistry instance;
    return instance;
}

int TestRegistry::Register(std::string name, RegisteredTest::Function function)
{
    _tests.emplace_back(std::move(name), function);
    return 0;
}

bool TestRegistry::RunAll(std::string_view filter)
{
    uint32_t passed = 0;
    uint32_t failed = 0;
    uint32_t skipped = 0;

    for (const auto& test : _tests)
    {
        if (!filter.empty() && test.Name().find(filter) == std::string::npos)
        {
            ++skipped;
            continue;
        }

        _currentFailures = 0;
        std::print("[ RUN  ] {}\n", test.Name());
        try
        {
            test.Run();
        }
        catch (const TestAbort&)
        {
            // A REQUIRE failed; it was already counted and reported.
        }

        if (_currentFailures == 0)
        {
            ++passed;
            std::print("[  OK  ] {}\n", test.Name());
        }
        else
        {
            ++failed;
            std::print("[ FAIL ] {} ({} failed checks)\n", test.Name(), _currentFailures);
        }
    }

    std::print("\n");
    std::print("{} passed, {} failed, {} skipped; {} checks ({} failed)\n",
               passed, failed, skipped, _totalChecks, _failedChecks);
    return failed == 0;
}

void TestRegistry::ListTests() const
{
    for (const auto& test : _tests)
    {
        std::print("{}\n", test.Name());
    }
}

bool TestRegistry::Check(bool condition, const char* expression, const char* file, int line)
{
    ++_totalChecks;
    if (!condition)
    {
        ++_currentFailures;
        ++_failedChecks;
        std::print("    FAIL {}:{}: CHECK({})\n", file, line, expression);
    }
    return condition;
}

bool TestRegistry::CheckEqual(long long actual, long long expected,
                              const char* actualExpression, const char* expectedExpression,
                              const char* file, int line)
{
    ++_totalChecks;
    if (actual != expected)
    {
        ++_currentFailures;
        ++_failedChecks;
        std::print("    FAIL {}:{}: CHECK_EQ({}, {}) -- got {} (0x{:X}), expected {} (0x{:X})\n",
                   file, line, actualExpression, expectedExpression,
                   actual, static_cast<uint64_t>(actual), expected, static_cast<uint64_t>(expected));
        return false;
    }
    return true;
}
}
