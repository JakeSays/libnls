// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Reads little-endian integers from a byte buffer (the on-disk order of the
// generated NLS data items).

#pragma once

#include <stdint.h>

class LittleEndian
{
public:
    static uint16_t ReadUInt16(const uint8_t* p)
    {
        return static_cast<uint16_t>(p[0] | (p[1] << 8));
    }

    static uint32_t ReadUInt32(const uint8_t* p)
    {
        return static_cast<uint32_t>(p[0])
            | (static_cast<uint32_t>(p[1]) << 8)
            | (static_cast<uint32_t>(p[2]) << 16)
            | (static_cast<uint32_t>(p[3]) << 24);
    }
};
