# 04 · Release pipeline and API hygiene

**Type:** hole · **Effort:** S · **Priority:** 3

## Problem
The library can't be shipped safely: no `<Version>`, no pack/publish job, no SourceLink/symbols, no
changelog, and no record of the public API, so breaking changes go unnoticed.

## Design
- Versioning: `VersionPrefix` in `Directory.Build.props` (start `0.1.0`); tag `v*` triggers a release.
- Packaging: `dotnet pack -c Release` with SourceLink (`Microsoft.SourceLink.GitHub`), `.snupkg` symbols,
  deterministic CI build, package icon, `RepositoryUrl`.
- `.github/workflows/release.yml`: on tag, build + test + pack + `dotnet nuget push` (API key secret) +
  GitHub release with notes from `CHANGELOG.md`.
- Public API baseline: `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` /
  `PublicAPI.Unshipped.txt`; any public change shows up in review.
- `CHANGELOG.md` (Keep a Changelog format), starting with the current feature set.
- CI pack check: pack on every PR and inspect the nupkg (README, XML docs, no test assemblies).

## Files
`Directory.Build.props`, `src/Tuinet/Tuinet.csproj`, `src/Tuinet/PublicAPI.*.txt`, `.github/workflows/release.yml`,
`.github/workflows/ci.yml`, `CHANGELOG.md`.

## Verification
Dry run: tag a pre-release on a fork or push to a local feed; install into a fresh console app and run a frame.
