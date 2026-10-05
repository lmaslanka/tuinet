# Roadmap plans

One plan per gap found in the 2026-10-03 review. Priority 1 is next.

| # | Plan | Type | Effort | Priority |
|---|---|---|---|---|
| 01 | [Grapheme clusters](01-grapheme-clusters.md) ✅ done | hole | L | 1 |
| 02 | [Suspend and resume (Ctrl+Z)](02-suspend-resume.md) ✅ done | hole | S | 4 |
| 03 | [End-to-end tests in the repo and CI](03-e2e-tests-in-repo.md) ✅ done | hole | S | 3 |
| 04 | [Release pipeline and API hygiene](04-release-pipeline.md) ✅ done | hole | S | 3 |
| 05 | [Mouse support in widgets](05-mouse-helpers.md) ✅ done | hole | M | 4 |
| 06 | [Mixed-style text](06-styled-text.md) ✅ done | missing | M | 2 |
| 07 | [Scrollbar](07-scrollbar.md) | missing | S | 5 |
| 08 | [Multi-line text editing](08-text-area.md) | missing | L | 5 |
| 09 | [Tabs, tree, charts, popup, menu](09-more-widgets.md) | missing | M each | 5 |
| 10 | [Inline mode](10-inline-mode.md) | missing | M | 4 |
| 11 | [Title, clipboard, cursor shape, hyperlinks, kitty keyboard](11-terminal-extras.md) | missing | S–M each | 5 |
| 12 | [Erase with escape codes](12-erase-sequences.md) | performance | S | 4 |
| 13 | [Left/right scroll margins](13-left-right-margins.md) | performance | M | 6 |
| 14 | [Repeat-character sequence](14-repeat-glyph.md) | performance | S | 7 |
| 15 | [Main-loop and focus helpers](15-loop-and-focus-helpers.md) | nice to have | S | 6 |
| 16 | [Test helpers for library users](16-test-helpers.md) | nice to have | S | 6 |
| 17 | [Docs, gallery, comparison benchmarks](17-docs-and-comparisons.md) | nice to have | M | 6 |
| 18 | [Images](18-images.md) | nice to have | L | 8 |

Ground rules for every plan: 0 bytes allocated per steady-state frame, bytes written per frame as a
product metric, AOT/trim clean, stateless `readonly ref struct` widgets with app-owned state, and no
retained widget tree or focus manager.
