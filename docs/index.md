# Eleris.TiffNet

A pure managed .NET library to read and write TIFF images, made for astronomy and scientific data: 8 to 64-bit integer and
floating point samples, grey or RGB, the metadata astronomy software stores in TIFF (Astro-TIFF FITS headers, ICC profiles),
multi-core encoding and decoding, and bounded resources on hostile files. It targets net10.0 on Windows, Linux and .NET MAUI,
with no dependency. The sister library of [Eleris.FitsNet](https://github.com/sputier/fits-net).

## Status

The library is being written. Its design is fixed:

| Page | Content |
| --- | --- |
| [Public API](design/api.md) | the types and members of version 1.0 |
| [TIFF coverage](design/coverage.md) | what is read and written, what is refused and why |
| [Architecture](design/architecture.md) | layers, resource limits, parallelism, tests, performance targets, decisions |
| [Backlog](design/backlog.md) | how the work is cut; progress is followed in the GitHub issues |
| [Review of the first implementation](review-2026-11-05.md) | the findings that motivate the design (in French) |

## Guide

| Page | Content |
| --- | --- |
| [Reliability and limits](reliability.md) | errors, limits for untrusted files |
| [API reference](api/index.md) | every public type and member, generated from the code |
