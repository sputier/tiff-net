# Eleris.TiffNet

A pure managed .NET library to read and write **TIFF** images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. The sister library of
[Eleris.FitsNet](https://github.com/sputier/fits-net).

**Status: design.** The library is being written from scratch. The requirements come from the
[review of the first implementation](docs/review-2026-11-05.md) (in French).

Targets: net10.0 on Windows, Linux and .NET MAUI (Android, iOS), trimming and NativeAOT compatible, no dependency.
