// Copyright (c) Jake Helfert
// Licensed under the MIT License.
//
// libnls excises ICU's character-property subsystem (uprops/uchar/uscript_props/
// propname/...), the likely-subtags machinery, and the locale display-name
// tables (locdispnames), none of which the prebuilt collation runtime needs. A
// few retained core/service TUs still *reference* functions from those removed
// units, but only on script-name / locale-maximization / display-name paths
// reachable solely through public ICU APIs that libnls neither exposes nor
// calls (its path is ucol_open -> prebuilt coll/<locale>.res ->
// getSortKey/strcoll, which uses integer reorder codes and never resolves
// script names, maximizes locales, or formats display names).
//
// These definitions resolve those otherwise-dangling references so libnls.so is
// self-contained. They are unreachable; if ever called they degrade gracefully
// (script not found / locale not maximized / empty display name) rather than
// mis-sorting.

#include "unicode/utypes.h"
#include "unicode/uchar.h"
#include "unicode/locid.h"
#include "unicode/unistr.h"
#include "charstr.h"
#include "ulocimp.h"

U_CAPI int32_t U_EXPORT2
u_getPropertyValueEnum(UProperty /*property*/, const char* /*alias*/) {
    return -1;  // USCRIPT_INVALID_CODE / "value not found"
}

U_COMMON_API icu::CharString
ulocimp_addLikelySubtags(const char* /*localeID*/, UErrorCode& status) {
    if (U_SUCCESS(status)) {
        status = U_UNSUPPORTED_ERROR;
    }
    return icu::CharString();
}

U_NAMESPACE_BEGIN

UnicodeString&
Locale::getDisplayName(const Locale& /*displayLocale*/, UnicodeString& name) const {
    return name.remove();
}

U_NAMESPACE_END
