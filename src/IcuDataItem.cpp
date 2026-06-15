// Copyright (c) Jake Helfert
// Licensed under the MIT License.

#include "IcuDataItem.h"

#include <unicode/udata.h>

UBool IcuDataItem::AcceptDataFormat(void* context, const char* type, const char* name, const UDataInfo* info)
{
    (void)type;
    (void)name;
    const char* tag = static_cast<const char*>(context);
    return info->dataFormat[0] == tag[0]
        && info->dataFormat[1] == tag[1]
        && info->dataFormat[2] == tag[2]
        && info->dataFormat[3] == tag[3];
}
