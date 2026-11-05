# Security

Eleris.TiffNet reads files that come from anywhere: a damaged or hostile TIFF file must fail with a `TiffException` or a
`NotSupportedException`, within the limits of `TiffReadOptions`, and never crash the application, exhaust its memory or block it.

## Reporting a vulnerability

Report a problem privately, through GitHub: *Security > Report a vulnerability* on the repository
(<https://github.com/sputier/tiff-net/security/advisories/new>). Please include the file that shows it, or a way to produce
it, and the version of the library.

Examples of what to report:

- a file that makes the library throw anything other than `TiffException` or `NotSupportedException` (an
  `OutOfMemoryException`, an `IndexOutOfRangeException`…);
- a small file that makes the library allocate much more memory than the limits of `TiffReadOptions` allow;
- a file that keeps the library busy for a long time compared with its size.

## Supported versions

Fixes go into the latest released version.
