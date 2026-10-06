# 09d · Menu, fuzzy matching and the command palette pattern

**Type:** missing · **Effort:** M · **Priority:** 5 · **Depends on:** 09a (Popup, `PlaceNear`), 09b (tab commands)

## Problem
Apps have no context menus or action lists, and there's no allocation-free way to filter a list by what was
typed. A command palette, which keyboard-first users expect, needs both.

## Design
### `Menu` (library)
```csharp
public readonly record struct MenuItem(string Label, string Shortcut = "", bool Enabled = true)
{
    public static MenuItem Separator { get; }   // drawn as ├───┤ joined to the border
    public bool IsSeparator { get; }
}

public enum MenuResult : byte { Unhandled, Handled, Activated, Cancelled }

public struct MenuState
{
    public int Selected { get; }                 // never a separator or a disabled item
    public Rect Area { get; private set; }
    public void Reset(ReadOnlySpan<MenuItem> items);  // highlights the first enabled item
    public MenuResult Handle(KeyEvent key, ReadOnlySpan<MenuItem> items);
    public MenuResult HandleMouse(MouseEvent ev, ReadOnlySpan<MenuItem> items);
}

public readonly ref struct Menu : IStatefulWidget<MenuState>
{
    public Menu(ReadOnlySpan<MenuItem> items);
    public static Size Measure(ReadOnlySpan<MenuItem> items);   // border + label + gap + shortcut
    public Popup Popup { get; init; }            // border, shadow, fill (09a)
    public Style SelectedStyle, ShortcutStyle, DisabledStyle, MnemonicStyle { get; init; }
}
```
- **Keys:** Up/Down (and j/k) skip separators and disabled items and wrap; Home/End; Enter/Space activates;
  Esc cancels. A mnemonic letter, marked `&` in the label (`"&Edit"`, drawn underlined), activates that item
  directly.
- **Mouse:** a click activates; with `TerminalOptions.MouseMotion` on, a pointer move over an item
  highlights it. A click outside returns `Cancelled`, and like `Dropdown` the caller can still act on that
  click.
- **Placement:** the app calls `buffer.Area.PlaceNear(anchor, Menu.Measure(items))`, using the selected row
  for the keyboard or a 0×0 point at the pointer.
- **Scrolling:** a menu taller than the space it gets scrolls, reusing the follow logic from `ListState`.
- **Stateless items:** the `MenuItem[]` is app-owned and built once. To toggle `Enabled`, the app swaps in
  an item with `with { Enabled = … }` before rendering. That's a struct copy, so no allocation.

### `Fuzzy` (library)
```csharp
public static class Fuzzy
{
    public static int Score(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text);                   // −1 if no match
    public static int Score(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text, Span<int> matched); // + char indices
}
```
- **Algorithm:** fzf "v1" style. A forward scan finds the first subsequence match, then a backward scan from
  its end tightens it to the shortest window. It runs in O(n), allocates nothing, and needs no scratch
  matrix. Scoring rewards word starts (after a space, `-`, `_`, `/`, `.`), camelCase humps, runs of
  consecutive matches and a match at position 0. It penalizes gaps and a late start.
- **Case:** ignored, unless the pattern has an uppercase letter (smart case, as in fzf/vim). Folding uses
  `char.ToLowerInvariant` per UTF-16 unit, which is enough for command names. Matched indices are UTF-16
  offsets, used to highlight matches with `StyledTextBuilder`.
- An empty pattern matches everything with score 0, so callers keep the original order.

### Command palette (Showcase pattern, not a widget)
A `CommandPalette` class in the sample, about 150 lines:
- Ctrl+P (and `:`) opens a `Popup` at the top center: a `TextInput` row, a separator, and a `ListView` of matches.
- **Filtering runs only when the query changes, not every frame.** Score every command into a preallocated
  `int[] keys`/`int[] order`, then `Array.Sort(keys, order)`, with the key packing score and original index
  so ties stay stable. A command can't be renamed while the palette is open, so the order is reused across frames.
- **Rows:** the name with matched characters highlighted (its `Score(..., matched)` is recomputed only for
  visible rows, into `stackalloc`), the shortcut right-aligned and dimmed.
- **Keys:** Up/Down move, Enter runs, Esc closes, typing edits the query, and a click runs a row.
- Commands are `record Command(string Name, string Shortcut, Action<ShowcaseApp> Run)`, built once.

## Showcase
- **Context menu:** a right-click on a row, or the menu key `m`, opens a `Menu` at the pointer or under the
  selected row. Items: Edit · Toggle enabled · Copy name, a separator, Sort by name/kind/priority/owner,
  another separator, then Delete, shown disabled to demonstrate the disabled style.
- **Command palette:** Ctrl+P lists every action in the app: the menu items, each tab switch, Progress dialog,
  Quit. It's the one place to discover everything the app can do.

## Files
`Widgets/Menu.cs` (new), `Fuzzy.cs` (new, in the `Tuinet` namespace, since it's text matching rather than a
widget), Showcase `CommandPalette.cs` (new) and `ShowcaseApp.cs`, `PublicAPI.Unshipped.txt`, README, CHANGELOG.

## Tests
- Menu: navigation skips separators and disabled items and wraps; an all-disabled menu gives −1 and doesn't
  hang; mnemonics; Enter/Esc results; click, motion-hover and click-outside; `Measure` with wide glyphs; separators join the border.
- Fuzzy: no match −1; subsequence order matters; word-start beats mid-word (`"ep"` → **E**dit **P**alette over
  d**e**e**p**); consecutive beats scattered; smart case; matched indices are right after the backward
  tightening; an empty pattern; a pattern longer than the text.
- Fuzzy benchmark: 10k commands × 3-character pattern, in ns per call and 0 B.
- Showcase: right-click opens the menu, choosing Sort sorts; Ctrl+P, type "prog", Enter opens the progress
  dialog; typing filters and highlights. AllocationTests: steady frames with an open menu and an open palette
  allocate 0 B, re-filtering on a keypress allocates 0 B, and only building the command list allocates.

## Non-goals
A menu bar, submenus and a library `CommandPalette` widget. Any of these can be added on top later.
