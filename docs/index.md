# Eleris.TiffNet

A pure managed .NET library to read and write TIFF images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. It targets net10.0 on Windows, Linux and .NET MAUI,
with no dependency. The sister library of [Eleris.FitsNet](https://github.com/sputier/fits-net).

## Design

How the library was designed:

| Page | Content |
| --- | --- |
| [Public API](design/api.md) | the types and members of the library |
| [Architecture](design/architecture.md) | layers, resource limits, parallelism, tests, performance targets, decisions |
| [Backlog](design/backlog.md) | how the work is cut; progress is followed in the GitHub issues |
| [Fuzzing campaign](fuzzing-2026-11.md) | 148 million inputs over every reader, no finding |
| [Mutation testing](mutation-2026-11.md) | Stryker.NET on the structure and the codecs: 84.6 %, the gaps found and fixed |

## Guide

| Page | Content |
| --- | --- |
| [Getting started](getting-started.md) | install, a first image |
| [Files and pages](files.md) | opening, pages, sub-images, images the library does not handle |
| [Reading pixels](pixels.md) | types, conversions, normalisation, regions, channels, planar order, orientation |
| [Metadata and Astro-TIFF](metadata.md) | tags, the FITS header, what is kept on saving |
| [Writing](writing.md) | creating images, saving, options |
| [Large images](large-images.md) | regions, TiffImageWriter, BigTIFF |
| [Threads and memory](threads.md) | parallelism, memory, thread safety |
| [Reliability and limits](reliability.md) | errors, limits for untrusted files, bounds of time and memory |
| [Performance](performance.md) | throughput, memory, tuning |
| [Platforms](platforms.md) | Windows, Linux, NativeAOT, .NET MAUI on Android |
| [Coverage of the standard](standard-coverage.md) | what is read and written, what is refused, how it is tested |
| [API reference](api/index.md) | every public type and member, generated from the code |
