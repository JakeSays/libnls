// libnls — single initialization entry point.
//
// ESE's platform-init calls InitializeNls() (it already handles call ordering), so the Unicode
// data loaders need no init-once / thread-safety guards: the data is loaded once, here, before
// any NLS API is used. Each loader fills the extern symbols that the (unchanged) hand-written
// ICU code references — see src/icudata/uprops_load.cpp etc.

#include <unicode/utypes.h>

extern "C" UErrorCode libnls_load_uprops(const char *dataDir);
// TODO(P2): libnls_load_ucase / libnls_load_ubidi / libnls_load_pnames / libnls_load_nfc.

// Loads all vendored-ICU runtime data from dataDir (the .icu/.nrm files regenerated from our
// pinned UCD/CLDR). Exported from libnls; called by ESE's platform initialization.
extern "C" UErrorCode InitializeNls(const char *dataDir)
{
    UErrorCode status = libnls_load_uprops(dataDir);
    if (U_FAILURE(status))
    {
        return status;
    }
    // P2: chain the remaining loaders here as they land.
    return status;
}
