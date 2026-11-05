# Eleris.TiffNet

A pure managed .NET library to read and write **TIFF** images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. The sister library of
[Eleris.FitsNet](https://github.com/sputier/fits-net).

**Status: design.** The library is being written from scratch. The requirements come from the
[review of the first implementation](docs/review-2026-11-05.md) (in French).

Targets: net10.0 on Windows, Linux and .NET MAUI (Android, iOS), trimming and NativeAOT compatible, no dependency.

## Build, test, documentation

```shell
dotnet test Eleris.TiffNet.slnx
dotnet run -c Release --project tools/DocGen              # regenerate the API pages and the samples in the pages
dotnet run -c Release --project tools/DocGen -- --check   # fail if they are not up to date (CI)
pip install -r docs/requirements.txt
python tools/check_docs.py                                # format, links and anchors of the Markdown
mkdocs build --strict                                     # the site, in site/
```

The fixtures of the tests are described in [tests/Fixtures/README.md](tests/Fixtures/README.md). The fuzzer mutates them
structurally and saves every input that crashes, hangs or allocates too much, with the seed and iteration that reproduce it:

```shell
dotnet run -c Release --project tools/Fuzz -- run --seconds 600 --seed 1       # a campaign (see tools/Fuzz/Program.cs)
dotnet run -c Release --project tools/Fuzz -- reproduce --seed 1 --iteration 1234
```
