# Two screens + clean input

Kernel stays immediate-mode: key → Handle → Draw, no widgets, no `Task`, no focus framework. The app today is one type (`OptionsScreen`) that is both the branch list and the settings form. Paint when the dialog is open **skips** the branch list, so you only see Options.

## What you should see

```
┌─ tuinet ─────────────────────────────────────┐
│ * master                                     │
│   feature/auth                               │
│   bugfix/window-resize                       │  ← always painted
│                                              │
│          ┌─ Options ─────────────────┐       │
│          │ Organization: [........]  │       │  ← overlay when open
│          │ PAT:          [••••]      │       │
│          │ Project:      [....▾]     │       │
│          │        [Save]  [Close]    │       │
│          └───────────────────────────┘       │
│ j/k move  o options  q quit                  │
└──────────────────────────────────────────────┘
```

- **Start:** main only (branch list). Dialog closed.
- **`o` / `O`:** open dialog on top of main. Branches stay visible around it.
- **Save / Close / esc:** dismiss dialog, main remains.
- **`q` / ctrl-c:** quit the app from either screen.

## Split (three modules, no `IScreen`)

One adapter each — do not invent a screen interface.

| Module | Owns | Does not own |
|---|---|---|
| **App** | overlay flag, quit keys, `Paint` = main then dialog | git, HTTP, fields |
| **Main** | branch list, cursor, `j`/`k`/`↑`/`↓` | options form |
| **Options** | fields, dropdown, Save/Close, fetch | tty, the loop |

```
Harness
  Terminal.Open / Poll / Draw
  └── App.Handle / App.Paint
        ├── Main.Handle / Main.Paint     (always)
        └── Options.Handle / Options.Paint (when open)
```

Rename `OptionsScreen` away. Harness constructs `App`. Tests construct `Main` or `Options` directly.

### App

```
Handle(Event):
  message  → Options.Handle (fetch results)
  resize   → true (Harness redraws)
  q/ctrl-c → false (quit)
  if open  → Options.Handle   // esc closes; does not quit
  else     → Main.Handle      // o opens

Paint(buffer):
  fill + outer box
  Main.Paint
  if open: Options.Paint overlay
```

`o` lives on Main. Closing lives on Options (`esc`, Close, Save). App only routes.

### Main

State: `GitBranch[]`, `_sel`, `_scroll`, directory title.

Keys: `j`/`k`/`↓`/`↑` move (clamp). `o`/`O` → App opens Options. Nothing else.

Paint: branch rows inside the outer box. Cursor = `Theme.ListCursor`. Current git branch keeps `*`. Hint: `j/k move  o options  q quit`.

### Options

Today’s form, unchanged behaviour: Tab cycle, masked PAT, project dropdown, Save persists + closes, Close/esc dismiss without save. Paint a **centered** box; do not clear the whole screen (Main already painted).

Fetch stays here. `ProjectsLoaded` / `ProjectsFailed` still come through App as messages.

## Input: modes, not a keymap table

Keep Handle **synchronous**. One key still equals one frame. No bind lists, no command objects, no `Task`.

Each module is a **mode**: a small `Handle(KeyEvent)` switch for the keys that mode owns.

```
Main:     j k ↓ ↑ o O
Options:  tab enter esc + field insert / dropdown j k
App:      q ctrl-c, then dispatch
```

`j` on Main moves the branch cursor. `j` in an Options text field is a character (Field.Handle). `j` in the project dropdown moves the list. Same key, different mode — that is the whole design.

Do **not**:

- A global keymap / chord parser
- `ICommand`, DI, or a widget focus ring
- Input on a background thread
- Extra allocations on the key path (no `new Field()` per frame — already fixed)

`Field` stays the text-editing module. Options calls it only when org/PAT is focused.

## Files

| Touch | Role |
|---|---|
| `App.cs` | shell: route + overlay paint |
| `MainView.cs` (or `BranchList.cs`) | branch list + j/k |
| `OptionsDialog.cs` | form; today’s OptionsScreen body |
| delete or empty `OptionsScreen.cs` | |
| `Program.cs` | construct `App` |
| tests | `MainViewTests` (j/k, list), `OptionsDialogTests` (form), one App test (o opens, esc closes, main still painted) |

## Verify

- Start: no `Organization:` on screen; branches visible.
- `o`: dialog centered; branch names still visible outside the dialog.
- `j`/`k` on main move the cursor; `j` in org field inserts `j`.
- Save/Close/esc: dialog gone, branches still there.
- `q` quits from main and from the dialog.
- Hold `j` on the branch list: 1:1 with key repeat (kernel path unchanged).

## Out of scope

Checking out a branch on enter, remote branches, a third screen, mouse, keymap config files.
