// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Test declaration and assertion macros. A test body is introduced with
// NLS_TEST(Group, Name); inside it, CHECK/CHECK_FALSE/CHECK_EQ record results and
// continue, while REQUIRE aborts the test on failure.

#pragma once

#include "TestRegistry.h"
#include "TestAbort.h"

#define NLS_TEST(group, name)                                                      \
    static void group##_##name();                                                  \
    static const int group##_##name##_registered =                                 \
        nls::tests::TestRegistry::Instance().Register(#group "." #name, &group##_##name); \
    static void group##_##name()

#define CHECK(condition)                                                           \
    nls::tests::TestRegistry::Instance().Check((condition) ? true : false, #condition, __FILE__, __LINE__)

#define CHECK_FALSE(condition)                                                     \
    nls::tests::TestRegistry::Instance().Check((condition) ? false : true, "!(" #condition ")", __FILE__, __LINE__)

#define CHECK_EQ(actual, expected)                                                 \
    nls::tests::TestRegistry::Instance().CheckEqual(                               \
        static_cast<long long>(actual), static_cast<long long>(expected),          \
        #actual, #expected, __FILE__, __LINE__)

#define REQUIRE(condition)                                                         \
    do                                                                             \
    {                                                                              \
        if (!CHECK(condition))                                                     \
        {                                                                          \
            throw nls::tests::TestAbort();                                         \
        }                                                                          \
    } while (false)
