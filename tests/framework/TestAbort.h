// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#pragma once

namespace nls::tests
{
// Thrown by REQUIRE when a fatal check fails, to abort the current test. The
// runner catches it so the remaining tests still run.
class TestAbort
{
};
}
