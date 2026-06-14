# ICU vendoring manifest (/p/ese/libnls/repo)

Pinned source: /p/ese/icu (ICU4C 78.3, Unicode License V3).

## common/  — vendor WHOLESALE *to bootstrap*, then trim (cleanup round)
Initially vendor all of `source/common/` (202 .cpp + headers, incl. `unicode/`) — do NOT
hand-curate up front. Rationale: you can't safely know what's truly dead until it builds,
links, and passes tests (ICU has static-init/cleanup registration and header coupling that
make some TUs reachable non-obviously). The UCONFIG_NO_* flags already compile the unwanted
clusters (break-iter, formatting, translit, regex, idna, mf) to near-empty TUs.

**Cleanup round (after build + integration + green tests):** trim common/ to the
actually-used set, driven by the toolchain, not by guessing:
- Build with `-Wl,--gc-sections -Wl,--print-gc-sections` (and/or a linker `-Map`) to see which
  object files contribute zero retained sections to the final `.so` — those TUs are dead.
- Drop a candidate .cpp → rebuild → re-run conformance + ESE tests. Keep only drops that stay
  green. Iterate.
- Record the result as a **drop-list / script** so a future ICU re-pin re-applies the trim
  mechanically (re-pin = re-copy all of common, then run the trim script). Re-pinning stays
  cheap despite the curated tree.

## i18n/  — collation subset ONLY (~32 .cpp + headers)
Compile these (29 from the name match):
  bocsu, collation, collationbuilder, collationcompare, collationdata,
  collationdatabuilder, collationdatareader, collationdatawriter, collationfastlatin,
  collationfastlatinbuilder, collationfcd, collationiterator, collationkeys, collationroot,
  collationrootelements, collationruleparser, collationsets, collationsettings,
  collationtailoring, collationweights, rulebasedcollator, sortkey, ucol, ucoleitr,
  ucol_res, ucol_sit, uitercollationiterator, utf16collationiterator, utf8collationiterator
Plus the public-class impls they require:
  coll.cpp (Collator base), coleitr.cpp (CollationElementIterator)
Plus headers pulled by the above (copy, don't necessarily compile):
  unicode/coll.h, unicode/coleitr.h, unicode/tblcoll.h, collunsafe.h, ucln_in.h, usrchimp.h

DROP the rest of i18n/ (~222 .cpp: number/date/calendar/translit/regex/etc. formatting).

## Spike watch-items
- `usrchimp.h` is included but `usearch.cpp` (string search) is likely NOT needed — try
  without it; add only if the link fails.
- `ucol_res.cpp` / `collationdatareader.cpp` are the resource/data-load path; they stay for
  now but get replaced by embedded-data access (the "strip udata" step). Verify at the
  shim-wiring stage which of these survive once data is embedded.
- UCONFIG_NO_SERVICE decides whether `Collator::getInstance`'s cache path is compiled; if set,
  drive the collator/builder directly.

## Build defines (CMake target_compile_definitions)
U_COMMON_IMPLEMENTATION, U_I18N_IMPLEMENTATION, U_STATIC_IMPLEMENTATION
UCONFIG set: NO_BREAK_ITERATION, NO_FILTERED_BREAK_ITERATION, NO_FORMATTING,
NO_TRANSLITERATION, NO_REGULAR_EXPRESSIONS, NO_IDNA, NO_MF
NOT set: NO_NORMALIZATION, NO_CONVERSION, NO_COLLATION
Evaluate: NO_SERVICE
