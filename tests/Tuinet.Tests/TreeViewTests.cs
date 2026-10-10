using Tuinet.Widgets;

namespace Tuinet.Tests;

public class TreeViewTests
{
    private static readonly Style Selected = new(Color.Black, Color.White);
    private static readonly Style Guide = new(Color.Blue, Color.Default);

    /// <summary>
    /// 1 ─┬─ 10 ── 100
    ///    └─ 11
    /// 2 ─── 20
    /// </summary>
    private static Tree Sample() => new()
    {
        [TreeState.Root] = [1, 2],
        [1] = [10, 11],
        [10] = [100],
        [2] = [20],
    };

    private static TreeState Expanded(params int[] nodes)
    {
        var state = new TreeState();
        foreach (int node in nodes)
        {
            state.Expand(node);
        }

        return state;
    }

    private static string[] Draw(Tree tree, TreeState state, int width = 20, int height = 8, bool guides = true)
    {
        var buffer = new CellBuffer(width, height);
        buffer.Render(new TreeView<Tree>(tree) { Guides = guides, GuideStyle = Guide, SelectedStyle = Selected }, buffer.Area, ref state);
        return [.. Enumerable.Range(0, height).Select(y => buffer.RowText(y).TrimEnd())];
    }

    [Fact]
    public void Rows_are_the_expanded_tree_depth_first()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1, 10, 2);
        state.Rebuild(tree);
        Assert.Equal(new[] { 1, 10, 100, 11, 2, 20 }, Enumerable.Range(0, state.Count).Select(state.NodeAt));
        Assert.Equal(new[] { 0, 1, 2, 1, 0, 1 }, Enumerable.Range(0, state.Count).Select(state.DepthAt));
        Assert.Equal(new[] { -1, 0, 1, 0, -1, 4 }, Enumerable.Range(0, state.Count).Select(state.ParentRowAt));

        // Bit d: the row's ancestor (or itself) at depth d is the last child. 100 is last under 10, but 10 isn't under 1.
        Assert.Equal(new ulong[] { 0, 0b00, 0b100, 0b10, 0b1, 0b11 }, Enumerable.Range(0, state.Count).Select(state.LastMaskAt));
    }

    [Fact]
    public void Guides_join_nodes_to_their_children_and_expanders_show_the_state()
    {
        string[] rows = Draw(Sample(), Expanded(1, 10));
        Assert.Equal(
            new[]
            {
                "[-] n1",
                " ├─ [-] n10",
                " │   └─ n100",       // the line continues past 10: 11 comes after it
                " └─ n11",
                "[+] n2",
                "", "", "",
            },
            rows);
    }

    [Fact]
    public void Guides_draw_only_lines_over_the_background()
    {
        var buffer = new CellBuffer(20, 4);
        var background = new Style(Color.Default, Color.Rgb(10, 20, 30));
        buffer.Fill(buffer.Area, background);
        var state = Expanded(1, 10);
        buffer.Render(new TreeView<Tree>(Sample()) { GuideStyle = Guide }, buffer.Area, ref state);
        Assert.Equal(background.Bg, buffer[0, 2].Style.Bg);   // blanks between guides keep what was there
        Assert.Equal(Guide.Fg, buffer[1, 2].Style.Fg);
        Assert.Equal(background.Bg, buffer[1, 2].Style.Bg);   // guides layer over it
    }

    [Fact]
    public void Without_guides_rows_only_indent()
    {
        string[] rows = Draw(Sample(), Expanded(1, 10), guides: false);
        Assert.Equal(new[] { "[-] n1", "    [-] n10", "        n100", "    n11", "[+] n2" }, rows[..5]);
    }

    [Fact]
    public void Other_symbols_and_a_leaf_symbol_can_be_set()
    {
        var buffer = new CellBuffer(20, 4);
        var state = Expanded(1);
        buffer.Render(new TreeView<Tree>(Sample()) { ExpandedSymbol = "▾ ", CollapsedSymbol = "▸ ", LeafSymbol = "· " }, buffer.Area, ref state);
        Assert.Equal("▾ n1", buffer.RowText(0).TrimEnd());
        Assert.Equal(" ├─ ▸ n10", buffer.RowText(1).TrimEnd());
        Assert.Equal(" └─ · n11", buffer.RowText(2).TrimEnd());
    }

    [Fact]
    public void Children_of_collapsed_nodes_are_never_asked_for()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1);
        Draw(tree, state);
        Assert.Equal(new[] { TreeState.Root, 1 }, tree.Listed.Order());   // not 10 or 2: they are collapsed

        // A lazy source loads 2's children when it opens, and Invalidate picks up what arrives later.
        tree[2] = [];
        state.Expand(2);
        Assert.DoesNotContain("n20", string.Join('\n', Draw(tree, state)));
        tree[2] = [20, 21];
        Assert.DoesNotContain("n21", string.Join('\n', Draw(tree, state)));   // cached until invalidated
        state.Invalidate();
        Assert.Contains(" └─ n21", Draw(tree, state));
    }

    [Fact]
    public void Steady_frames_do_not_rebuild_the_rows()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1, 10, 2);
        Draw(tree, state);
        tree.Listed.Clear();
        Draw(tree, state);
        state.Handle(new KeyEvent(KeyCode.Down), tree);
        Assert.Empty(tree.Listed);
    }

    [Fact]
    public void Right_expands_then_steps_into_the_first_child()
    {
        Tree tree = Sample();
        var state = new TreeState();
        Assert.True(state.Handle(new KeyEvent(KeyCode.Right), tree));
        Assert.True(state.IsExpanded(1));
        Assert.Equal(1, state.SelectedNode);
        Assert.True(state.Handle(KeyEvent.Char('l'), tree));
        Assert.Equal(10, state.SelectedNode);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Right), tree));   // 10 opens
        Assert.True(state.Handle(new KeyEvent(KeyCode.Right), tree));   // into 100
        Assert.Equal(100, state.SelectedNode);
        Assert.False(state.Handle(new KeyEvent(KeyCode.Right), tree));  // a leaf: not used
    }

    [Fact]
    public void Left_collapses_then_steps_to_the_parent()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1, 10);
        state.Select(100, tree);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Left), tree));    // a leaf: to its parent
        Assert.Equal(10, state.SelectedNode);
        Assert.True(state.Handle(KeyEvent.Char('h'), tree));            // open: collapse
        Assert.False(state.IsExpanded(10));
        Assert.Equal(10, state.SelectedNode);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Left), tree));    // closed: to the parent
        Assert.Equal(1, state.SelectedNode);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Left), tree));    // collapse 1
        Assert.False(state.Handle(new KeyEvent(KeyCode.Left), tree));   // a closed top-level node: nothing to do
    }

    [Fact]
    public void Space_toggles_and_enter_is_left_to_the_app()
    {
        Tree tree = Sample();
        var state = new TreeState();
        Assert.True(state.Handle(KeyEvent.Char(' '), tree));
        Assert.True(state.IsExpanded(1));
        Assert.True(state.Handle(KeyEvent.Char(' '), tree));
        Assert.False(state.IsExpanded(1));
        Assert.False(state.Handle(new KeyEvent(KeyCode.Enter), tree));
        state.Handle(KeyEvent.Char(' '), tree);
        state.Handle(new KeyEvent(KeyCode.Down), tree);
        Assert.Equal(10, state.SelectedNode);
        state.Handle(new KeyEvent(KeyCode.Down), tree);
        Assert.Equal(11, state.SelectedNode);
        Assert.False(state.Handle(KeyEvent.Char(' '), tree));            // a leaf: the app's (e.g. check it)
    }

    [Fact]
    public void Movement_keys_go_through_the_list()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1, 10, 2);
        state.Handle(new KeyEvent(KeyCode.End), tree);
        Assert.Equal(20, state.SelectedNode);
        state.Handle(KeyEvent.Char('k'), tree);
        Assert.Equal(2, state.SelectedNode);
        state.Handle(new KeyEvent(KeyCode.Home), tree);
        Assert.Equal(1, state.SelectedNode);
        state.Handle(KeyEvent.Char('j'), tree);
        Assert.Equal(10, state.SelectedNode);
    }

    [Fact]
    public void Collapsing_an_ancestor_selects_it()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1, 10);
        state.Select(100, tree);
        state.Collapse(1);
        Draw(tree, state);
        Assert.Equal(1, state.SelectedNode);
    }

    [Fact]
    public void The_selection_follows_its_node_when_rows_above_it_open()
    {
        Tree tree = Sample();
        TreeState state = Expanded(2);
        state.Select(20, tree);
        Assert.Equal(2, state.List.Selected);
        state.Expand(1);
        Draw(tree, state);
        Assert.Equal(20, state.SelectedNode);
        Assert.Equal(4, state.List.Selected);
    }

    [Fact]
    public void Select_opens_the_path_to_a_hidden_node()
    {
        Tree tree = Sample();
        var state = new TreeState();
        state.Select(100, tree);
        Assert.True(state.IsExpanded(1) && state.IsExpanded(10));
        Assert.False(state.IsExpanded(2));
        Assert.Equal(100, state.SelectedNode);
    }

    [Fact]
    public void The_offset_stays_when_rows_rebuild()
    {
        Tree tree = Wide(50);
        var state = new TreeState();
        state.Expand(0);
        state.Select(1300, tree);   // under node 30: rows 0, its ten leaves, 1…30, then 1300 at row 41
        Draw(tree, state, height: 10);
        int offset = state.List.Offset;
        Assert.Equal(32, offset);
        state.Expand(45);   // a node below the view opens
        Draw(tree, state, height: 10);
        Assert.Equal(offset, state.List.Offset);
        Assert.Equal(1300, state.SelectedNode);
    }

    [Fact]
    public void A_click_on_the_expander_toggles_and_a_click_on_the_label_selects()
    {
        Tree tree = Sample();
        TreeState state = Expanded(1);
        Draw(tree, state);                                                // [-] n1 /  ├─ [+] n10 /  └─ n11 / [+] n2
        Assert.True(state.HandleMouse(Click(4, 1), tree));              // 10's "[+]" is columns 4-7
        Assert.True(state.IsExpanded(10));
        Assert.Equal(10, state.SelectedNode);
        Draw(tree, state);
        Assert.True(state.HandleMouse(Click(5, 4), tree));              // the label "n2" (row 4: [+] n2)
        Assert.False(state.IsExpanded(2));
        Assert.Equal(2, state.SelectedNode);
        Assert.True(state.HandleMouse(Click(0, 0), tree));              // 1's "[-]": collapse
        Assert.False(state.IsExpanded(1));
        Assert.Equal(1, state.SelectedNode);
        Assert.False(state.HandleMouse(Click(5, 7), tree));             // below the rows
    }

    [Fact]
    public void A_click_hits_the_right_node_when_scrolled()
    {
        Tree tree = Wide(50);
        var state = new TreeState();
        state.Expand(0);
        state.Select(1040, tree);
        Draw(tree, state, height: 10);
        int offset = state.List.Offset;
        Assert.True(state.HandleMouse(Click(10, 3), tree));
        Assert.Equal(state.NodeAt(offset + 3), state.SelectedNode);

        // The wheel scrolls as in a list.
        Assert.True(state.HandleMouse(new MouseEvent(MouseKind.ScrollUp, MouseButton.None, 5, 5, Modifiers.None), tree));
        Assert.Equal(offset - 3, state.List.Offset);
    }

    [Fact]
    public void A_deep_tree_does_not_overflow_and_indentation_is_capped()
    {
        // A chain 0 → 1 → … → 99999, all open: an iterative walk, not recursion.
        var tree = new Tree { [TreeState.Root] = [0] };
        var state = new TreeState();
        for (int i = 0; i < 99_999; i++)
        {
            tree[i] = [i + 1];
            state.Expand(i);
        }

        state.Select(99_999, tree);
        Assert.Equal(100_000, state.Count);
        Assert.Equal(99_999, state.DepthAt(99_999));
        string[] rows = Draw(tree, state, width: 30, height: 4);

        // 30 columns: a label keeps 8 after the expander, so rows indent at most (30 - 4 - 8) / 4 = 4 levels. The
        // single children are all last children, so only the connectors show.
        Assert.Equal("             └─ n99999", rows[3]);
    }

    [Fact]
    public void Guides_past_64_levels_are_blank()
    {
        var tree = new Tree { [TreeState.Root] = [0, 1000] };
        var state = new TreeState();
        for (int i = 0; i < 69; i++)
        {
            tree[i] = [i + 1, 2000 + i];   // every level has a sibling below: lines all the way down
            state.Expand(i);
        }

        state.Select(69, tree);
        string[] rows = Draw(tree, state, width: 300, height: 1);
        string row = rows[0];
        Assert.Equal('│', row[1 + 4 * 62]);   // the ancestor at depth 63 still has a guide bit
        Assert.Equal(' ', row[1 + 4 * 63]);   // depth 64: no bit, so a blank
        Assert.Equal(' ', row[1 + 4 * 67]);
        Assert.Equal("├─ n69", row[(1 + 4 * 68)..]);   // its own connector still shows
    }

    [Fact]
    public void Wide_labels_are_cut_at_the_edge()
    {
        var tree = new Tree { [TreeState.Root] = [1] };
        tree.Labels[1] = "世界世界世界";
        string[] rows = Draw(tree, new TreeState(), width: 9, height: 1);
        Assert.Equal("世界世界", rows[0]);
    }

    [Fact]
    public void An_empty_tree_selects_nothing()
    {
        var tree = new Tree { [TreeState.Root] = [] };
        var state = new TreeState();
        Draw(tree, state);
        Assert.Equal(-1, state.SelectedNode);
        Assert.Equal(-1, state.List.Selected);
        Assert.False(state.Handle(new KeyEvent(KeyCode.Right), tree));
        Assert.False(state.Handle(new KeyEvent(KeyCode.Left), tree));
        Assert.False(state.Handle(KeyEvent.Char(' '), tree));
    }

    [Fact]
    public void A_cycle_in_the_source_is_not_walked_forever()
    {
        var tree = new Tree { [TreeState.Root] = [1], [1] = [2], [2] = [1] };
        TreeState state = Expanded(1, 2);
        Draw(tree, state);
        Assert.Equal(new[] { 1, 2, 1 }, Enumerable.Range(0, state.Count).Select(state.NodeAt));
    }

    [Fact]
    public void The_selected_row_is_highlighted_with_its_guides()
    {
        var buffer = new CellBuffer(20, 4);
        TreeState state = Expanded(1);
        state.Handle(new KeyEvent(KeyCode.Down), Sample());
        buffer.Render(new TreeView<Tree>(Sample()) { SelectedStyle = Selected, HighlightSymbol = "> " }, buffer.Area, ref state);
        Assert.Equal(">  ├─ [+] n10", buffer.RowText(1).TrimEnd());
        Assert.Equal("  [-] n1", buffer.RowText(0).TrimEnd());
        Assert.Equal(Selected, buffer[19, 1].Style);
    }

    [Fact]
    public void Frames_and_scrolling_allocate_nothing()
    {
        Tree tree = Wide(2000);
        var state = new TreeState();
        state.Expand(0);
        var buffer = new CellBuffer(80, 30);
        var down = new KeyEvent(KeyCode.Down);
        for (int i = 0; i < 50; i++)
        {
            state.Handle(down, tree);
            buffer.Clear();
            buffer.Render(new TreeView<Tree>(tree) { Scrollbar = ScrollbarMode.Auto }, buffer.Area, ref state);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            state.Handle(down, tree);
            state.HandleMouse(new MouseEvent(MouseKind.ScrollDown, MouseButton.None, 5, 5, Modifiers.None), tree);
            buffer.Clear();
            buffer.Render(new TreeView<Tree>(tree) { Scrollbar = ScrollbarMode.Auto }, buffer.Area, ref state);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Top-level nodes 0..count-1, each with ten leaves 1000 + 10·i + j.</summary>
    private static Tree Wide(int count)
    {
        var tree = new Tree { [TreeState.Root] = [.. Enumerable.Range(0, count)] };
        for (int i = 0; i < count; i++)
        {
            tree[i] = [.. Enumerable.Range(0, 10).Select(j => 1000 + 10 * i + j)];
        }

        return tree;
    }

    private static MouseEvent Click(int x, int y) => new(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None);

    /// <summary>
    /// A tree from child lists, labelled "n{id}". A node has children (an expander) when it has a list, even an empty
    /// one (a lazy node not loaded yet). Records which nodes' children were listed.
    /// </summary>
    private sealed class Tree : ITreeSource
    {
        private readonly Dictionary<int, int[]> _children = [];
        private readonly Dictionary<int, int> _parents = [];

        public List<int> Listed { get; } = [];

        public Dictionary<int, string> Labels { get; } = [];

        public int[] this[int node]
        {
            set
            {
                _children[node] = value;
                foreach (int child in value)
                {
                    _parents[child] = node;
                }
            }
        }

        public int ChildCount(int node)
        {
            Listed.Add(node);
            return _children.TryGetValue(node, out int[]? children) ? children.Length : 0;
        }

        public int Child(int node, int index) => _children[node][index];

        public bool HasChildren(int node) => _children.ContainsKey(node);

        public int Parent(int node) => _parents.TryGetValue(node, out int parent) ? parent : TreeState.Root;

        public void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected)
        {
            if (Labels.TryGetValue(node, out string? label))
            {
                buffer.SetString(area.X, area.Y, label, default, area.Width);
                return;
            }

            Span<char> text = stackalloc char[12];
            text[0] = 'n';
            node.TryFormat(text[1..], out int length);
            buffer.SetString(area.X, area.Y, text[..(length + 1)], default, area.Width);
        }
    }
}
