// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// Shared udata_openChoice acceptance for libnls's own data items.

#pragma once

#include <unicode/udata.h>
#include <unicode/utypes.h>

class IcuDataItem
{
public:
    // udata_openChoice isAcceptable callback: accepts an item whose 4-char
    // dataFormat equals the expected tag passed as the context pointer.
    static UBool AcceptDataFormat(void* context, const char* type, const char* name, const UDataInfo* info);
};
