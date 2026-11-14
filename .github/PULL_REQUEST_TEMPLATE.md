## What and why

<!-- The problem, and the change. Reference the issue: "Closes #123". -->

## Checklist

- [ ] Automated tests cover the change and pass (`TIFFNET_SKIP_INTEROP=1 dotnet test Eleris.TiffNet.slnx`, or with the interoperability corpus when the change touches reading or writing).
- [ ] No build warning; every public member is documented.
- [ ] Documentation updated: `docs/`, `docs/standard-coverage.md`, `CHANGELOG.md` when the change is visible to users.
- [ ] Code that reads a file stays bounded on hostile input (limits of `TiffReadOptions`, `TiffException` / `NotSupportedException` only).
- [ ] A change to a hot path comes with before and after benchmark figures below.

## Measurements

<!-- Benchmark figures, or "not applicable". -->

## Not verified

<!-- Platforms, cases or configurations you could not test. -->
