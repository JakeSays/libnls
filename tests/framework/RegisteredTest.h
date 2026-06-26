// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#pragma once

#include <string>
#include <utility>

namespace nls::tests
{
// One registered test: its dotted name (e.g. "CodePage.Cp1252RoundTrip") and the
// function that runs it.
class RegisteredTest
{
public:
    using Function = void (*)();

    RegisteredTest(std::string name, Function function)
        : _name(std::move(name)), _function(function)
    {
    }

    const std::string& Name() const
    {
        return _name;
    }

    void Run() const
    {
        _function();
    }

private:
    std::string _name;
    Function _function;
};
}
