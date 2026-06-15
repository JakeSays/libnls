// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Small helpers for the WCHAR strings that cross the Win32 boundary.

#pragma once

#include <optional>
#include <string>

class WideString
{
public:
    // The ASCII bytes of a null-terminated wide string, or nullopt if any unit
    // is non-ASCII. Locale names (the only wide strings libnls converts this
    // way) are always ASCII BCP-47 tags.
    static std::optional<std::string> ToAscii(const wchar_t* text)
    {
        std::string result;
        for (; *text != 0; ++text)
        {
            const uint16_t unit = static_cast<uint16_t>(*text);
            if (unit > 0x7F)
            {
                return std::nullopt;
            }
            result.push_back(static_cast<char>(unit));
        }
        return result;
    }

    // The number of wide units before the null terminator.
    static int Length(const wchar_t* text)
    {
        int length = 0;
        while (text[length] != 0)
        {
            ++length;
        }
        return length;
    }
};
