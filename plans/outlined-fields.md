# Outlined fields (jiratui Input)

The options form currently paints **label on the left + filled cyan bar**. That was the wrong reading of [jiratui’s home form](https://jiratui.sh/assets/img/gallery/screenshot-jiratui-home.png).

Real look: **outlined box**, rounded corners, **label sitting on the top border**, value **inside** the box, required mark **`( *)`** on the bottom-right of the border.

## Target (one field)

```
╭─ Organization ─────────────────────────────╮
│ acme                                       │
╰──────────────────────────────────── (*) ───╯
```

PAT and Project are the same chrome. Project keeps `▾` on the right of the **inner** value row. Required: Organization, PAT, Project — all get `( *)`.

## vs today

| | Today | Target |
|---|---|---|
| Chrome | no box; `Fill` bar | `╭╮╰╯` outline, 3 rows |
| Label | left of the bar, `Organization:` | on the **top** border, no colon |
| Value | on the filled bar | inside the box, dialog background |
| Focus | whole bar cyan | **border + label** cyan; interior stays screen bg |
| Required | none | `( *)` interrupting the bottom border, right side |
| Height | 1 row per field | 3 rows per field |

## Paint (OptionsDialog only)

Do **not** add a kernel widget. `CellBuffer.DrawBox` stays the window chrome (`┌┐└┘`). Field frames are painted in the dialog.

Each field is a `Rect` of height 3, full inner dialog width (`dialog.X+2` … `dialog.Width-4`):

1. **Top:** `╭` `─` space `Label` space `─…` `╮`. Label uses focus/idle border style.
2. **Middle:** `│` + value (existing caret/scroll/`DisplayRune` / project text + `▾`) + `│`. Interior style = `Theme.Screen` (not `FieldFocus` fill). Caret still inverts the glyph cell.
3. **Bottom:** `╰` `─…` space `( *)` space `─` `╯` if required, else plain `╰─…╯`.

Focus style: `Theme.FieldFocus` **foreground** on the border and label (cyan ink, screen bg) — not a filled bar. Idle: `Theme.Label`. Value text: `Theme.Title`. Caret: `Theme.Caret` as now.

Stack:

```
y+0..2   Organization
y+3..5   PAT
y+6..8   Project
y+9      dropdown (when open; clip under project box, above buttons)
buttons / status unchanged
```

Grow `DialogRect` max height from 14 → **18** so three 3-row fields + buttons + status fit.

Dropdown: same width as the field inner; starts on the row after Project’s bottom border.

## Input

Unchanged. Tab / enter / caret / mask / Save / Close. Only paint changes. `Field` is untouched.

## Tests

- Label `Organization` is on the same row as `╭` / `─` (top border), not a left-hand column.
- Typed `acme` is on the row **below** that label.
- `( *)` appears on the PAT/org/project bottom border.
- Focused field: border cells use cyan **foreground**, not a full-width cyan **background** bar.
- Project still shows `▾` inside the box.

## Files

| Touch | Role |
|---|---|
| `OptionsDialog.cs` | replace `PaintField` / `PaintProject` / `Bar` with outlined frames; taller dialog |
| `Theme.cs` | only if we need a `FieldBorder` style; prefer reusing `FieldFocus` fg + `Screen` bg |
| `OptionsDialogTests.cs` | layout asserts above |

## Out of scope

Rounded `DrawBox` in the kernel, button restyle, dialog title restyle, mouse, validation beyond the visual `( *)`.
