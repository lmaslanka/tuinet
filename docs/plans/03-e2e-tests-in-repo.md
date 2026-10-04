# 03 · End-to-end tests in the repo and in CI

**Type:** hole · **Effort:** S · **Priority:** 3

## Problem
The real-terminal checks used so far live outside the repo (a session scratch folder):
- a **PTY driver**: spawns an AOT sample on a pseudo-terminal at a given size, feeds keys, replays the output
  through a tiny VT model, checks screen text, clean exit, termios restored, no files written under `$HOME`;
- a **tmux scroll check**: runs the Stress sample in a detached tmux session, sends `j`/`k`/PageDown/PageUp
  bursts, validates every visible row (`item N  0x<N*2654435761>` and consecutive N), and logs the pane
  output to confirm IL/DL scroll sequences were actually used.
They are the only coverage against a real VT implementation (tmux) and will be lost.

## Design
- `tools/e2e/` with the two scripts (Python 3, stdlib only), parameterised by binary path.
- Make the PTY VT model handle DECSTBM/IL/DL (it currently ignores them) or replace it with tmux capture.
- `tools/e2e/run.sh`: publish both samples with Native AOT, run both checks, non-zero exit on failure.
- CI: run it in the existing `native-aot` job on ubuntu (install `tmux`).

## Files
`tools/e2e/pty_showcase.py`, `tools/e2e/tmux_scroll.py`, `tools/e2e/run.sh`, `.github/workflows/ci.yml`, README (testing section).

## Verification
CI green on a PR; deliberately break the renderer (e.g. disable the forced CUP after a shift) and see it fail.

## Status: implemented (branch `e2e-tests`)

- `tools/e2e/e2e.py` (Python 3, stdlib only) drives everything through tmux instead of a home-made VT
  model: a private tmux server per run, apps in a shell wrapper that records the exit code and `stty -g`
  before/after, an empty `HOME`, and waits that poll the screen instead of fixed sleeps. 45 checks, ~11 s.
- Three suites: **showcase** (110×34 and 80×24), **stress** (scroll bursts, row content/order, IL/DL seen),
  **clusters** (`tools/e2e/ClusterScreen` vs a reference that places every cell absolutely; the cases live
  in `tools/e2e/clusters.txt`, read by both sides).
- `tools/e2e/run.sh [publish-dir]` publishes what's missing with Native AOT and runs it; `./run e2e`;
  CI runs it in the `native-aot` job on the binaries that job already publishes.

**Verified by breaking the renderer:** rows mapped the wrong way after a scroll → 6 stress failures; no
reposition after a lone emoji-plane glyph → both cluster comparisons fail. Two other breaks (cursor not
marked unknown after a shift, margins never reset) are harmless on these screens and are caught by the
renderer fuzz tests instead (19 failures for the margins one); the two layers complement each other.

**Not covered:** colors and attributes (`capture-pane` is compared as text). `capture-pane -e` could add
style checks later.
