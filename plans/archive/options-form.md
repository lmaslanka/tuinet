# Options form (org, PAT, project)

Not a Lazygit clone. First real screen: settings that look like [jiratui’s home form](https://jiratui.sh/assets/img/gallery/screenshot-jiratui-home.png), talk to Azure DevOps, persist JSON.

## Look (from the screenshot)

- Dark full-screen form, not a tiny overlay (a centered modal can wait).
- **Label on the left**, colon, then a **wide filled bar** for the value. Labels are muted; the bar is the widget.
- **Focused** field: bright cyan/teal background, dark text, full remaining width.
- **Unfocused** field: darker filled bar, lighter text.
- No `┌─┐` around the inputs. The fill *is* the textbox.
- Title at top. Hint/status at bottom (`tab` next field, `enter` open/save, `q` quit).
- Dropdown **closed** = same filled bar, current project name (or placeholder), a `▾` at the right edge.
- Dropdown **open** = that bar plus a list under it (`j`/`k`, enter to pick). Same selection style as the existing lists.

Three rows:

| Label | Widget |
|---|---|
| `Organization:` | textbox |
| `PAT:` | textbox, masked (`•`) |
| `Project:` | dropdown, filled from Azure after org+PAT work |

## Azure DevOps

A PAT cannot list projects alone. URL is org-scoped:

```
GET https://dev.azure.com/{organization}/_apis/projects?api-version=7.1
Authorization: Basic base64(":" + pat)
```

PAT needs **Project (read)**. Success → names into the dropdown. Failure → stay on the form, status line shows the error, dropdown empty.

Do **not** ask the user to type the project first. Load the list; they pick.

## JSON save file

Path: `$XDG_CONFIG_HOME/tuinet/settings.json` (fallback `~/.config/tuinet/settings.json`). Create the dir. Mode **0600**.

```json
{
  "organization": "my-org",
  "pat": "…",
  "project": "MyProject"
}
```

PAT lives in this file because you asked for JSON. Never paint the real PAT into the cell buffer (mask it). Never write it to logs/status.

Load on startup. If missing/invalid, show the form empty. Save when the user confirms a project (enter on a selected project, or an explicit save). Write via temp file + rename so a crash doesn’t truncate.

## Kernel vs app

Still **not** a widget framework. Immediate-mode: each frame the app paints the form on top of a cleared buffer.

**Kernel additions (tiny):**

1. **`Fill(Rect, Style)`** — write spaces with fg/bg so the cyan bars exist. Without this the screenshot look is impossible (today empty cells are default style).
2. **Posted events** — HTTP cannot run on the UI thread (kernel has zero `Task`s). A worker enqueues a result; `Poll` returns it. While a request is in flight, `Poll(50)` so the loop wakes; idle stays `Timeout.Infinite`.
3. **No real cursor.** Caret = invert fg/bg on the glyph cell (or a ` ` if empty). Terminal cursor stays hidden.

**App (harness):**

- `Field` state: `string Text`, `int Caret`, `bool Masked`
- Insert/backspace/left/right/home/end on the focused textbox
- `Tab` / `Shift+Tab` (if we have shift; else `Tab` cycles) between org → pat → project
- Project field: `enter` toggles dropdown; `j`/`k` move only while open; `enter` selects and **saves**
- Typing in org/pat does not block; refetch projects when both are non-empty after leaving PAT or on `enter` in PAT (don’t hit the API on every key)
- `IAzureProjects` seam: real HTTP + a fake for tests

## Keys

| Key | Textbox | Dropdown closed | Dropdown open |
|---|---|---|---|
| printable | insert | — | type-to-filter later, not v1 |
| backspace/delete | edit | — | — |
| left/right/home/end | caret | — | — |
| tab | next field | next field | next field (closes) |
| j / k / down / up | — | — | move selection |
| enter | next field; if PAT just finished, fetch | open list (fetch if needed) | pick + save |
| esc | quit if nothing open | quit | close list |
| q / ctrl-c | quit | quit | quit |

## Build order

1. **`Fill(Rect, Style)`** — TDD: filled cells have the background; clip to rect.
2. **Paint one label+bar row** in the harness that matches the screenshot (muted label, cyan bar, text padded one cell). Prove focus vs unfocus colors.
3. **Text editing** — org field only: type, backspace, caret invert. Still snappy (hold keys, 1:1).
4. **Masked PAT field** — buffer shows `•` × length; state holds the real string.
5. **Tab** between org and PAT.
6. **Posted event + fake client** — after PAT confirm, dropdown fills with stub names. No HTTP yet.
7. **Dropdown** — closed bar with `▾`; open list clipped under the field; `j`/`k`/enter.
8. **Real Azure GET** on a background thread; errors on the status line.
9. **JSON load/save** 0600; first run opens this form.

Snap test at step 3: if typing lags, stop and fix the kernel. Do not add HTTP on a slow paint path.

## Anti-goals (this slice)

Mouse, real terminal cursor, layout engine, validation beyond “request failed”, org discovery from PAT, OS keyring, async/await in the kernel, fetching on every keystroke.

## Colors (screenshot-ish, tweak in one place)

| Role | RGB |
|---|---|
| screen bg | `20, 24, 28` |
| label | `140, 160, 170` |
| field focused bg | `0, 200, 200` |
| field focused fg | `16, 16, 16` |
| field idle bg | `40, 70, 80` |
| field idle fg | `200, 220, 220` |
| title | `220, 220, 220` |
| status | `140, 160, 170` |
| dropdown selected | same as list cursor we already have |
