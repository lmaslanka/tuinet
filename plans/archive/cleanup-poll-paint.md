# Poll wakeup, kernel latency, App paint/fetch

The kernel’s shape stays: `ITty` adapters, immediate-mode `Draw`/`Poll`, no widgets, no DI. These three slices remove a leak, fix real bugs, and stop the form from allocating on every key.

Out of scope: internal `Diff`/`VtParser`, Event file collapse, Diff span indexer, SGR batching, SIGWINCH, `AllowUnsafeBlocks`, termios mask names, `DrawBox` removal.

## 1. Deepen `Terminal.Poll` so `Post` wakes it

**Problem.** `Post` only enqueues. Unix `poll()` / Windows `WaitForSingleObject` wait on the tty. Harness papers over that with `screen.Fetching ? 50 : Infinite` (`Program.cs`). `Fetching` is App UI state used as a wakeup protocol.

**Solution.** Grow `ITty` by one method: `void Wake()`. `Terminal.Post` enqueues then calls `Wake()`. `Read` waits on tty **and** the wake source. Two adapters already exist (Unix, Windows); `FakeTty` becomes the test adapter. That is a real seam.

```
ITty
  Width, Height, Write, Read, Wake
      ├─ UnixTty:    poll(tty, pipe)
      ├─ WindowsTty: WaitForMultipleObjects(console, event)
      └─ FakeTty:    ManualResetEventSlim
```

**Unix** (`UnixTty` + `LibC`): `pipe()`; `poll` already takes `nfds` — pass 2. `Wake` writes one byte; `Read` drains the pipe and returns 0 if only the wake fired. Close both pipe fds in `Dispose`.

**Windows** (`WindowsTty` + `Kernel32`): auto-reset event; `WaitForMultipleObjects` on console input + event. `Wake` → `SetEvent`.

**`Terminal.Post`:** enqueue, then `_tty.Wake()`. Existing Poll already rechecks `_messages` after `Read` returns 0.

**Harness:** `Poll(..., Timeout.Infinite)` always. Delete `OptionsScreen.Fetching`.

**Tests:** Make `FakeTty.Read` block when empty and `timeoutMs != 0`, until `Wake` or `Enqueue`. Add `Poll_returns_posted_message_while_blocked` (background `Post` unblocks `Poll(Infinite)`). Existing `Posted_message_is_returned_from_poll` stays.

**Not this slice:** SIGWINCH, typed `Post<T>`, eventfd-only (pipe is portable).

## 2. Kernel bugs + latency

### Incomplete sequences (`Terminal.Poll`)

Today `Poll(0)` can still `Read(..., 10)` (`Terminal.cs` ~83–94). Drain with `timeoutMs: 0` in a loop; only if still incomplete **and** the caller’s timeout is non-zero, wait 10ms once, then `FlushIncomplete`. `Poll(0)` never sleeps.

Test via `FakeTty`: enqueue `ESC` only, `Poll(0)` must not hang.

### Size: one ioctl per check

`Width`/`Height` each call `RefreshSize()` (`UnixTty.cs` 42–44, `WindowsTty.cs` 50–52). `SizeChanged` reads both → 2 syscalls, up to twice per Poll plus Draw.

`Width` getter: refresh once, cache both. `Height` getter: return cache. `SizeChanged` already reads Width first.

### Wide-cell overwrite (`CellBuffer.Put`)

Putting a narrow rune on a wide lead leaves a ghost continuation; Diff then skips it (`CellBuffer.cs` 164–168). Before write:

- If this cell is a continuation, empty the lead at `x-1`
- If the next cell is a continuation of this lead, empty it
- If placing wide, also smash a wide lead that started at `x+1`

Tests in `BufferTests`: narrow over wide; write onto a continuation.

### Teardown: don’t `Dispose` the tty from `CrashGuard`

`CrashGuard.Restore` writes leave-alt bytes **and** `tty.Dispose()` while `Terminal` still owns it (`CrashGuard.cs` 59–64 vs `Terminal.Dispose` 136–139).

Add `ITty.Restore()` (termios / console mode only). `Dispose` = `Restore` + close fds. `CrashGuard` writes `Vt.ShowCursor` / `ResetStyle` / `AltOff`, then `Restore()` — no dispose. `Terminal.Leave` keeps the same VT bytes.

Unix/Windows `Dispose` already restores modes; extract that into `Restore()` so both paths share it.

No signal-integration test. Existing `Dispose_restores_cursor_and_leaves_alt_screen` still pins the VT bytes.

## 3. App paint / fetch / keys

### Paint without a throwaway `Field`

`PaintProject` does `new Field(); field.Set(...)` every frame (`OptionsScreen.cs` 362–366). Split bar painting:

- Text fields: iterate `_org`/`_pat` runes (masked → `•`), not `Display` → string → `EnumerateRunes`
- Project: paint the string + chevron; no `Field`, no caret

### Cache `Field.Text`

Invalidate on `Set` / `Insert` / `Backspace` / `Delete`. `Display` for unmasked returns the cache.

### `q` quits everywhere

Plan table: `q` / Ctrl-C quit in textbox, closed dropdown, and open dropdown. Today `q` only in `HandleProjectClosed`. Handle at the top of `HandleKey` next to Ctrl-C. Test: `q` on org focus returns false.

### Fetch without `Thread.Sleep` in tests

Ctor arg `Action<Action>? background = null`, default `new Thread(...){ IsBackground = true }.Start()`. Tests pass `work => work()`. `Drain` becomes: dequeue mailbox (already filled). Delete the 50×10ms loop.

Delete `Fetching` once Poll wakes.

### `HttpClient` reuse + testable adapter

`AzureDevOpsProjects` constructs a new `HttpClient` per call (`AzureDevOpsProjects.cs` 30). Ctor takes `HttpClient` (Harness passes one long-lived instance; or a static fallback). Tests: `HttpMessageHandler` stub, JSON `value[].name` sort, HTTP error → exception. Zero tests today.

Move `ProjectsLoaded` / `ProjectsFailed` to `OptionsScreen.cs` (or bottom of that file). Leave `IAzureProjects` as `string[] ListProjects(...)`.

Tiny: fix `SettingsStore.Default` `Path.Combine` indent (`SettingsStore.cs` 22–24).

## Files

| Area | Touch |
|---|---|
| Kernel | `ITty.cs`, `Terminal.cs`, `UnixTty.cs`, `WindowsTty.cs`, `LibC.cs`, `CrashGuard.cs`, `CellBuffer.cs` |
| Kernel tests | `FakeTty.cs`, `TerminalTests.cs`, `BufferTests.cs` |
| App | `OptionsScreen.cs`, `Field.cs`, `AzureDevOpsProjects.cs`, `IAzureProjects.cs` |
| App tests | `OptionsScreenTests.cs`, new `AzureDevOpsProjectsTests.cs` |
| Harness | `Program.cs` |

## Verify

`dotnet test` on Kernel + App. No new framework, no `IScreen` / `IWorker` type, no `ISettingsStore`.
