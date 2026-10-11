# 17 · Docs, gallery sample and comparison benchmarks

**Type:** nice to have · **Effort:** M · **Priority:** 6

## Problem
The README is the only documentation. There is no single place to see every widget, and the performance
claims are measured against TUI.NET itself only.

## Design
- **Gallery sample** (`samples/Tuinet.Samples.Gallery`): one screen per widget (tabs to switch), each with
  its code snippet shown next to it; doubles as a visual regression check (screenshots generated from it).
- **Docs**: `docs/` pages per topic (getting started, layout, widgets, input, testing, performance), API
  reference generated from XML docs (docfx or a lightweight generator), published with GitHub Pages.
- **Screenshot generator**: move the cell-buffer → PNG renderer used for the README images into
  `tools/screenshots/` so images can be regenerated (it currently lives outside the repo).
- **Comparison benchmarks** (separate project, not in CI): same scenario (200×60, 5,000-row scrolling list)
  in Spectre.Console live display and Terminal.Gui, measuring time per frame, allocations and bytes written.
  Publish methodology and numbers in the README.

## Verification
Docs build in CI; gallery screenshots regenerate deterministically; comparison numbers reproducible on one machine.

## Status: implemented

- Gallery: `samples/Tuinet.Samples.Gallery`, every widget live beside the code that draws it, with styled snapshot
  tests of every page.
- Docs: topic pages in `docs/` (getting started, layout, widgets, input, testing, performance) and the API reference
  for `Tuinet` and `Tuinet.Testing`, built with docfx (`dotnet tool restore && dotnet docfx docs/docfx.json`; the
  version is pinned in `dotnet-tools.json`). Only the sources are committed. CI builds the site with
  `--warningsAsErrors`, and `.github/workflows/docs.yml` publishes it to GitHub Pages on pushes to main
  (one-time setup: Settings → Pages → Source: GitHub Actions).
- Screenshots: `tools/screenshots` renders the Showcase and Gallery shots to PNG; CI runs it with `--check`, which
  fails when a shot isn't deterministic or doesn't match the committed image.
- Comparisons: `bench/Tuinet.Comparisons` (Spectre.Console live display, Terminal.Gui v2), not in CI. Methodology,
  numbers and caveats in its README; the table is also in the main README's Performance section.
