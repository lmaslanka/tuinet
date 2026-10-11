# Tuinet security review

Date: 2026-10-11 · Scope: `src/Tuinet`, `src/Tuinet.Testing`, samples, tools, bench, tests, CI/release configuration, at commit `9233843` (branch `docs/drop-plan-18`). No code was modified; this file is the only artifact.

## Summary / Top 5 priorities

Tuinet's core safety claims hold up well: application text cannot smuggle escape sequences to the terminal (the cell write path drops every C0/C1/format code point, the title is filtered, OSC 8 URLs are percent-encoded and controls refused, OSC 52 is base64), the input parser has fixed-size buffers for CSI/DCS/UTF-8 with saturating parameter arithmetic, every `stackalloc` is bounded, P/Invoke uses source-generated `LibraryImport` with blittable structs, and the terminal is restored on exit, crash and signals. The findings below are mostly denial-of-service and robustness issues in paths that take terminal- or data-controlled sizes without an upper bound. All findings marked "confirmed" were reproduced with a throwaway probe program against the public API (see Methodology).

| # | Severity | Finding | Where |
|---|---|---|---|
| 1 | **High** | A terminal-reported window size is only clamped from below; width × height is used for three allocations with no upper bound and without overflow checks. A 65535×65535 (or 46341²) report throws `OverflowException` out of `BeginFrame`; 30000×30000 allocated **28.8 GB** in the probe. Window size is attacker-controlled when an app is served over SSH (the client's window-change request is a 16-bit rows/cols pair) and by anyone sharing the tty (`stty rows 60000 cols 60000`). | `src/Tuinet/Terminal.cs:805`, `:72-77`, `:514-524`; `src/Tuinet/CellBuffer.cs:57`, `:475-477` |
| 2 | **Medium** | A bracketed-paste start (`ESC [ 200 ~`) with no end marker wedges all input forever (every key, including Ctrl+C, is appended to the paste buffer; `IsIncomplete` excludes the Paste state so nothing ever times out) and the buffer grows without bound (64 MB of input → +270 MB heap). The parser enters this state even when `BracketedPaste` is off. | `src/Tuinet/Internal/VtParser.cs:39`, `:53`, `:370-373`, `:599-621` |
| 3 | **Medium** | `Paragraph`'s line enumerator calls `IndexOf('\n')` over the *entire remaining text* for every visual line, so wrapping one long logical line is O(L²/width). Reached every frame through `Scrollbar = Auto/Always`, `Scroll`, `Paragraph.LineCount` and `Terminal.PrintAbove`. Measured: 4 MB single line with `Scrollbar = Auto` → **1.57 s per frame**; `PrintAbove` of a 20 MB line → **112 s** and +424 MB. A log/chat viewer rendering untrusted text is frozen by one long line. | `src/Tuinet/Widgets/Paragraph.cs:152-155`, `:79-93`, `:55-68`; `src/Tuinet/Terminal.cs:302-304` |
| 4 | **Low** (supply chain) | All GitHub Actions are pinned by mutable major tag (`@v7`, `@v6`, …), including the third-party `NuGet/login@v1` that performs the OIDC exchange for the nuget.org publish; tag-derived version strings are interpolated with `${{ }}` directly into `run:` shell lines in the release workflow; there is no NuGet lock file, package source mapping or `global.json`. | `.github/workflows/release.yml:122`, `:76`, `:82-83`, `:133`; `.github/workflows/ci.yml` (all `uses:`) |
| 5 | **Low** | Several smaller input-side issues that each deserve a one-line fix: a cursor-position reply with row 10⁶ makes the inline-mode start-up write 1 MB of newlines (`Renderer.ScrollScreen` loops per line); C1 control code points (U+0080–U+009F) are delivered as `KeyCode.Char` keys from both the kitty and the UTF-8 paths; an unterminated XTVERSION reply is re-typed into the app as Alt+Shift+P followed by its payload as keystrokes; invisible bidi/zero-width format characters (U+202E, U+200B…) are kept in `TextInputState.Text` while never being displayed. | `VtParser.cs:340-344`, `:398-401`, `:587-591`, `:528-545`; `Renderer.cs:236-247`; `Widgets/EditableText.cs:305-363` |

## Threat model

The library runs inside the user's process and talks to a terminal over a tty (or Windows console). Untrusted input reaches it from three directions:

1. **The terminal itself** (keyboard, mouse, pasted text, and replies to the queries the library sends: `CSI 6n` cursor position, `CSI ? 69 $ p` DECRQM, `CSI > 0 q` XTVERSION, `CSI ? u` kitty flags, plus window-size reports via `TIOCGWINSZ`/SIGWINCH). On a local machine the terminal is usually trusted, but it is not when the app runs as an SSH forced command, inside a shared tmux/screen session, or behind a terminal multiplexer/web terminal controlled by someone else; and *pasted text* is attacker-controlled on any machine (clipboard contents from a web page).
2. **Application data rendered into widgets**: strings from files, logs, the network, chat messages, file names. The library must never let those reach the tty as control sequences and must stay responsive on pathological strings (huge, deeply combining, bidi-controlled).
3. **Resize and job-control signals** (SIGWINCH, SIGTSTP/SIGCONT, SIGINT/SIGTERM/SIGHUP), which can be sent by any process of the same user and, for window size, by the SSH peer.

Out of scope: a compromised .NET runtime, a malicious `ITty` implementation supplied by the host application, and attacks requiring code execution in the process.

## Methodology

- Read every file under `src/Tuinet` (including `Internal/UnicodeWidth.g.cs` header and `tools/gen-width/gen.py`, which define which code points are width 0), `src/Tuinet/Testing/TestTty.cs`, all four `src/Tuinet.Testing` files, the sample entry points and the files that touch the terminal (`Program.cs`, `ShowcaseApp.cs`, `CommandPalette.cs`, `EditDialog.cs`, `Inline/Download.cs`, `Stress/Program.cs`, `Gallery/Program.cs`, `GalleryApp.cs`, `CodePanel.cs`, `Pages/InputPage.cs`), `tools/e2e/*`, `tools/screenshots/Program.cs`, `ScreenshotOptions.cs`, `Shot.cs`, `tools/pack/verify.sh`, `bench/*/Program.cs`, every `.csproj`, `Directory.Build.props`, `dotnet-tools.json`, `tuinet.slnx`, `.gitignore`, `run`, the three workflows, `docs/plans/04-release-pipeline.md`, `docs/plans/02-suspend-resume.md`, `README.md`, `docs/index.md`, `docs/input.md`, and the test files' method lists plus the tests that cover sanitization (`TerminalTests.Control_characters_in_text_never_reach_the_terminal`, `TerminalExtrasTests.Urls_are_sanitized`, `VtParserTests`, `UnixTermiosTests`, `RendererFuzzTests`).
- Grepped the whole tree for file/process/network/environment/temp-file usage, secrets, `unsafe`, `stackalloc`, `Unsafe.*`, `MemoryMarshal`.
- Ran `dotnet restore`, `dotnet list package --vulnerable --include-transitive`, `--deprecated`, `--include-transitive` (results below).
- Wrote a probe console project in the session scratchpad (public API only, over `Tuinet.Testing.TestTty`) to confirm or refute each suspicion: terminal-size overflow/allocation, paste wedge and growth, paste-end injection, oversized mouse coordinates, oversized cursor report, `Markup.Parse` scaling, bidi/zero-width retention, grapheme intern growth, C1 runes as keys, unterminated DCS, oversized CSI parameters, title/link sanitization, `PrintAbove` and `Paragraph` cost on long lines. Numbers quoted below are from those runs (Release build, .NET 10, this machine). One experiment (resize to 30000×30000) transiently allocated ~29 GB; it completed and the memory was released.
- Verified the termios bit masks and struct layouts against glibc/XNU headers by hand, and the Windows `INPUT_RECORD`/`KEY_EVENT_RECORD`/`CONSOLE_SCREEN_BUFFER_INFO` layouts against the Win32 definitions (not executed: no Windows or macOS host).

## Findings

### A. Terminal size, allocation and overflow

#### A1. Unbounded terminal size → integer overflow and multi-gigabyte allocations — **High** (confirmed)

`Terminal` clamps the reported size only from below:

```csharp
// src/Tuinet/Terminal.cs:805
private static Size Clamp(Size size) => new(Math.Max(1, size.Width), Math.Max(1, size.Height));
```

and then multiplies width × height three times without checked arithmetic or an upper bound:

```csharp
// src/Tuinet/Terminal.cs:74-77
Size size = FrameSize(_screen);
_front = new CellBuffer(size.Width, size.Height);
_back = new CellBuffer(size.Width, size.Height);
_out = new VtBuffer(size.Width * size.Height * 8 + 4096);
// src/Tuinet/CellBuffer.cs:57          _cells = new Cell[width * height];
// src/Tuinet/CellBuffer.cs:475-477     if (_cells.Length < width * height) { _cells = new Cell[width * height]; }
```

`UnixTty.RefreshSize` (`Platform/UnixTty.cs:344-350`) accepts any non-zero `ushort` pair from `TIOCGWINSZ`; `WindowsTty.RefreshSize` (`Platform/WindowsTty.cs:247-258`) any positive window rectangle. The size is re-read on every SIGWINCH and applied in `SyncSize` on the next `BeginFrame` (`Terminal.cs:514-524`), so the attack works at any time during the session, not only at start-up.

Observed with the probe:

| Reported size | Result |
|---|---|
| 65535×65535 (max `winsize`) | `OverflowException` from `new Cell[width*height]` (int wrap) — thrown out of `Terminal..ctor`/`BeginFrame`, i.e. an app crash with the terminal left for `CrashGuard` to restore |
| 46341×46341 | same `OverflowException` |
| 30000×30000 via `Resize` + `BeginFrame` | succeeded: **+28,800 MB heap, 76 s** (two `CellBuffer`s of 14.4 GB each; the `VtBuffer` of 7.2 GB only if a frame needs it) |

Attack scenarios: (a) a TUI served over SSH as a forced command (the increasingly common "SSH app" pattern): the client's `window-change` request carries 32-bit rows/cols which the server pty clamps to 16 bits, so a malicious client can request 65535×32767 (= 2,147,385,345 cells, fits in `int`) and make the process allocate 34 GB per buffer or, with 65535×65535, crash it immediately; (b) anyone with access to the same tty (`stty rows 60000 cols 60000` from another window of a shared tmux) does the same locally; (c) a buggy terminal reporting garbage sizes.

Recommended fix: clamp the size in `Terminal.Clamp` to a sane maximum (e.g. 1024×1024 or 4096×1024; even the largest physical setups are a few hundred columns by a few hundred rows), use `checked` arithmetic or `Math.BigMul` when sizing buffers, and make `CellBuffer`'s constructor/`Resize` reject sizes whose product exceeds a documented limit with an `ArgumentOutOfRangeException` rather than wrapping. Add a test that a `TestTty` reporting 65535×65535 does not throw out of `BeginFrame`.

#### A2. `PrintAbove` allocates 16 bytes per character of the printed text — **Low** (confirmed; shares the root cause of C1)

`Terminal.PrintAbove` renders the whole text into a scratch `CellBuffer` sized `width × lines` (`Terminal.cs:302-304`), i.e. one 16-byte cell per character of the line, before writing it. A 20 MB log line cost +424 MB. Combined with C1 it also took 112 s. Fix: render and emit the text row by row into the `VtBuffer` (or cap the printed text and append `…`).

### B. Input parsing (`Internal/VtParser.cs`)

Overall the parser is in good shape: the CSI buffer is a fixed 32 bytes with an overflow flag that drops the sequence (`:241-248`), parameters saturate at 1,000,000 and at most 4 are kept (`:25`, `:690`), the DCS buffer is 64 bytes and the payload is ignored past that (`:480-486`, `:499-502`), UTF-8 is validated by `Rune.DecodeFromUtf8` after a correct lead-byte/continuation check so overlongs and surrogates are dropped (`:189-205`, `:571-596`), kitty code points go through `Rune.IsValid`, every state transition consumes a byte, and `Terminal.Poll` reads at most 4096 bytes before draining events, so the event queue cannot grow beyond one read's worth. Oversized CSI parameters (`CSI 99999999999999999999 A`, 40-digit parameters, 20 parameters) were fed and produced either the capped key or nothing, never an exception.

#### B1. Bracketed paste: unbounded buffer and a stuck state with no escape — **Medium** (confirmed)

```csharp
// src/Tuinet/Internal/VtParser.cs:53
public bool IsIncomplete => _state is State.Esc or State.Csi or State.Ss3 or State.Utf8 or State.Dcs or State.DcsEsc;   // Paste is not here
// :370-373   case 200: _pasteLen = 0; _state = State.Paste; return;
// :604-609   if (_pasteLen == _paste.Length) Array.Resize(ref _paste, Math.Max(256, _paste.Length * 2)); _paste[_pasteLen++] = data[n++];
```

Once `ESC [ 200 ~` is seen, every subsequent byte is appended to `_paste` until `ESC [ 201 ~` arrives. There is no size cap, no timeout (`FlushIncomplete` is never invoked for this state because `IsIncomplete` is false) and, since raw mode turns ISIG off, Ctrl+C is just another swallowed key. The parser reacts to `200~` whether or not `TerminalOptions.BracketedPaste` was requested (`Terminal.cs:653-656` only decides whether mode 2004 is *enabled*).

Probe: after `ESC[200~abc`, `Poll` returned no event for `q`, Enter or anything else; feeding 64 MB of further bytes grew the heap by 270 MB (doubling plus copies) and still produced no event.

Scenarios: a dropped SSH connection or a tmux `send-keys`/`paste-buffer` mid-paste; a terminal that had mode 2004 left enabled by a previous program and then disconnects; or simply a user pasting a very large clipboard (hundreds of MB) into an app that only expected short input. The user cannot quit; the only recovery is `kill` from elsewhere.

Recommended fix: (1) cap the paste buffer (e.g. 16 MB; keep scanning for the end marker but stop storing, and deliver a truncated paste or a documented error); (2) include `State.Paste` in a timeout path: if no byte arrives for, say, 2 s while in Paste state, flush what was received as a `Paste` event and return to Ground (a real paste is delivered as one burst); (3) when `BracketedPaste` is false, treat `CSI 200 ~` like any unknown CSI instead of entering the state; (4) add tests for all three.

#### B2. Pasted text can terminate the paste and inject keystrokes — **Low / inherent** (confirmed)

Pasting `hello ESC[201~ :q!\r` produced `Paste "hello"`, then keys `:` `q` `!` `Enter`. This is the well-known bracketed-paste weakness; the terminal, not the library, is the party that can prevent it (modern xterm, VTE, kitty, foot, WezTerm and iTerm2 strip or neutralize `ESC` in pasted text, older ones don't). The library cannot tell the two apart, but it can reduce the blast radius: the `Paste` payload is handed to the app with all control characters intact (`:612-613` only normalizes line endings). `EditableText.Insert` drops them, but apps that forward paste text elsewhere (a command line, a `Process`, a file name) get raw `ESC`. Recommend: strip C0 (except `\n`, `\t`) and C1 from the paste string by default (or offer `TerminalOptions.SanitizePaste`, default on) and document the terminal-side caveat in `docs/input.md`.

#### B3. C1 control code points delivered as printable `Char` keys — **Low** (confirmed)

```csharp
// src/Tuinet/Internal/VtParser.cs:398-401
if (Rune.IsValid(codePoint) && codePoint >= 0x20) { Key(KeyCode.Char, codePoint, mods, kind); }
// :587-591 (UTF-8 path) if (Rune.DecodeFromUtf8(...) == Done) { Key(KeyCode.Char, rune.Value); }
```

`CSI 133 u` and the bytes `C2 85` both produced `KeyCode.Char` with rune U+0085 (NEL). The widgets are safe (`EditableText.Insert(Rune)` checks `Rune.IsControl`, and cells drop width-0 code points), but an app that appends `ev.Key.Rune` to its own strings gets C1 controls, which some terminals interpret when the app later prints them outside Tuinet (e.g. via `Console.WriteLine` after exit). Fix: exclude `0x7F..0x9F` in both paths (`!Rune.IsControl(rune)`).

#### B4. Unterminated DCS is re-typed as keystrokes — **Low** (confirmed)

`NotDcs` (`:528-545`) turns an `ESC P` that was not followed by `>` — *or an XTVERSION reply that never reached `ST`* — into Alt+Shift+P followed by the buffered payload as ordinary key presses. Feeding `ESC P > | k i t t y ( 0 . 3` and then silence produced `[Alt+P][>][|][k][i][t][t][y][(][0][.][3]`, which a focused text field would accept as typed text. A reply split across two reads with more than `EscapeTimeoutMs` (20 ms) between them (a laggy SSH link at start-up) is enough. Fix: only re-type the payload when the DCS was *not* introduced by `>` (i.e. it was really Alt+Shift+P); for a `>`-prefixed DCS that times out, drop it silently.

#### B5. Oversized cursor-position reply → 1 MB write and a screen scroll — **Low** (confirmed)

In inline mode the start row comes straight from the terminal's `CSI row ; col R` (`VtParser.cs:340-344`, saturating at 10⁶). `Terminal.Enter` (`Terminal.cs:686`) passes it to `PlaceBand`, which calls `Renderer.ScrollScreen(screenHeight, top + height - screenHeight)` — a loop that writes one `\n` per line (`Renderer.cs:243-246`). A reply of row 1,000,000 produced a 1,000,050-byte start-up write (999,978 newlines), scrolling the user's scrollback away. The same path is reached on every resize via `SyncSize` (`Terminal.cs:530-537` clamps `top` there, but `Enter` does not). Fix: clamp the reported row/column to the current screen size (`Math.Min(row, _screen.Height - 1)`) before use, and clamp `lines` in `ScrollScreen` to `screenHeight`.

#### B6. Mouse coordinates — no issue (confirmed)

Coordinates up to 999,998 arrive in `MouseEvent`; every consumer (`Rect.Contains`, `ListState.RowAt`, `Scrollbar.PositionAt`, `TextAreaState.OffsetAt`, `TreeState.HandleMouse`, `Tabs.Hit`, `Menu.HandleMouse`) bounds-checks before indexing. No crash with `ListState.HandleMouse`.

### C. Output and escape injection

#### C1. Control characters cannot reach the terminal through cells — no issue (verified)

`CellBuffer.Write` drops any code point whose width is 0 (`CellBuffer.cs:680-684`), and `UnicodeWidth` marks Cc, Cf (except prepend marks and SOFT HYPHEN), Mn, Me, Zl, Zp and Cs as width 0 (`tools/gen-width/gen.py:46-55`). ESC, BEL, C1 (including 0x9B CSI, 0x9D OSC, 0x9C ST), DEL, CR/LF, bidi controls and BOM are all removed before a cell exists; lone surrogates become U+FFFD via `Rune.DecodeFromUtf16`. The public `Cell` constructor replaces width-0 runes with a space (`Cell.cs:33-46`); `Fill` rejects non-single-column cells. The renderer only ever emits a cell's rune (`Renderer.cs:1145-1153`) or an interned cluster's text (`:1171-1192`); clusters cannot contain controls because UAX #29 breaks before and after every Control code point, and `Graphemes.Width` returns 0 (so the cluster is not drawn) when the cluster starts with a zero-width code point. `TerminalTests.Control_characters_in_text_never_reach_the_terminal` pins this behaviour.

#### C2. Title, clipboard and links — no injection found (verified)

- `SetTitle` drops `< 0x20`, `0x7F–0x9F` and U+FFFD and caps the title at 512 chars (`Terminal.cs:729-744`); the probe's `x ESC]52;c;QUJD BEL y U+009C z U+0085 w` came out as `ESC]2;x]52;c;QUJDyzw ESC\`.
- `CopyToClipboard` base64-encodes and caps at 74,000 bytes (`Terminal.cs:189-218`); the library never *reads* the clipboard (no OSC 52 `?` query), so there is no exfiltration path.
- `Links.Encode` refuses controls, C1 and broken surrogates and percent-encodes spaces and non-ASCII (`Links.cs:88-131`); length is capped at 2048 and the table at 4095 entries. `Renderer.SetLink` reserves `url.Length + 16` itself, so a row of distinct 2 KB links cannot overrun the `VtBuffer` reservation.

#### C3. URL scheme is not restricted — **Informational** (confirmed)

`SetLink` accepts `javascript:alert(1)` and `file:///etc/passwd`. OSC 8 handling is the terminal's responsibility (most hand the URI to `xdg-open`/`open`), but an application that links untrusted strings should be told to whitelist schemes. Recommend documenting this in `docs/input.md` or offering an allow-list parameter (`http`, `https`, `mailto`, `file` by default).

#### C4. `VtBuffer` is "unchecked-style" but memory-safe — no issue

`VtBuffer.Byte/Bytes/Rune` index a managed array or call `Span.CopyTo`, so an under-reservation throws `IndexOutOfRangeException`/`ArgumentException` instead of corrupting memory. I checked the per-row reservation (`(last - first + 2) * 96 + 64`) against the worst case of a cell (SGR with all attributes and two RGB colors ≈ 60 bytes, a 24-byte absolute move, a 4-byte glyph) and the separately reserved link and cluster paths; no shortfall found.

### D. Unicode handling

#### D1. Invisible format characters survive in editor text — **Low** (confirmed)

`EditableText.Clean`/`IsClean` (`Widgets/EditableText.cs:305-363`) remove C0/DEL/C1 and invalid UTF-16 only. Format characters (U+202A–U+202E bidi embeddings/overrides, U+2066–U+2069 isolates, U+200B ZWSP, U+2060 WJ, U+FEFF) are kept in the text but, having width 0, are never displayed. The probe inserted `safe U+202E evil U+200B` into a `TextInputState`: the widget shows `safeevil`, `Text.Length` is 10 and both code points are present. A user cannot see or caret-step onto what they pasted, and whatever the app does with `Text` (a file name, a command, a URL) carries the hidden characters — the "Trojan Source"/homoglyph pattern. Display-side spoofing is prevented (the override never reaches the terminal); the issue is the data side. Fix: in `Clean`, also drop General Category Cf code points that are not part of a cluster (keep ZWJ/ZWNJ/variation selectors when preceded by a base character), or at least U+200B, U+200E/F, U+202A–E, U+2060–64, U+2066–69, U+FEFF; and mention it in the `Insert` docs.

#### D2. Grapheme intern table is process-wide and never shrinks — **Low** (measured)

`Graphemes.Intern` (`Internal/Graphemes.cs:103-145`) keeps every distinct multi-code-point cluster ever rendered (up to 2²⁰ entries, `MaxEntries`, each ≤ 64 chars). 300,000 distinct 4-char clusters cost +35 MB in 335 ms; the cap therefore bounds the leak at roughly 120 MB for short clusters and several hundred MB for 64-char ones. Untrusted text with many unique base+mark combinations (random Zalgo text in a chat) fills it. It is bounded and degrades gracefully (new clusters fall back to their first code point), so Low; worth documenting as a limit and perhaps lowering the cap or making it configurable.

#### D3. Width and cluster edge cases — no issue found

- Clusters longer than 64 chars are truncated without splitting a surrogate pair (`Graphemes.cs:105-108`); `TextWidth.Of`, `SetString`, `SetText`, `Paragraph.Fit`, `TextArea.RowEnd` and `Fuzzy.Score` are all linear in the text length.
- Lone surrogates are not "Simple" (`Graphemes.cs:178-186`), so they go through `Rune.DecodeFromUtf16` and render as U+FFFD; `WriteAscii`'s `Unsafe` stores are only reached for `0x20..0x7E`.
- Table lookup: `Stage1` is indexed by `cp >> 8` for `cp < 0x110000` (4352 entries) and `Stage2` by `block << 6 | …` (≤ 64 per block) — in range by construction of the generator.
- East Asian *Ambiguous* characters are width 1 (wcwidth-compatible); on a terminal configured for ambiguous-wide, columns can drift. Cosmetic.

### E. Platform layer and resource handling

#### E1. P/Invoke correctness — no issue found (code review; Linux tested by the suite, macOS/Windows not executed)

- `LibraryImport` throughout (source-generated, no runtime marshalling, AOT-safe), `SetLastError = true` on every call whose errno is read.
- `TermiosLinux` (60 bytes, `c_line` at 16, `c_ispeed` at 52) matches glibc on x64/arm64 and is pinned by `UnixTermiosTests`; `TermiosMac` uses `nuint` for `tcflag_t`/`speed_t` and `cc_t[20]`, matching XNU; `PollFd`, `WinSize`, `TIOCGWINSZ` constants (0x5413 / 0x40087468), `O_CLOEXEC` (0x80000 / 0x1000000), `SIGTSTP` (20 / 18), `EAGAIN` (11 / 35), `VMIN`/`VTIME` offsets (6/5 Linux, 16/17 macOS) are correct. The `cfmakeraw` masks (`UnixTty.cs:354-374`) decode to exactly the documented flag sets on both platforms.
- The arm64 macOS variadic `ioctl` shim passes the pointer as the 9th argument, which is the documented Apple calling convention for variadics.
- Windows: `INPUT_RECORD` (explicit size 20, union at offset 4), `KEY_EVENT_RECORD` (16 bytes), `COORD`/`SMALL_RECT`/`CONSOLE_SCREEN_BUFFER_INFO` layouts are right; `ReadConsoleInputW` is bounded by `_records.Length`; the UTF-8 translation never writes past `buffer.Length - 4`; surrogate pairing is handled.
- No `fixed` on anything but the records array; every `stackalloc` has a constant or clamped size (`Layout` ≤ 64, `Table` ≤ 64 columns, `Renderer` ≤ 256 rows, `SetMarkup` ≤ 1024 chars, `Links` 2048, `EditableText` 128), and `[module: SkipLocalsInit]` is safe because each buffer is written before read.

#### E2. Terminal restore on every exit path — done well, one caveat

`CrashGuard` (`Internal/CrashGuard.cs`) restores on `ProcessExit`, `UnhandledException` and SIGINT/SIGTERM/SIGHUP/SIGQUIT; `UnixTty.Restore` is idempotent; `Suspend` restores before stopping and re-enters raw mode after; SIGCONT after an external SIGSTOP re-applies raw mode; the e2e suite checks `stty -g` before/after and the "wrote nothing under HOME" rule for every sample, including the suspend paths. Caveat (unverified, see *Needs verification*): if the host app also handles SIGINT (`Console.CancelKeyPress` with `Cancel = true`, or its own `PosixSignalRegistration`), CrashGuard's handler still runs, writes the leave sequence and restores cooked mode while the app keeps running.

#### E3. `UnixTty.Read` turns persistent `poll()` errors into a busy loop — **Low** (code-confirmed, not executed)

```csharp
// src/Tuinet/Platform/UnixTty.cs:153-158
int ready = LibC.poll(ref fds[0], 2, timeoutMs);
if (ready <= 0) { return 0; }   // Timeout, or EINTR
```

Any other failure (`EBADF` after the tty is closed under the process, `ENOMEM`) also returns 0, and `Terminal.Poll` with `timeoutMs < 0` loops immediately, spinning a core until the app exits. Fix: check `errno`; return 0 only for `EINTR`/`EAGAIN`, throw `IOException` otherwise (as `Write` already does).

#### E4. Wake pipe created without `O_CLOEXEC` — **Informational**

`LibC.pipe` (`UnixTty.cs:61`) leaks two descriptors into child processes started with raw `fork/exec` by the host app (.NET's `Process.Start` closes them, so only hand-rolled spawns are affected). Use `pipe2(O_CLOEXEC)` on Linux and `fcntl(FD_CLOEXEC)` on macOS.

### F. Denial of service through application data

#### F1. `Paragraph` wraps one long line in quadratic time — **Medium** (confirmed)

```csharp
// src/Tuinet/Widgets/Paragraph.cs:152-155
ReadOnlySpan<char> rest = _text[_pos..];
int newline = rest.IndexOf('\n');          // scans to the end of the text for every visual line
ReadOnlySpan<char> logical = newline < 0 ? rest : rest[..newline];
```

For a logical line of L chars wrapped at width W, `MoveNext` is called L/W times and each call scans O(L), so enumerating all visual lines is O(L²/W). `Paragraph.Render` itself stops after `Scroll + Height` lines, but `Scrollbar = Auto` (`:80-85`) falls back to a full `LineCount` once the text is taller than the view, `Scrollbar = Always` always does, `Scroll = n` pays n scans, and `Terminal.PrintAbove` (`Terminal.cs:302`) and the public `Paragraph.LineCount` enumerate everything.

Measured on an 80×24 frame, one line of `x`:

| Text | No scrollbar | `Scrollbar = Auto` | `Scroll = 10000` | `LineCount` |
|---|---|---|---|---|
| 1 MB | 1 ms | 187 ms | 96 ms | 98 ms |
| 2 MB | 0 ms | 390 ms | 238 ms | 381 ms |
| 4 MB | 1 ms | **1,573 ms per frame** | 560 ms | 1,544 ms |
| 4 MB in 40k short lines | — | 7 ms | — | — |

`PrintAbove` of a 20 MB line took 112 s. A viewer that shows attacker-supplied text (a minified JSON blob, a log line without newlines) in a scrollable `Paragraph` is frozen for seconds per frame; at 20 MB it is unusable.

Fix: remember the end of the current logical line and only search for the next `\n` when the enumerator moves past it (one `IndexOf` per logical line, giving O(L) overall). Add a regression test with a 2 MB single line and an upper time bound, or better, a test that counts calls.

#### F2. `Markup.Parse` is superlinear on `[`-heavy input — **Low** (confirmed)

For every `[` that does not start a valid tag, `Parse` does `markup[i..].IndexOf(']')` (`StyledText.cs:209`) and, when that fails or the tag is invalid, advances one char; `[a[a[a…` therefore rescans the rest of the string per `[`. Measured 70 ms at 100k chars, 233 ms at 200k, 552 ms at 400k (vectorized `IndexOf` keeps the constant small). Only matters when untrusted text is passed as *markup* (`SetMarkup`/`Markup.Parse`) rather than as text. Fix: bound the tag search (a tag is at most a few dozen chars; stop at the next `[` or after, say, 64 chars) or precompute the next `]` once.

#### F3. Things that are fine

- `Fuzzy.Score` is three linear passes; the pattern length is bounded by the text length check.
- `TextArea`/`TextInput` measure only visible rows; caret moves on a single huge line are O(line) per key, acceptable.
- `TreeState.Rebuild` is iterative with a cycle guard (`_open`), so a cyclic `ITreeSource` cannot recurse or loop forever (an `ITreeSource` returning absurd `ChildCount` is the app's own bug).
- Renderer work per frame is O(cells); the scroll-detection probes are bounded (`MinScrollRows`, three probes, nine samples).
- Resize storms: a `Resize` event is only produced when the size actually differs (`Terminal.cs:461-468`), buffers grow but never shrink (so no re-allocation churn), and inline mode's cursor query is bounded by `CursorReportTimeoutMs`. Subject to A1 for the size itself.
- `Links` and `Graphemes` tables are capped; `EditHistory` is capped at 1000 steps / 1 M chars (`EditableText.cs:661-662`).

### G. Samples, tools and tests

- **No secrets are persisted or hard-coded.** The rule "samples must not persist secrets" holds: no sample uses `File`, `Process`, `HttpClient`, sockets or environment variables (grep over `samples/`); Showcase's header comment says "Nothing is saved to disk" (`samples/Tuinet.Samples.Showcase/Program.cs:3`); the Gallery's password field is an in-memory `TextInputState(mask: '•')` whose `Text` is never read or displayed (`samples/Tuinet.Samples.Gallery/Pages/InputPage.cs:49`, no `_password.Text` access anywhere); `WidgetTests.Masked_input_never_draws_the_secret` pins masking; the e2e harness asserts that every sample run writes nothing under an empty `$HOME` (`tools/e2e/e2e.py:135-153`, `:375-376`).
- `CopyToClipboard`/`SetTitle` in the samples send only item names; `SetLink` only the repository URL (`ShowcaseApp.cs:602`).
- **Tools**: `e2e.py` runs `tmux` via `subprocess.run([...])` with no shell, quotes paths with `shlex.quote`, uses `tempfile.mkdtemp`, a private tmux socket (`-L`) and `-f /dev/null`, and runs apps under `env -i` with an empty HOME. `gen.py` downloads UCD files from `unicode.org` over HTTPS into `mkdtemp` without a checksum; the generated table is committed and reviewable, so this is a dev-time integrity note only. `tools/screenshots` writes PNGs under `docs/images` or an explicit directory; `tools/pack/verify.sh` inspects the packages with `unzip` only.
- **Tests**: `TestingTests` creates `Path.GetTempPath()/tuinet-snapshots-<GUID>` with `Directory.CreateDirectory` (not an exclusive create, but the GUID makes a pre-planted directory infeasible). `Snapshot.Verify` rejects names containing invalid file-name characters, which on Linux means only `/` and NUL — enough to prevent traversal, since the name is joined under `__snapshots__`. `TUINET_ACCEPT_SNAPSHOTS` lets an environment variable turn comparisons into writes; intended, but worth refusing when `GITHUB_ACTIONS`/`CI` is set so a mis-set CI variable cannot silently bless regressions.

### H. Supply chain, build and release

#### H1. Actions pinned by mutable tags, including the OIDC publish step — **Low** (would be Medium for a widely used package)

Every `uses:` in `ci.yml`, `docs.yml` and `release.yml` references a major tag (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`, `actions/upload-artifact@v7`, `actions/download-artifact@v7`, `actions/configure-pages@v6`, `actions/upload-pages-artifact@v5`, `actions/deploy-pages@v5`, `NuGet/login@v1`). A compromised or force-moved tag runs attacker code; for `NuGet/login@v1` (`release.yml:122`) that code holds the short-lived nuget.org API key and runs inside the `release` environment with `contents: write`. Fix: pin every action to a full commit SHA (with the tag in a comment) and let Dependabot/Renovate bump them; add `permissions: {}` at the top of `release.yml` and grant per job (today the top level grants `contents: read`, which is already minimal).

#### H2. Expression interpolation of a tag-derived string into shell — **Low** (defence in depth)

`release.yml` interpolates `${{ steps.version.outputs.version }}` into `run:` lines (`:76`, `:82-83`) and `${{ needs.pack.outputs.version }}` into the `gh release create --title` line (`:133`). The value is derived from `GITHUB_REF_NAME`; the prefix is checked against `VersionPrefix`, but the suffix is free-form and git allows `$`, `(`, `)`, `;`, `|` in tag names, so `v0.1.0-$(cmd)` passes the check and is executed when the step's script is composed. Only someone who can push a `v*` tag can trigger it (and that person can usually edit the workflow anyway), hence Low. Fix: expose the value through `env:` and reference it as `"$VERSION"` in the script, and protect `v*` tags with a tag-protection rule so only maintainers can create them.

#### H3. No NuGet lock files, source mapping or SDK pin — **Low**

There is no `nuget.config` (so no package source mapping and no explicit source list beyond the default), no `packages.lock.json` (`RestorePackagesWithLockFile` is off), no `Directory.Packages.props` (central package management) and no `global.json`. The library itself has a single, version-pinned, `PrivateAssets="all"` analyzer dependency (`Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0`), so the shipped package has no runtime dependencies to drift; the risk is confined to the build/test/bench graph (BenchmarkDotNet, xunit, Spectre.Console, Terminal.Gui and ~60 transitive packages). Recommend enabling `RestorePackagesWithLockFile` with `--locked-mode` in CI and adding a `global.json` with `rollForward: latestFeature`.

#### H4. Release integrity — **Informational**

Positive: nuget.org Trusted Publishing (OIDC, no stored API key), a protected `release` environment, deterministic builds with `ContinuousIntegrationBuild`, SourceLink + embedded untracked sources, `.snupkg` symbols, and `tools/pack/verify.sh` which checks the package contents and the recorded commit. Missing: build provenance attestations (`actions/attest-build-provenance`) and/or NuGet package signing, which would let consumers verify that a package on nuget.org came from this workflow. `dotnet-tools.json` pins `docfx 2.81.0` with `rollForward: false` — good.

#### H5. Deprecated test dependency — **Informational**

`xunit 2.9.3` is flagged "Legacy" by NuGet (alternative `xunit.v3`). Test-only; no action needed beyond planning the migration.

## Needs verification

Suspicions I could not confirm in code or by experiment on this (Linux) machine:

1. **CrashGuard vs. an app that cancels SIGINT.** `CrashGuard.Register` (`Internal/CrashGuard.cs:36`) restores the terminal from its own `PosixSignalRegistration` for SIGINT/SIGTERM/SIGHUP/SIGQUIT without consulting whether another handler set `context.Cancel`. If the host app handles SIGINT itself and keeps running (e.g. `Console.CancelKeyPress` with `e.Cancel = true` to do a graceful shutdown), the terminal would be left in cooked mode on the normal screen while the app continues to draw; `UnixTty._restored` would then also make the later `Dispose` a no-op. In raw mode a keyboard Ctrl+C never raises SIGINT (it is a key), so this needs an external `kill -INT`. Not executed; needs a test with a second registration.
2. **Windows backend** (`Platform/WindowsTty.cs`): struct layouts and the mode/code-page restore look correct, and `CrashGuard` relies on .NET mapping SIGINT/SIGQUIT/SIGTERM/SIGHUP to Ctrl+C/Ctrl+Break/close events. Not executed (no Windows host); the README already marks it "implemented, not yet tested".
3. **macOS** termios/`ioctl` path: constants and layouts hand-checked only.
4. **Signal-handler write races**: `CrashGuard.Restore` writes the leave sequence from the signal thread while the UI thread may be in the middle of `Write`; the result is at worst interleaved bytes on a dying process. Harmless in theory, not observed.
5. **`DispatchCsi` cursor-report swallowing**: after a timed-out `CSI 6n`, `ExpectCursorReport` stays set, so the next *modified* F3 (`CSI 1;5R`) from the user is consumed as a report. Trivial impact; not tested.
6. Whether any terminal in common use reflects `CSI ? 69 ; 1 $ y` dishonestly in a way that makes the renderer's `DECSLRM` path garble output — a correctness, not security, question.

## Dependency & supply-chain audit results

Commands (run from the repository root, .NET SDK `10.0.x`, source `https://api.nuget.org/v3/index.json` only):

```
$ dotnet restore tuinet.slnx
  All projects are up-to-date for restore.   (exit 0)

$ dotnet list tuinet.slnx package --vulnerable --include-transitive
The following sources were used:
   https://api.nuget.org/v3/index.json
The given project `Tuinet.Benchmarks` has no vulnerable packages given the current sources.
The given project `Tuinet.Comparisons` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Gallery` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Inline` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Showcase` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Stress` has no vulnerable packages given the current sources.
The given project `Tuinet.Testing` has no vulnerable packages given the current sources.
The given project `Tuinet` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Gallery.Tests` has no vulnerable packages given the current sources.
The given project `Tuinet.Samples.Showcase.Tests` has no vulnerable packages given the current sources.
The given project `Tuinet.Testing.Tests` has no vulnerable packages given the current sources.
The given project `Tuinet.Tests` has no vulnerable packages given the current sources.
The given project `Screenshots` has no vulnerable packages given the current sources.
   (exit 0)

$ dotnet list tuinet.slnx package --deprecated
Project `Tuinet.Tests` (and the three other test projects) has the following deprecated packages
   Top-level Package      Requested   Resolved   Reason(s)   Alternative
   > xunit                2.9.3       2.9.3      Legacy      xunit.v3 >= 0.0.0
   (all other projects: no deprecated packages)
```

Direct dependencies of the shipped packages: `Tuinet` → `Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0` (`PrivateAssets=all`, analyzer only; **no runtime dependencies**); `Tuinet.Testing` → the same analyzer plus a project reference to `Tuinet` (verified by `tools/pack/verify.sh` to resolve to the same version). Samples pull only the SDK-implicit `Microsoft.NET.ILLink.Tasks 10.0.8`. Test/bench graph: `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `coverlet.collector 6.0.4`, `BenchmarkDotNet 0.15.8`, `Spectre.Console 0.57.2`, `Terminal.Gui 2.5.0` and their transitives (full list in `dotnet list package --include-transitive` output; nothing vulnerable at the time of the run).

Build-time code execution: no custom MSBuild targets, no `Exec` tasks, no source generators beyond the SDK's `LibraryImport` generator, no NuGet `init.ps1`-style scripts. `Directory.Build.props` sets `TreatWarningsAsErrors`, `Deterministic`, `InvariantGlobalization` and `ContinuousIntegrationBuild` under GitHub Actions.

## Test coverage gaps

Existing coverage worth noting: 27 focused `VtParserTests` (split UTF-8, invalid UTF-8 recovery, lone ESC, paste split across reads, paste not interpreting escapes, unknown CSI, cursor-report gating), `TerminalTests.Control_characters_in_text_never_reach_the_terminal`, `TerminalExtrasTests.Urls_are_sanitized` and the OSC 8 round trip, `GraphemeTests.Pathological_clusters_are_cut_when_stored`, `UnixTermiosTests` layout pinning, the property-based `RendererFuzzTests` (random frames replayed through a VT emulator, including REP and legacy-width terminals), allocation tests, and the tmux end-to-end suite (modes restored, nothing written under HOME, suspend/resume). Gaps:

1. **No fuzz/property test for `VtParser`.** Random byte streams (and random concatenations of valid sequences cut at arbitrary points) should never throw, never leave the parser in a state that cannot be exited by `FlushIncomplete`/further bytes, and never emit a `KeyCode.Char` whose rune is a control. A few hundred lines with `System.Random`, in the style of `RendererFuzzTests`, would have caught B1, B3 and B4.
2. **No test for oversized or hostile terminal reports**: window sizes ≥ 46341², cursor replies far outside the screen, DECRPM/kitty replies with absurd parameters, a `200~` without `201~`, a paste larger than a cap.
3. **No test for the paste timeout/cap** (does not exist yet) or for `200~` arriving while `BracketedPaste` is off.
4. **No performance/scaling tests** for untrusted text: a 2 MB single line in `Paragraph` with `Scrollbar = Auto`, `PrintAbove` of a long line, `Markup.Parse` of `[`-heavy input, a text with 10⁵ distinct clusters. Even a coarse "must finish in < 200 ms" guard would flag quadratic regressions.
5. **No test that format characters are stripped (or deliberately kept) by `EditableText.Insert`**, and none that `KeyEvent.Rune` is never a C1 control.
6. **Platform tests exist only for the Linux termios layout**; the macOS `TermiosMac` layout and the Windows structs have no size/offset assertions (they could be asserted with `Marshal.SizeOf`/`OffsetOf` under `OperatingSystem.IsMacOS()`/`IsWindows()` so they run on the CI matrix's macOS and Windows runners, which `ci.yml` already has).
7. **CrashGuard** has no test for the interaction with a second SIGINT handler (see *Needs verification*).
8. **Supply chain**: no CI step runs `dotnet list package --vulnerable` (or Dependabot alerts for NuGet and GitHub Actions); adding both is cheap.

## What's done well

- **Output sanitization by construction.** Control, format and separator code points are width 0 in a generated, versioned table and are dropped before a `Cell` exists; the renderer can therefore only emit what the buffer holds. Title, link and clipboard paths each have their own filter, and all three are tested.
- **Input parser discipline.** Fixed 32-byte CSI and 64-byte DCS buffers with explicit overflow handling, saturating parameter parsing, `Rune`-validated UTF-8 and kitty code points, a `MaxParams` cap, cursor reports accepted only when expected, and an event queue bounded by the 4 KB read buffer. Oversized and malformed sequences were fed in volume without an exception.
- **Memory safety despite the performance focus.** Every `stackalloc` is constant-sized or clamped with a heap fallback; `Unsafe`/`MemoryMarshal` use is confined to `Cell`/`CellBuffer`/`Renderer` on a 16-byte blittable struct with layout tests; `VtBuffer` throws rather than corrupts when a reservation is wrong; `[SkipLocalsInit]` is justified.
- **Platform layer.** Source-generated `LibraryImport`, correct termios masks and layouts on both Unix flavours, a pinned Linux layout test, the arm64-macOS variadic `ioctl` handled, idempotent restore, SIGWINCH/SIGTSTP/SIGCONT handled with the runtime's own SIGCONT behaviour cancelled, and `CrashGuard` covering unhandled exceptions, `ProcessExit` and fatal signals.
- **Operational hygiene.** Samples write nothing and persist no secrets; the e2e suite proves "modes restored" and "nothing under HOME" for every sample on a real terminal; tools use `mkdtemp`, argument lists instead of shells and quoted paths.
- **CI/release.** Least-privilege `GITHUB_TOKEN`, `pull_request` rather than `pull_request_target`, a gated `release` environment, nuget.org Trusted Publishing instead of a stored key, deterministic builds with SourceLink, package content verification, a pinned docfx tool, public-API baselines that make surface changes reviewable, and Native AOT publishing checked on every PR.
- **Bounded caches.** Links (4095 × 2 KB), grapheme clusters (2²⁰ × 64 chars), undo history (1000 steps / 1 M chars) and the tree walk (iterative, cycle-guarded) all have explicit limits with graceful degradation.
