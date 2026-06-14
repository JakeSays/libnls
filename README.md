# libnls — Win32 NLS surface for the ESE Linux port (ICU edition)

A C++ shared library that implements the slice of the Win32 National Language Support API the
ESE Linux port consumes, **built on a vendored, pinned snapshot of ICU4C 78.3** — no
dependency on any installed ICU.

This is the greenfield replacement for the Wine-derived libnls. The Wine version remains in
the engine tree at `third_party/libnls` as a working fallback and the export-surface
reference; it is not touched. Design: `/p/ese/localization-layer.md`. Vendoring scope:
`VENDORING.md`.

## Why ICU, vendored and pinned

- **Permissive license.** ICU4C is under the Unicode License V3 — vendoring its source is
  clean (unlike Wine's LGPL).
- **No installed-ICU dependency.** ICU's data-loading (`udata`/resource-bundle) layer is
  stripped; data is regenerated from pinned public text and embedded. Nothing links the host's
  `libicu`.
- **Deterministic, owned collation version.** Pinning the ICU snapshot and the CLDR/UCA data
  freezes the sort-key version surfaced through `GetNLSVersionEx`, so it changes only when we
  re-pin (with a reindex) — never because a host updated its `libicu`.

## Layout

```
include/winnls.h   Public header — our own clean Win32 declarations (no Wine provenance)
src/               Win32 shims over ICU (LCMapStringEx->ucol, GetDateFormatW->udat, etc.)
icu/               Vendored pinned ICU subset: common/ (wholesale) + i18n/ collation only
data/              Regenerated embedded data (collation/normalization), built from pinned text
tools/             CMake data-regen target (genrb / gennorm2 / root build)
tests/             Conformance + smoke tests
VENDORING.md       Exactly which ICU files are vendored and the UCONFIG build flags
```

## Status

Scaffold only — no code yet. Next: vendor the ICU subset, write `CMakeLists.txt`, and the
compile spike (validate the `UCONFIG_NO_*` choices). See the work queue in
`/p/ese/localization-layer-handoff.md`.
