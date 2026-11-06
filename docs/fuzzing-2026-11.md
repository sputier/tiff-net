# Fuzzing campaign, November 2026

A long campaign of the structure-aware fuzzer (`tools/Fuzz`) over every layer that parses untrusted bytes: the header and the
directories (`open`), the pixels of every image (`read`, which also opens), and the JPEG decoder (`jpeg`). It complements the
short campaign that the CI runs on every change of these layers (`.github/workflows/fuzz.yml`).

## Setup

| | |
| --- | --- |
| Code | branch `batch/47-51` (commit `b9f0fb8`), Release build |
| Machine | Windows 11, 8 cores, .NET 10.0.12; the three campaigns ran at once, one process each |
| Duration | 3 600 s per campaign |
| Corpus | `open`, `read`: the fixtures (domain, hostile, and the 6 000 files of the generated tifffile corpus), 6 110 files, id `F9BABFCC8A918D10`; `jpeg`: the 24 JPEG-in-TIFF fixtures, id `DD050711EDCA0970` |
| Failure criteria | any exception other than `TiffException` or `NotSupportedException`; an input running more than 2 s; an input allocating more than 256 MB; a process crash (the input being run is written to `current.txt` first) |

```text
dotnet run -c Release --project tools/Fuzz -- run --seconds 3600 --seed 5001 --mode mixed --target open --out long/open
dotnet run -c Release --project tools/Fuzz -- run --seconds 3600 --seed 5002 --mode mixed --target read --out long/read
dotnet run -c Release --project tools/Fuzz -- run --seconds 3600 --seed 5003 --mode havoc --target jpeg --out long/jpeg
```

## Results

**148 264 816 executions, no finding**: no crash, no input slower than 2 s, no input allocating more than 256 MB, no crash
of a process.

| Target | Seed | Mode | Inputs | Read | Refused (`TiffException`) | Refused by a limit | Not supported |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| `open` | 5001 | mixed | 106 496 154 | 81 143 620 | 23 037 464 | 2 281 398 (`MaxTagsPerDirectory`) | 33 672 |
| `read` | 5002 | mixed | 15 617 365 | 7 876 558 | 7 367 423 | 333 811 (`MaxTagsPerDirectory`), 39 312 (`MaxPixelBytes`) | 261 |
| `jpeg` | 5003 | havoc | 26 151 297 | 16 596 386 | 9 323 010 | 2 643 (`MaxPixelBytes`) | 229 258 |

- Every rejected input was rejected through the documented exceptions, and those that asked for too much memory through the
  limits of `TiffReadOptions` (`MaxPixelBytes`, `MaxTagsPerDirectory`) before any allocation.
- Half to three quarters of the inputs of each target were still read: the mutations keep the structure valid often enough to reach the
  decoders, which is what a structure-aware fuzzer is for.
- The shorter campaigns run during the hardening work had their findings fixed, each with a hostile fixture that the tests
  keep; this campaign found none left. See [reliability and limits](reliability.md) for what the limits guarantee.

## Reproducing

An input is a pure function of the seed, the iteration and the corpus: `reproduce --seed <seed> --iteration <n>` writes it
again from a corpus of the same id. The CI campaign uses the number of the run as its seed, so that every run explores other
inputs.
