# Data tools

`nls-data-gen` is the single native-AOT C# tool that generates every NLS data file libnls
embeds, directly from pinned CLDR + UCD text. It replaces ICU's `genrb`, `gennorm2`, `genuca`,
and `genprops`, plus the standalone CLDR collation extractor — all folded into one program.

## What it produces

Written into the `--out` directory:

- `ucadata.icu` — collation root (radical-stroke / unihan han order)
- `coll/<locale>.res` — collation tailorings, one per CLDR collation locale
- `nfc.nrm` — NFC / FCD normalization data
- `ucase.icu` — case mapping
- `cp1252.nlsdata`, `lcid-locales.nlsdata` — the Win32 ANSI codepage and LCID tables

The collation output is byte-identical to ICU's for every comparable locale. `zh` is the one
deliberate exception: libnls ships CLDR's **comprehensive** CJK coverage (every extension kanji
gets an explicit pinyin/stroke position), whereas ICU's reference is built from the trimmed
**modern**-coverage production data.

## bin/nls-data-gen (prebuilt, committed)

The native-AOT binary is committed via Git LFS so regenerating data needs neither the .NET SDK
nor a built ICU — it is a self-contained, single-file, trimmed linux-x64 executable. It depends
only on glibc.

## Rebuilding

The `NlsDataGenerator/.run/AOT.run.xml` Rider config publishes the AOT binary straight into
`bin/`. Equivalent from the command line:

    dotnet publish NlsDataGenerator -c Release -r linux-x64 -o bin

(`PublishAot`, single-file, and trim settings live in `NlsDataGenerator.csproj`.)

## Usage

    nls-data-gen --cldr <cldr-dir> --ucd <ucd-dir> --out <out-dir> --cldr-version <ver> [--locales de,ja,...]

`--cldr` points at the directory containing CLDR's `common/` (it reads `common/collation/*.xml`,
`common/uca/FractionalUCA.txt`, and `common/uca/UCA_Rules.txt`); `--ucd` at the unpacked UCD; the
vendored `icu/` tree is read for `uscript.h`. With no `--locales`, every collation locale is
generated. Conformance is checked by the in-tool self-tests (`--test-collset` diffs each produced
`%%CollationBin` against the icudt reference, `--test-normalizer`, `--dump-coll`, `--decode-trie`, …).
