# Contributing to Eleris.TiffNet

Thank you for your interest. Bug reports, small fixes, documentation, test files and benchmarks are all welcome. For a new
feature or a large change, please open an issue first so that the design can be discussed before you write code.

By taking part you agree to follow the [code of conduct](CODE_OF_CONDUCT.md). To report a security problem, do not open an
issue: follow [SECURITY.md](SECURITY.md).

## Reporting a bug

Open an issue with the *Bug report* form. The most useful report has:

- the version of the library, the .NET version and the operating system;
- the smallest code that shows the problem;
- the file that triggers it, or a way to produce it (do not attach a file you are not allowed to share; a crop or a file
  written with `tools/make_fixtures.py` often does the same).

A file that makes the library throw anything other than `TiffException` or `NotSupportedException`, allocate far more than the
limits of `TiffReadOptions`, or run for a long time compared with its size is a security problem: see
[SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET SDK pinned by [`global.json`](global.json). Python 3.12 or later is needed only for the interoperability
corpus and the documentation.

```shell
TIFFNET_SKIP_INTEROP=1 dotnet test Eleris.TiffNet.slnx    # every test but the interoperability gate
```

The interoperability tests compare the library with [tifffile](https://github.com/cgohlke/tifffile). To run them:

```shell
pip install -r tools/requirements-interop.txt
python -I tools/make_fixtures.py                            # generates the corpus (about 6 000 files)
dotnet test Eleris.TiffNet.slnx
```

The [README](README.md) lists the other commands (documentation, fuzzing, benchmarks) and the environment variables that
control the slow or machine-dependent tests. A test that cannot run on a machine is reported as skipped, never as passed.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Make the change, with tests. A change is finished when automated tests that exercise it pass; a bug fix starts with a test
   that fails without the fix.
3. Make sure that the build has no warning (analyzers are on and warnings are errors) and that every public member has its
   XML documentation (the build fails otherwise).
4. Update the documentation the change touches: the guide and the pages under `docs/`, the coverage table
   ([`docs/standard-coverage.md`](docs/standard-coverage.md)), the reliability and performance pages, and the
   [changelog](CHANGELOG.md). Run `dotnet run -c Release --project tools/DocGen -- --check` and
   `python tools/check_docs.py` when you changed a page or a public signature.
5. If the change is on a hot path (decoders, encoders, pixel pipeline), run the benchmarks of `benchmarks/` before and after
   and give the figures in the pull request. A performance change is kept only if the gain is measured.
6. Open a pull request against `main` and fill in the template.

### Conventions

- The format is set by [`.editorconfig`](.editorconfig); files use LF line endings.
- The library targets net10.0 only, has no dependency, and stays compatible with trimming and NativeAOT.
- Code that reads a file must stay bounded on hostile input: every allocation and loop is limited by `TiffReadOptions` or by
  the size of the file, and failures are `TiffException` or `NotSupportedException`. Add a fixture or a fuzzing case when
  you fix a robustness problem.
- Commit messages: a short imperative summary line (`Read planar files by bands`), then the reason and the measurements if
  any. Reference the issue (`Closes #123`).

## Licence

By contributing you agree that your contribution is licensed under the [MIT licence](LICENSE) of the project.
