// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Minimal self-registering test harness for libnls. Tests declared with
// NLS_TEST register themselves into the single TestRegistry; the runner executes
// them, tallies CHECK/REQUIRE results, and reports per-test and overall pass/fail.

#pragma once

#include "RegisteredTest.h"

#include <stdint.h>

#include <string>
#include <string_view>
#include <vector>

namespace nls::tests
{
class TestRegistry
{
public:
    static TestRegistry& Instance();

    // Adds a test. Returns 0 so it can seed a namespace-scope static initializer.
    int Register(std::string name, RegisteredTest::Function function);

    // Runs every test whose name contains filter (empty filter = all). Returns
    // true if every selected test passed.
    bool RunAll(std::string_view filter);

    // Prints the dotted name of every registered test, one per line.
    void ListTests() const;

    // Records a boolean check. Returns the condition so REQUIRE can branch on it.
    bool Check(bool condition, const char* expression, const char* file, int line);

    // Records an integer equality check, reporting both values on mismatch.
    bool CheckEqual(long long actual, long long expected,
                    const char* actualExpression, const char* expectedExpression,
                    const char* file, int line);

private:
    std::vector<RegisteredTest> _tests;
    uint32_t _currentFailures = 0;
    uint64_t _totalChecks = 0;
    uint64_t _failedChecks = 0;
};
}
