namespace Tuinet.Widgets;

/// <summary>
/// A hierarchy for a <see cref="TreeView{TSource}"/>: app-owned data addressed by integer node ids the app chooses
/// (an index into its own array, or a key into a dictionary). The tree must not have cycles.
/// </summary>
public interface ITreeSource
{
    /// <summary>Children of <paramref name="node"/>; <see cref="TreeState.Root"/> (-1) is the invisible root.</summary>
    /// <remarks>Called only for the root and expanded nodes, so a lazy source (a file tree) loads a node's children the
    /// first time it is opened. If loading is async, return 0 until the data arrives, then call <see cref="TreeState.Invalidate"/>.</remarks>
    int ChildCount(int node);

    /// <summary>The id of child <paramref name="index"/> of <paramref name="node"/>.</summary>
    int Child(int node, int index);

    /// <summary>Whether <paramref name="node"/> can be expanded (gets an expander). Called for visible rows: keep it cheap.</summary>
    bool HasChildren(int node);

    /// <summary>The parent of <paramref name="node"/> (<see cref="TreeState.Root"/> for a top-level node). Used only by
    /// <see cref="TreeState.Select{TSource}"/> to open the path to a node that isn't shown.</summary>
    int Parent(int node);

    /// <summary>Draw <paramref name="node"/>'s label into a one-row <paramref name="area"/> (after its guides and expander).</summary>
    void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected);
}

/// <summary>
/// Expanded nodes, selection and scroll position of a tree, owned by the app. The visible rows are cached as flat
/// arrays (node, parent row, depth, guide bits), rebuilt only when the tree's shape changes: expanding, collapsing or
/// <see cref="Invalidate"/>. <see cref="Handle{TSource}"/>, <see cref="HandleMouse{TSource}"/>, <see cref="Select{TSource}"/>
/// and render take the source and rebuild first, so a frame costs O(visible rows) however deep the view is scrolled.
/// The selection follows its node across rebuilds; if an ancestor collapsed, the ancestor is selected.
/// </summary>
public sealed class TreeState
{
    /// <summary>The invisible root's id: <see cref="ITreeSource.ChildCount"/>(Root) lists the top-level nodes.</summary>
    public const int Root = -1;

    /// <summary>Guide bits cover this many levels; deeper rows indent but draw blanks where guides would be.</summary>
    internal const int GuideLevels = 64;

    /// <summary>Columns each level indents: a guide sits under the middle of its parent's <c>[-]</c>.</summary>
    internal const int Level = 4;

    private readonly HashSet<int> _expanded = [];
    private readonly HashSet<int> _open = [];   // nodes on the walk's stack, to stop at a cycle
    private int[] _nodes = new int[16];
    private int[] _parents = new int[16];   // row of the parent, -1 for top-level rows
    private int[] _depths = new int[16];
    private ulong[] _lastMask = new ulong[16];   // bit d: the row's ancestor (or the row itself) at depth d is a last child
    private bool[] _last = new bool[16];          // the row itself is a last child (at any depth)
    private Frame[] _stack = new Frame[8];
    private int[] _path = new int[8];
    private int _count;
    private bool _dirty = true;

    // Where the rows were drawn at the last render, for hits on the expander.
    private int _rowX;
    private int _levels;
    private int _expandedWidth;
    private int _collapsedWidth;

    /// <summary>Selected row, scroll position, wheel and scrollbar: rows are list items.</summary>
    public ListState List;

    /// <summary>Visible rows, as of the last rebuild.</summary>
    public int Count => _count;

    /// <summary>The selected node, or -1 for none (an empty tree).</summary>
    public int SelectedNode => NodeAt(List.Selected);

    /// <summary>The node on visible row <paramref name="row"/>, or -1.</summary>
    public int NodeAt(int row) => (uint)row < (uint)_count ? _nodes[row] : -1;

    public bool IsExpanded(int node) => _expanded.Contains(node);

    public void Expand(int node) => _dirty |= _expanded.Add(node);

    public void Collapse(int node) => _dirty |= _expanded.Remove(node);

    public void Toggle(int node)
    {
        if (!_expanded.Remove(node))
        {
            _expanded.Add(node);
        }

        _dirty = true;
    }

    /// <summary>The source's data changed (children added, removed or loaded): rebuild the rows before their next use.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>
    /// Select <paramref name="node"/>, expanding its ancestors (found with <see cref="ITreeSource.Parent"/>) so it shows.
    /// Render scrolls to it.
    /// </summary>
    public void Select<TSource>(int node, TSource source)
        where TSource : ITreeSource, allows ref struct
    {
        for (int parent = source.Parent(node); parent != Root; parent = source.Parent(parent))
        {
            Expand(parent);
        }

        Rebuild(source);
        int row = IndexOf(node);
        if (row >= 0)
        {
            List.Selected = row;
        }
    }

    /// <summary>
    /// Up/Down (or k/j), PageUp/PageDown, Home/End move. Right (or l) expands a node, or steps into its first child
    /// when it is open; Left (or h) collapses a node, or steps to its parent. Space toggles a node with children.
    /// Returns false for keys it does not use, including Enter (open or activate is the app's) and Space on a leaf.
    /// </summary>
    public bool Handle<TSource>(KeyEvent key, TSource source)
        where TSource : ITreeSource, allows ref struct
    {
        Rebuild(source);
        int row = List.Selected;
        int node = NodeAt(row);
        if (key.Is(KeyCode.Down) || key.IsChar('j'))
        {
            List.Next(_count);
        }
        else if (key.Is(KeyCode.Up) || key.IsChar('k'))
        {
            List.Previous(_count);
        }
        else if (key.Is(KeyCode.PageDown))
        {
            List.PageDown(_count);
        }
        else if (key.Is(KeyCode.PageUp))
        {
            List.PageUp(_count);
        }
        else if (key.Is(KeyCode.Home))
        {
            List.First(_count);
        }
        else if (key.Is(KeyCode.End))
        {
            List.Last(_count);
        }
        else if (key.Is(KeyCode.Right) || key.IsChar('l'))
        {
            if ((uint)row >= (uint)_count || !source.HasChildren(node))
            {
                return false;
            }

            if (!IsExpanded(node))
            {
                Expand(node);
                Rebuild(source);
            }
            else if (row + 1 < _count && _parents[row + 1] == row)
            {
                List.Selected = row + 1;
            }
        }
        else if (key.Is(KeyCode.Left) || key.IsChar('h'))
        {
            if ((uint)row >= (uint)_count)
            {
                return false;
            }

            if (IsExpanded(node) && source.HasChildren(node))
            {
                Collapse(node);
                Rebuild(source);
            }
            else if (_parents[row] >= 0)
            {
                List.Selected = _parents[row];
            }
            else
            {
                return false;
            }
        }
        else if (key.IsChar(' ') && (uint)row < (uint)_count && source.HasChildren(node))
        {
            Toggle(node);
            Rebuild(source);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// A click on a node's expander toggles it (and selects it); a click elsewhere on a row selects it; the wheel and
    /// the scrollbar scroll, as in a list. Returns whether the event was used.
    /// </summary>
    public bool HandleMouse<TSource>(MouseEvent ev, TSource source, int wheelRows = 3)
        where TSource : ITreeSource, allows ref struct
    {
        Rebuild(source);
        if (ev.IsClick)
        {
            int row = List.RowAt(ev.X, ev.Y, _count);
            int node = NodeAt(row);
            if (row >= 0 && source.HasChildren(node))
            {
                int x = _rowX + Math.Min(_depths[row], _levels) * Level;
                if (ev.X >= x && ev.X < x + (IsExpanded(node) ? _expandedWidth : _collapsedWidth))
                {
                    List.Selected = row;
                    Toggle(node);
                    Rebuild(source);
                    return true;
                }
            }
        }

        return List.HandleMouse(ev, _count, wheelRows);
    }

    internal int DepthAt(int row) => _depths[row];

    internal ulong LastMaskAt(int row) => _lastMask[row];

    internal bool IsLastAt(int row) => _last[row];

    internal int ParentRowAt(int row) => _parents[row];

    /// <summary>Render records where rows start and how many levels indent, for <see cref="HandleMouse{TSource}"/>.</summary>
    internal void SetLayout(int rowX, int levels, int expandedWidth, int collapsedWidth)
    {
        _rowX = rowX;
        _levels = levels;
        _expandedWidth = expandedWidth;
        _collapsedWidth = collapsedWidth;
    }

    /// <summary>
    /// Flatten the visible tree into the row arrays if its shape changed: an iterative depth-first walk with a reused
    /// stack, so a deep tree can't overflow the call stack. Then select the same node again, or its nearest ancestor.
    /// </summary>
    internal void Rebuild<TSource>(TSource source)
        where TSource : ITreeSource, allows ref struct
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;

        // The selected node and its ancestors, from the old rows: the first one still shown is selected again.
        int selected = List.Selected;
        int pathLength = 0;
        for (int row = (uint)selected < (uint)_count ? selected : -1; row >= 0; row = _parents[row])
        {
            Grow(ref _path, pathLength + 1);
            _path[pathLength++] = _nodes[row];
        }

        _count = 0;
        int depth = 0;   // frames on the stack; frame d lists the children at depth d
        _stack[0] = new Frame(Root, source.ChildCount(Root), -1, 0);
        while (depth >= 0)
        {
            ref Frame frame = ref _stack[depth];
            if (frame.Next >= frame.Count)
            {
                _open.Remove(frame.Node);
                depth--;
                continue;
            }

            int index = frame.Next++;
            int node = source.Child(frame.Node, index);
            ulong mask = frame.Mask | (index == frame.Count - 1 && depth < GuideLevels ? 1UL << depth : 0);
            int row = Add(node, frame.Row, depth, mask, index == frame.Count - 1);
            // A node already open above itself is a cycle in the source: don't walk into it again.
            if (_expanded.Contains(node) && !_open.Contains(node))
            {
                int children = source.ChildCount(node);
                if (children > 0)
                {
                    Grow(ref _stack, depth + 2);
                    _stack[++depth] = new Frame(node, children, row, mask);
                    _open.Add(node);
                }
            }
        }

        List.Selected = _count == 0 ? -1 : Math.Clamp(selected, 0, _count - 1);
        for (int i = 0; i < pathLength; i++)
        {
            int row = IndexOf(_path[i]);
            if (row >= 0)
            {
                List.Selected = row;
                break;
            }
        }
    }

    private int Add(int node, int parent, int depth, ulong mask, bool last)
    {
        if (_count == _nodes.Length)
        {
            int size = _count * 2;
            Array.Resize(ref _nodes, size);
            Array.Resize(ref _parents, size);
            Array.Resize(ref _depths, size);
            Array.Resize(ref _lastMask, size);
            Array.Resize(ref _last, size);
        }

        _nodes[_count] = node;
        _parents[_count] = parent;
        _depths[_count] = depth;
        _lastMask[_count] = mask;
        _last[_count] = last;
        return _count++;
    }

    private int IndexOf(int node) => Array.IndexOf(_nodes, node, 0, _count);

    private static void Grow<T>(ref T[] array, int size)
    {
        if (array.Length < size)
        {
            Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }
    }

    /// <summary>A node being walked: its children Next..Count-1 are still to come.</summary>
    private struct Frame(int node, int count, int row, ulong mask)
    {
        public readonly int Node = node;
        public readonly int Count = count;
        public readonly int Row = row;
        public readonly ulong Mask = mask;
        public int Next;
    }
}

/// <summary>
/// A scrolling, selectable tree over any <see cref="ITreeSource"/>. Each row is its guides (<c>├─</c>, <c>└─</c>,
/// <c>│</c>), an expander for nodes with children (<c>[+]</c> collapsed, <c>[-]</c> expanded), then the label. Only
/// visible rows are drawn. Selection, the highlight symbol and the scrollbar work as in <see cref="ListView{TSource}"/>.
/// </summary>
public readonly ref struct TreeView<TSource> : IStatefulWidget<TreeState>
    where TSource : ITreeSource, allows ref struct
{
    /// <summary>Indentation stops growing once a label would get fewer columns than this.</summary>
    internal const int MinLabelWidth = 8;

    private readonly TSource _source;

    public TreeView(TSource source) => _source = source;

    /// <summary>Draw guide lines from each node to its children.</summary>
    public bool Guides { get; init; } = true;

    public Style GuideStyle { get; init; }

    /// <summary>Before an expanded node's label.</summary>
    public ReadOnlySpan<char> ExpandedSymbol { get; init; } = "[-] ";

    /// <summary>Before a collapsed node's label.</summary>
    public ReadOnlySpan<char> CollapsedSymbol { get; init; } = "[+] ";

    /// <summary>Before a leaf's label (none by default; e.g. four spaces to line leaves up with their siblings' labels).</summary>
    public ReadOnlySpan<char> LeafSymbol { get; init; }

    /// <summary>Layered over the expander.</summary>
    public Style ExpanderStyle { get; init; }

    /// <summary>Layered over the selected row after it renders.</summary>
    public Style SelectedStyle { get; init; }

    /// <summary>Drawn before the selected row (e.g. "> "); other rows are indented by its width.</summary>
    public ReadOnlySpan<char> HighlightSymbol { get; init; }

    public Style HighlightSymbolStyle { get; init; }

    /// <summary>Draw a scrollbar in the rightmost column; rows are one column narrower while it shows.</summary>
    public ScrollbarMode Scrollbar { get; init; }

    public Style ScrollbarThumbStyle { get; init; }
    public Style ScrollbarTrackStyle { get; init; }

    public void Render(Rect area, CellBuffer buffer, ref TreeState state)
    {
        state.Rebuild(_source);
        buffer.Render(new ListView<Rows>(new Rows(this, state))
        {
            SelectedStyle = SelectedStyle,
            HighlightSymbol = HighlightSymbol,
            HighlightSymbolStyle = HighlightSymbolStyle,
            Scrollbar = Scrollbar,
            ScrollbarThumbStyle = ScrollbarThumbStyle,
            ScrollbarTrackStyle = ScrollbarTrackStyle,
        }, area, ref state.List);
    }

    /// <summary>Draw row <paramref name="row"/>: guides, expander, label.</summary>
    private void RenderRow(TreeState state, int row, Rect area, CellBuffer buffer, bool selected)
    {
        int node = state.NodeAt(row);
        int depth = state.DepthAt(row);
        ulong mask = state.LastMaskAt(row);
        int expandedWidth = TextWidth.Of(ExpandedSymbol);
        int collapsedWidth = TextWidth.Of(CollapsedSymbol);
        int levels = Math.Max(0, (area.Width - Math.Max(expandedWidth, collapsedWidth) - MinLabelWidth) / TreeState.Level);
        state.SetLayout(area.X, levels, expandedWidth, collapsedWidth);

        levels = Math.Min(depth, levels);
        int y = area.Y;
        int x = area.X;
        if (Guides)
        {
            for (int level = 0; level < levels; level++)
            {
                // The last column is the node's own connector; the ones before it continue its ancestors' lines.
                if (level == levels - 1)
                {
                    buffer.SetString(x + 1, y, state.IsLastAt(row) ? "└─" : "├─", GuideStyle, area.Right - x - 1);
                }
                else if (level + 1 < TreeState.GuideLevels && (mask >> (level + 1) & 1) == 0)
                {
                    buffer.SetString(x + 1, y, "│", GuideStyle, area.Right - x - 1);
                }

                x += TreeState.Level;
            }
        }
        else
        {
            x += levels * TreeState.Level;
        }

        if (x >= area.Right)
        {
            return;
        }

        ReadOnlySpan<char> expander = !_source.HasChildren(node) ? LeafSymbol
            : state.IsExpanded(node) ? ExpandedSymbol : CollapsedSymbol;
        if (!expander.IsEmpty)
        {
            buffer.SetString(x, y, expander, ExpanderStyle, area.Right - x);
            x += TextWidth.Of(expander);
        }

        if (x < area.Right)
        {
            _source.RenderLabel(node, new Rect(x, y, area.Right - x, 1), buffer, selected);
        }
    }

    /// <summary>The visible rows as list items, so the tree draws (and scrolls, and hit-tests) through <see cref="ListView{TSource}"/>.</summary>
    private readonly ref struct Rows(TreeView<TSource> view, TreeState state) : IListSource
    {
        private readonly TreeView<TSource> _view = view;

        public int Count => state.Count;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected) =>
            _view.RenderRow(state, index, area, buffer, selected);
    }
}
