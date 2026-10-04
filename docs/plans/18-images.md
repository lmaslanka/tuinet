# 18 · Images (kitty graphics, sixel)

**Type:** nice to have · **Effort:** L · **Priority:** 8

## Problem
No way to show images (logos, charts, thumbnails). Niche for most TUIs, but a differentiator.

## Design
- `Image` widget taking pre-decoded RGBA pixels (no image decoding in the library; samples use a decoder).
- Protocols, chosen by detection: kitty graphics (`ESC _G … ESC \`, upload once by id, place per frame),
  sixel (`ESC P q … ESC \`, re-encode on change), fallback to half-block (`▀` with fg/bg) which works everywhere.
- Images live outside the cell grid: the renderer must place/delete them after the cell diff, keep a
  per-frame placement list, and treat covered cells as reserved so the diff doesn't overwrite them.
- Half-block fallback is a plain cell-grid widget and can ship first.

## Tests
Half-block rendering exact cells; kitty/sixel encoders against known byte outputs; placement add/move/delete
across frames with `TestTty`.
