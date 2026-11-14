# Eleris.TiffNet

A pure managed .NET library to read and write **TIFF** images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. The sister library of
[Eleris.FitsNet](https://github.com/sputier/fits-net).

**Status: 0.1.0.** Written from scratch after an audit of a first implementation. See the [changelog](CHANGELOG.md), the
[guide](docs/getting-started.md) and the [coverage of the standard](docs/standard-coverage.md).

Targets: net10.0 on Windows, Linux, NativeAOT and .NET MAUI (checked on Android), trimming compatible, no dependency.

## Build, test, documentation

On a fresh clone, the tests need the tifffile corpus, which is generated with Python; without it, set `TIFFNET_SKIP_INTEROP=1`
to run every other test:

```shell
TIFFNET_SKIP_INTEROP=1 dotnet test Eleris.TiffNet.slnx   # every test but the interop gate
pip install -r tools/requirements-interop.txt               # Python 3.12: tifffile, imagecodecs, Pillow
python -I tools/make_fixtures.py                            # generate the corpus, then:
dotnet test Eleris.TiffNet.slnx
dotnet test tests/Eleris.TiffNet.Tests --filter "Category!=Slow"   # without the fuzzing campaigns of the suite
dotnet run -c Release --project tools/DocGen              # regenerate the API pages and the samples in the pages
dotnet run -c Release --project tools/DocGen -- --check   # fail if they are not up to date (CI)
pip install -r docs/requirements.txt
python tools/check_docs.py                                # format, links and anchors of the Markdown
mkdocs build --strict                                     # the site, in site/
```

Tests that need what a machine may not have are reported as skipped, not passed: the interop corpus (category `Interop`;
`TIFFNET_SKIP_INTEROP=1`), the 4.4 GB file (`Huge`; `TIFFNET_HUGE=1`), the written corpus (`TIFFNET_WRITTEN_DIR`), AVX2, Unix
permissions. `TIFFNET_TIME_FACTOR` scales the time bounds of the tests of hostile files on a slow machine.

The fixtures of the tests are described in [tests/Fixtures/README.md](tests/Fixtures/README.md). The fuzzer mutates them
structurally and saves every input that crashes, hangs or allocates too much, with the seed and iteration that reproduce it:

```shell
dotnet run -c Release --project tools/Fuzz -- run --seconds 600 --seed 1       # a campaign (see tools/Fuzz/Program.cs)
dotnet run -c Release --project tools/Fuzz -- reproduce --seed 1 --iteration 1234
```

## Contributing, security, licence

Bug reports and pull requests are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md) and the
[code of conduct](CODE_OF_CONDUCT.md). To report a vulnerability (a file that crashes the library, exhausts its memory or blocks
it), follow [SECURITY.md](SECURITY.md) and do not open a public issue. Eleris.TiffNet is released under the
[MIT licence](LICENSE).
