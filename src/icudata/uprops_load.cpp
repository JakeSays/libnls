// libnls — runtime loader for the Unicode character-property data (uprops.icu).
//
// uchar_props_data.h was changed from compiled-in static arrays to extern declarations; this
// file DEFINES those symbols and fills them at init from uprops.icu, so the data can be
// re-versioned without recompiling libnls. The hand-written uchar.cpp/uprops.cpp are unchanged.
//
// uprops.icu layout (genprops/corepropsbuilder.cpp), in uint32 units, located via indexes[16]:
//   [16        .. i0) PT  serialized propsTrie            (UTrie2, 16-bit values)
//   [i3        .. i4) AT  serialized propsVectorsTrie     (UTrie2, 16-bit values)
//   [i4        .. i6) PV  propsVectors[] (i5 columns)
//   [i6        .. i7) SCX scriptExtensions[] (uint16)
//   [i7        .. i9) blockTrie serialized UCPTrie
// (i0=PROPS32, i3=ADDITIONAL_TRIE, i4=ADDITIONAL_VECTORS, i5=VECTORS_COLUMNS,
//  i6=SCRIPT_EXTENSIONS, i7=BLOCK_TRIE, i9=DATA_TOP)

#include <unicode/utypes.h>
#include <unicode/udata.h>
#include <unicode/uversion.h>
#include <unicode/ucptrie.h>
#include "utrie2.h"
#include "uprops.h"

// --- the symbols uchar.cpp / uprops.cpp expect (extern in the replaced uchar_props_data.h) ---
UVersionInfo dataVersion = {0, 0, 0, 0};
UTrie2 propsTrie = {};
UTrie2 propsVectorsTrie = {};
UCPTrie block_trie = {};
const uint32_t *propsVectors = nullptr;
int32_t countPropsVectors = 0;
int32_t propsVectorsColumns = 0;
const uint16_t *scriptExtensions = nullptr;
const int32_t *indexes = nullptr;

namespace
{
    UDataMemory *gUPropsData = nullptr;
}

// Loads uprops.icu from the given directory and fills the data symbols. Returns U_ZERO_ERROR
// on success. Call once before any uchar/uprops API use.
extern "C" UErrorCode libnls_load_uprops(const char *dataDir)
{
    UErrorCode status = U_ZERO_ERROR;
    gUPropsData = udata_open(dataDir, "icu", "uprops", &status);
    if (U_FAILURE(status))
    {
        return status;
    }

    UDataInfo info;
    info.size = sizeof(UDataInfo);
    udata_getInfo(gUPropsData, &info);
    uprv_memcpy(dataVersion, info.dataVersion, sizeof(UVersionInfo));

    const uint32_t *base = static_cast<const uint32_t *>(udata_getMemory(gUPropsData));
    indexes = reinterpret_cast<const int32_t *>(base);

    int32_t i0 = indexes[UPROPS_PROPS32_INDEX];
    int32_t i3 = indexes[UPROPS_ADDITIONAL_TRIE_INDEX];
    int32_t i4 = indexes[UPROPS_ADDITIONAL_VECTORS_INDEX];
    int32_t i6 = indexes[UPROPS_SCRIPT_EXTENSIONS_INDEX];
    int32_t i7 = indexes[UPROPS_BLOCK_TRIE_INDEX];
    int32_t i9 = indexes[UPROPS_DATA_TOP_INDEX];

    propsVectorsColumns = indexes[UPROPS_ADDITIONAL_VECTORS_COLUMNS_INDEX];
    propsVectors = base + i4;
    countPropsVectors = i6 - i4;
    scriptExtensions = reinterpret_cast<const uint16_t *>(base + i6);

    // The serialized tries reference the loaded bytes in place; gUPropsData is kept open for
    // the process lifetime, so the copied struct's internal pointers stay valid.
    UTrie2 *pt = utrie2_openFromSerialized(
        UTRIE2_16_VALUE_BITS, base + 16, (i0 - 16) * 4, nullptr, &status);
    if (U_FAILURE(status))
    {
        return status;
    }
    propsTrie = *pt;

    UTrie2 *pvt = utrie2_openFromSerialized(
        UTRIE2_16_VALUE_BITS, base + i3, (i4 - i3) * 4, nullptr, &status);
    if (U_FAILURE(status))
    {
        return status;
    }
    propsVectorsTrie = *pvt;

    UCPTrie *bt = ucptrie_openFromBinary(
        UCPTRIE_TYPE_ANY, UCPTRIE_VALUE_BITS_ANY, base + i7, (i9 - i7) * 4, nullptr, &status);
    if (U_FAILURE(status))
    {
        return status;
    }
    block_trie = *bt;

    return status;
}
