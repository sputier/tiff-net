# Publishing the NuGet package

The package is `Eleris.TiffNet`. Its version is `<Version>` in `src/Eleris.TiffNet/Eleris.TiffNet.csproj`, the only place to
change it (`0.1.0`, or `0.2.0-preview.1` for a prerelease: nuget.org lists a version with a suffix as a prerelease).

## One-time setup

1. On [nuget.org](https://www.nuget.org), create an API key (Account > API Keys) limited to **Push new packages and package
   versions**, with the glob pattern `Eleris.TiffNet*`.
2. On GitHub, in *Settings > Environments*, create the environment `nuget`, add the secret `NUGET_API_KEY` to it, and, if every
   publication must be approved by hand, add yourself as a required reviewer.

## Releasing a version

1. Change `<Version>` in `src/Eleris.TiffNet/Eleris.TiffNet.csproj`, move the entries of `CHANGELOG.md` from "Unreleased" to
   the new version, and commit.
2. Check the package locally:

   ```shell
   dotnet pack src/Eleris.TiffNet -c Release -o artifacts
   python tools/verify_package.py artifacts
   ```

   The script checks the content and the metadata of the package, then builds a new project from the package alone.
3. Tag and push. The tag is `v` followed by the version:

   ```shell
   git tag v0.1.0
   git push origin v0.1.0
   ```

The `Release` workflow then checks that the tag matches the version, builds, runs the tests, packs, runs
`tools/verify_package.py`, waits for the approval of the `nuget` environment if there is one, pushes the `.nupkg` and its
symbols (`.snupkg`) to nuget.org and creates the GitHub release.

A published version cannot be replaced. It can only be *unlisted* on nuget.org, so a mistake costs a new version number.

## After the first publication

To protect later versions against an accidental API break, set `<PackageValidationBaselineVersion>` in the project to the last
published version: package validation then compares the new package with it.
