# 09e · Tree view

**Type:** missing · **Effort:** M–L · **Priority:** 5 · **Depends on:** 09b (Showcase Groups page)

## Problem
Data with a hierarchy (file trees, grouped items, JSON, outlines) can only be shown flattened by hand. Without
a cache, finding which rows are visible at a scroll offset means walking the tree from the root every frame,
which costs O(offset) and gets slower deeper into a big tree.

## Design
### Source (app-owned data, integer node ids)
```csharp
public interface ITreeSource
{
    int ChildCount(int node);                 // node −1 is the invisible root
    int Child(int node, int index);           // id of a child
    bool HasChildren(int node);               // cheap check for the ▸ glyph; lazy sources never list a collapsed node
    void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected);
}
```
- Ids are plain `int`s that the app chooses: an index into its own array, or a key into a dictionary. Using
  ints keeps the state free of generics and allocations.
- **Lazy children:** `ChildCount` and `Child` are called only for expanded nodes. A file tree reads a
  directory the first time it's expanded. If loading is async, the source returns 0 at first and the app
  calls `Invalidate` when the data arrives.

### `TreeState` (class: it owns growable arrays, like `TextAreaState`)
```csharp
public sealed class TreeState
{
    public ListState List;                       // selected row, scroll, wheel, scrollbar: reused as is
    public int SelectedNode { get; }
    public bool IsExpanded(int node);
    public void Expand(int node); public void Collapse(int node); public void Toggle(int node);
    public void Invalidate();                    // the source changed: rebuild before the next use
    public bool Handle<TSource>(KeyEvent key, TSource source) where TSource : ITreeSource, allows ref struct;
    public bool HandleMouse<TSource>(MouseEvent ev, TSource source) where TSource : ITreeSource, allows ref struct;
    public void Select(int node);                // expands its ancestors if needed (needs the parent links)
}
```
- **Cached flat rows.** Parallel arrays `int[] node`, `int[] parent`, `ushort[] depth` and
  `ulong[] lastMask`, where bit d means "the ancestor at depth d is the last child", for `├─`/`└─`/`│ `/
  blank guides. They're rebuilt by an iterative DFS with a reused stack array, so deep trees can't overflow
  the stack. The arrays only grow (doubling) and are never trimmed, so steady frames allocate nothing.
- **When rows rebuild:** expand, collapse and `Invalidate` mark the rows dirty. `Handle`, `HandleMouse` and
  `Render` all take the source and rebuild before reading rows. Two Right presses in one input batch
  (expand, then step into the first child) therefore see correct rows. Rendering costs O(visible rows); a
  rebuild costs O(expanded rows) and happens only when the tree's shape changes.
- **Selection follows the node, not the row.** After a rebuild, the same node is selected again. If its
  ancestor collapsed, the ancestor is selected instead. `List.Offset` is kept, so the view doesn't jump.
- **Expanded set:** a `HashSet<int>`, allocated once. Adding to it doesn't allocate after it has grown.
- **Keys:** Up/Down/j/k, PageUp/PageDown, Home/End (through `List`). Right or l expands a node, or moves to
  its first child if it's already open. Left or h collapses a node, or moves to its parent. Space toggles.
  Enter is left to the app (open or activate), so `Handle` returns false for it.
- **Mouse:** a click selects; a click on the expander glyph toggles; wheel and scrollbar work through `List`.
- **Depth limit:** guides cover 64 levels (`ulong`). Deeper rows still indent but draw blanks where the
  guides would be. Indentation stops growing once only 8 cells of label width would be left.

### `TreeView<TSource>`
```csharp
public readonly ref struct TreeView<TSource> : IStatefulWidget<TreeState>
    where TSource : ITreeSource, allows ref struct
{
    public bool Guides { get; init; } = true;
    public Style GuideStyle { get; init; }
    public ReadOnlySpan<char> ExpandedSymbol { get; init; } = "▾ ";
    public ReadOnlySpan<char> CollapsedSymbol { get; init; } = "▸ ";
    public Style SelectedStyle, HighlightSymbol, HighlightSymbolStyle, Scrollbar… { get; init; }  // same as ListView
}
```
Each row is drawn as guide prefix, expander, then label. The row-drawing and scrollbar code is shared with
`ListView` (an internal helper takes the row-drawing callback as a struct, so there's no delegate allocation).

## Showcase
- **Groups** tab: kind → priority → items, with counts in the group labels (`feature (4)`). Node ids:
  items are 0…19, kind groups are 1000 + k, priority groups are 2000 + k·10 + p.
- Selecting an item node selects it in the List tab too, and the details panel follows. Enter on an item
  edits it, and Enter on a group toggles it. Expand and collapse work with the keyboard and with clicks.
- Kind groups start expanded and priority groups start collapsed, so both glyphs and lazy children
  show up on first open.

## Files
`Widgets/TreeView.cs` (new), `Widgets/ListView.cs` (shared row-drawing helper), Showcase
`ShowcaseApp.cs` + `ItemTree.cs` (source), `PublicAPI.Unshipped.txt`, README, CHANGELOG.

## Tests
- Flatten: the order, depth and `lastMask` for a known tree, and guide rendering line by line
  (`├─`, `└─`, `│ `, blank under a last child).
- Lazy: a spy source shows that `ChildCount`/`Child` are never called for collapsed nodes, and that
  `Invalidate` picks up new children.
- Keys: Right expands, then goes into the first child. Left collapses, then goes to the parent. Left at the
  root does nothing, Space toggles, and `Handle` returns false for Enter.
- Selection: collapsing an ancestor selects it; `Select(node)` expands the path; the offset stays the same
  after a rebuild.
- Mouse: a click on the expander toggles, a click on the label selects, a click with a scroll offset hits the right node.
- Depth 100 doesn't overflow the stack and guides are capped. Wide-glyph labels are cut. An empty tree gives
  selection −1.
- Benchmark: 1M-node tree, 100k rows expanded, render time is flat at offset 0, 50k and 99k. Rebuild time is
  reported separately.
- AllocationTests: steady frames, and scrolling, allocate 0 B. A Showcase test covers the Groups tab: open a
  group, select an item, check that the details follow.

## Non-goals
Multi-select, drag and drop, inline rename, and columns per node (a tree table).

## Status: implemented

- Expanders are `[+]` (collapsed) and `[-]` (expanded) by default, as asked; `▸`/`▾` are one property away.
  Each level indents 4 columns, so a child's guide sits under the middle of its parent's `[-]`. Leaves have no
  expander (`LeafSymbol`, empty by default).
- `ITreeSource` gained `Parent(node)`: `Select` needs it to open the path to a node that isn't shown, and the rows
  only know the parents of visible nodes.
- No new shared helper in `ListView`: `TreeView` draws through `ListView` with a ref-struct adapter as its
  `IListSource`. Selection, highlight symbol, scrollbar, wheel and drag are the list's own code.
- Rows also keep a per-row "last child" flag, so a node's own connector (`└─` or `├─`) is right at any depth.
  Ancestor lines past 64 levels are blanks (the `ulong` guide bits). A cycle in the source is walked once, not
  forever (a set of the nodes open on the walk's stack).
- Showcase: the tab order is List · Stats · Groups (alt+3), so existing keys keep their pages. Enter folds a group or
  edits an item, Space toggles an item, a double-click edits. The tree is invalidated after a sort or a saved
  edit. The sample data puts exactly one item in each kind × priority group.
- 1M-node tree, 109,000 rows open, 200×60: a frame takes 6.8 / 7.0 / 6.6 µs at rows 0 / 50,000 / 99,000; a
  rebuild takes 0.47 ms; both allocate 0 B.
