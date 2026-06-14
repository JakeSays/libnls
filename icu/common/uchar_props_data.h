// libnls: this generated header normally defines the Unicode character-property data as
// static const compiled-in arrays. We replace it with extern declarations so the data is
// LOADED at runtime from uprops.icu (see src/icudata/uprops_load.cpp) and can be re-versioned
// without recompiling libnls. The hand-written uchar.cpp / uprops.cpp are unchanged — they
// reference these symbols exactly as before; only the storage moves from compiled-in to loaded.
//
// Original generated header (the compiled-in arrays) is preserved in libnls/temp for reference;
// the data now comes from genprops's uprops.icu, regenerated from our pinned UCD.
//
// file name: uchar_props_data.h
// machine-generated original by: icu/tools/unicode/c/genprops/corepropsbuilder.cpp

#ifdef INCLUDED_FROM_UCHAR_C

extern UVersionInfo dataVersion;

// Property tries/structs — filled at init by the loader (non-const so the loader can fill them).
extern UTrie2 propsTrie;
extern UTrie2 propsVectorsTrie;
extern UCPTrie block_trie;

// Flat arrays become pointers into the loaded data.
extern const uint32_t *propsVectors;
extern int32_t countPropsVectors;
extern int32_t propsVectorsColumns;
extern const uint16_t *scriptExtensions;
extern const int32_t *indexes;

#endif  // INCLUDED_FROM_UCHAR_C
