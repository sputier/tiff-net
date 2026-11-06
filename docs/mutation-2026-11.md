# Mutation testing, November 2026

[Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) 5.0 on the code that parses untrusted bytes: the
structure (`Internal/Structure`) and the codecs (`Internal/Codecs`). A mutant is the code with one change (a comparison
reversed, a statement removed, a constant changed); a mutant the tests do not notice "survives", and shows an edge the tests
do not check.

```text
dotnet tool restore
dotnet stryker            # stryker-config.json: the files mutated, the tests run (all but the slow ones)
```

## Results

| File | Killed | Survived | Not covered | Score |
| --- | ---: | ---: | ---: | ---: |
| `Structure/ImageDescriptor.cs` | 333 | 45 | 27 | 82 % |
| `Structure/TiffStructure.cs` | 191 | 32 | 6 | 83 % |
| `Structure/MetadataReader.cs` | 26 | 1 | 0 | 96 % |
| `Codecs/Lzw.cs` | 103 | 25 | 0 | 80 % |
| `Codecs/Encoders.cs` | 122 | 15 | 5 | 86 % |
| `Codecs/PackBits.cs` | 37 | 6 | 0 | 86 % |
| `Codecs/Predictors.cs` | 76 | 2 | 2 | 95 % |
| `Codecs/PredictorEncoder.cs` | 31 | 2 | 0 | 94 % |
| `Codecs/Deflate.cs` | 7 | 1 | 0 | 88 % |
| **Total** | **926** | **129** | **40** | **84.6 %** |

The first run gave 77.6 %; 1 055 mutants were tested in the last one (20 minutes on 8 cores, 3 test sessions).

## What the surviving mutants showed, and was fixed

- **`Lzw.Decode` and `ImageDescriptor.Read` could not be mutated at all**: their mutants did not compile (locals assigned on
  every branch, a `while (true)` loop), and Stryker dropped the whole methods. The locals are initialised and the loop has no
  condition; both are now mutated.
- **The bounds of the encoders**: a smaller `MaxEncoded` went unnoticed, because pooled buffers are rounded up. Each encoder
  now writes incompressible data into a buffer of exactly its bound. Stryker still reports these mutants as surviving (its
  coverage per test does not see the calls of these static bounds); applied by hand, a smaller bound fails four tests.
- **The LZW encoder past a full table**: no test filled the 4 093 codes; a megabyte of noise now does, and is decoded back.
  The end of the stream (libtiff's post-encode, where the last string can fill the table or widen the codes) is tested with
  every length of 0 to 9 000 bytes, which ends with the table at every size; that test kills the mutants left there by the
  last run (checked by hand).
- **The overflow reported by the decoders** (LZW, PackBits): tested with destinations that cut a string or a run.
- **Loops that go through references** (the predictors): a loop running one sample too far went unnoticed. They now run on
  rows followed by sentinels that must not change.
- **Structural refusals**: removing a check went unnoticed when a later check failed too, since the tests asserted the type
  of the exception only. Each refusal (header, tiles, required tags, colour map, strict ordering, invalid codes) is now
  checked by its message, and the defaults of invalid codes without `Strict`.

## What survives, and why it is left

- **Equivalent mutants**, which change nothing a caller can see:
  - `>>` against `>>>` on unsigned values;
  - the thresholds of the fast paths of LZW (`size > 16`): a slower copy gives the same bytes;
  - the probing and the multiplier of the hash table of the LZW encoder: a worse hash gives the same codes;
  - bounds that only grow.
- **Messages** (31 mutants): the text of an exception emptied. The refusals that matter are checked by their message; the
  others only by their type.
- **Redundant checks** (most of the rest): a guard removed whose condition a later check refuses too. An example is the
  length of a BigTIFF header, then the read of its fields. Such a guard gives a clearer message and is kept, but its removal
  does not change the outcome.
- **Not covered** (40): branches only the slow tests reach (the tifffile corpus, the fuzzing harness, the measurements),
  which the configuration leaves out to keep a run under half an hour.

No surviving mutant showed a defect of the library: each gap was an edge the code handled and the tests did not check.
