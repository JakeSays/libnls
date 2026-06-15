// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// UTF-8 / UTF-16 transcoding for the codepage conversions. Works in char16_t
// (UTF-16 code units); callers reinterpret_cast between WCHAR and char16_t,
// which are layout-compatible under -fshort-wchar.

#pragma once

#include <stdint.h>

class Utf
{
public:
    // Decodes one UTF-8 scalar from [p, end), returning U+FFFD on malformed
    // input and advancing p past the bytes consumed.
    static uint32_t DecodeUtf8(const unsigned char*& p, const unsigned char* end)
    {
        if (p >= end)
        {
            return 0;
        }
        const unsigned char b0 = *p++;
        uint32_t scalar;
        int extra;
        if (b0 < 0x80)
        {
            return b0;
        }
        else if (b0 < 0xC0)
        {
            return 0xFFFD;
        }
        else if (b0 < 0xE0)
        {
            scalar = b0 & 0x1F;
            extra = 1;
        }
        else if (b0 < 0xF0)
        {
            scalar = b0 & 0x0F;
            extra = 2;
        }
        else if (b0 < 0xF8)
        {
            scalar = b0 & 0x07;
            extra = 3;
        }
        else
        {
            return 0xFFFD;
        }
        for (int i = 0; i < extra; ++i)
        {
            if (p >= end || (*p & 0xC0) != 0x80)
            {
                return 0xFFFD;
            }
            scalar = (scalar << 6) | (*p++ & 0x3F);
        }
        return scalar;
    }

    // Encodes a scalar into UTF-16, returning the number of code units (1 or 2).
    // Writes only when out is non-null.
    static int EncodeUtf16(uint32_t scalar, char16_t* out)
    {
        if (scalar <= 0xFFFF)
        {
            if (out != nullptr)
            {
                out[0] = static_cast<char16_t>(scalar);
            }
            return 1;
        }
        if (scalar > 0x10FFFF)
        {
            if (out != nullptr)
            {
                out[0] = static_cast<char16_t>(0xFFFD);
            }
            return 1;
        }
        scalar -= 0x10000;
        if (out != nullptr)
        {
            out[0] = static_cast<char16_t>(0xD800 | (scalar >> 10));
            out[1] = static_cast<char16_t>(0xDC00 | (scalar & 0x3FF));
        }
        return 2;
    }

    // Decodes one UTF-16 scalar from [p, end), advancing p.
    static uint32_t DecodeUtf16(const char16_t*& p, const char16_t* end)
    {
        if (p >= end)
        {
            return 0;
        }
        const uint32_t hi = *p++;
        if (hi >= 0xD800 && hi <= 0xDBFF && p < end)
        {
            const uint32_t lo = *p;
            if (lo >= 0xDC00 && lo <= 0xDFFF)
            {
                ++p;
                return 0x10000 + ((hi - 0xD800) << 10) + (lo - 0xDC00);
            }
        }
        return hi;
    }

    // Encodes a scalar into UTF-8, returning the number of bytes (1..4). Writes
    // only when out is non-null.
    static int EncodeUtf8(uint32_t scalar, char* out)
    {
        if (scalar < 0x80)
        {
            if (out != nullptr)
            {
                out[0] = static_cast<char>(scalar);
            }
            return 1;
        }
        if (scalar < 0x800)
        {
            if (out != nullptr)
            {
                out[0] = static_cast<char>(0xC0 | (scalar >> 6));
                out[1] = static_cast<char>(0x80 | (scalar & 0x3F));
            }
            return 2;
        }
        if (scalar < 0x10000)
        {
            if (out != nullptr)
            {
                out[0] = static_cast<char>(0xE0 | (scalar >> 12));
                out[1] = static_cast<char>(0x80 | ((scalar >> 6) & 0x3F));
                out[2] = static_cast<char>(0x80 | (scalar & 0x3F));
            }
            return 3;
        }
        if (out != nullptr)
        {
            out[0] = static_cast<char>(0xF0 | (scalar >> 18));
            out[1] = static_cast<char>(0x80 | ((scalar >> 12) & 0x3F));
            out[2] = static_cast<char>(0x80 | ((scalar >> 6) & 0x3F));
            out[3] = static_cast<char>(0x80 | (scalar & 0x3F));
        }
        return 4;
    }
};
