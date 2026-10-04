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
