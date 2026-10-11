using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="TreeView{TSource}"/> over this repository's outline, addressed by index. Arrows or j/k move, → opens a
/// folder (then steps in), ← closes it (then steps out), space or Enter toggles; a click on ▸/▾ folds, a click on a
/// row selects it.
/// </summary>
public sealed class TreePage : Page
{
    private const string Snippet = """
        var source = new Outline(Nodes);
        buffer.Render(new TreeView<Outline>(source)
        {
            GuideStyle = Theme.Faded,
            ExpandedSymbol = "▾ ",
            CollapsedSymbol = "▸ ",
            LeafSymbol = "  ",
            ExpanderStyle = Theme.Accent(Theme.Amber),
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
            Scrollbar = ScrollbarMode.Auto,
        }, tree, ref _tree);
        // …
        if (_tree.Handle(key, source)) return true;
        if (key.Is(KeyCode.Enter)
            && source.HasChildren(_tree.SelectedNode))
        {
            _tree.Toggle(_tree.SelectedNode);
            return true;
        }
        """;

    private static readonly (string, string)[] Keys = [("j/k", "move"), ("←/→", "fold"), ("enter", "toggle")];

    /// <summary>One node: its name, depth and parent (node ids are indexes into <see cref="Nodes"/>), and its children.</summary>
    private sealed record Node(string Name, int Depth, int Parent, int[] Children);

    private static readonly Node[] Nodes = Build(
    [
        (0, "src"), (1, "Tuinet"), (2, "Widgets"), (3, "Block.cs"), (3, "ListView.cs"), (3, "Menu.cs"), (3, "Table.cs"),
        (3, "Tabs.cs"), (3, "TreeView.cs"), (2, "Platform"), (3, "UnixTty.cs"), (3, "WindowsTty.cs"), (2, "CellBuffer.cs"),
        (2, "Layout.cs"), (2, "Terminal.cs"), (0, "samples"), (1, "Gallery"), (2, "Pages"), (3, "ListPage.cs"),
        (3, "TreePage.cs"), (2, "GalleryApp.cs"), (1, "Showcase"), (2, "ShowcaseApp.cs"), (0, "tests"),
        (1, "Tuinet.Tests"), (2, "TreeViewTests.cs"), (0, "README.md"),
    ]);

    /// <summary>Top-level nodes: the children of <see cref="TreeState.Root"/>.</summary>
    private static readonly int[] Roots = Enumerable.Range(0, Nodes.Length).Where(i => Nodes[i].Parent == TreeState.Root).ToArray();

    private TreeState _tree = new();

    public TreePage() : base("Tree", "TreeView<T> · TreeState", Snippet, Keys)
    {
        _tree.Expand(0);   // src
        _tree.Expand(1);   // src/Tuinet
    }

    public TreeState Tree => _tree;

    /// <summary>The selected node's name.</summary>
    public string SelectedName => _tree.SelectedNode >= 0 ? Nodes[_tree.SelectedNode].Name : "";

    public override bool Handle(Event ev, long nowMs)
    {
        var source = new Outline(Nodes);
        if (ev.Kind == EventKind.Mouse)
        {
            return _tree.HandleMouse(ev.Mouse, source);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (_tree.Handle(key, source)) return true;
        if (key.Is(KeyCode.Enter)
            && source.HasChildren(_tree.SelectedNode))
        {
            _tree.Toggle(_tree.SelectedNode);
            return true;
        }

        return false;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        var tree = new Rect(area.X, area.Y, area.Width, Math.Max(0, area.Height - 2));
        var source = new Outline(Nodes);
        buffer.Render(new TreeView<Outline>(source)
        {
            GuideStyle = Theme.Faded,
            ExpandedSymbol = "▾ ",
            CollapsedSymbol = "▸ ",
            LeafSymbol = "  ",
            ExpanderStyle = Theme.Accent(Theme.Amber),
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
            Scrollbar = ScrollbarMode.Auto,
        }, tree, ref _tree);

        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[4]);
        status.Append(_tree.Count, Theme.Strong);
        status.Append(" of ", Theme.Dim);
        status.Append(Nodes.Length, Theme.Strong);
        status.Append(" nodes shown", Theme.Dim);
        if (area.Height > 2)
        {
            buffer.SetText(area.X, area.Bottom - 1, status.Build(), area.Width);
        }
    }

    /// <summary>The outline's (depth, name) pairs as nodes: each one's parent is the last node one level up.</summary>
    private static Node[] Build((int Depth, string Name)[] outline)
    {
        var parents = new int[outline.Length];
        var last = new int[8];
        for (int i = 0; i < outline.Length; i++)
        {
            int depth = outline[i].Depth;
            parents[i] = depth == 0 ? TreeState.Root : last[depth - 1];
            last[depth] = i;
        }

        var nodes = new Node[outline.Length];
        for (int i = 0; i < outline.Length; i++)
        {
            int[] children = Enumerable.Range(0, outline.Length).Where(c => parents[c] == i).ToArray();
            nodes[i] = new Node(outline[i].Name, outline[i].Depth, parents[i], children);
        }

        return nodes;
    }

    /// <summary>The tree's view of <see cref="Nodes"/>: folders in blue, files in the text color.</summary>
    private readonly struct Outline(Node[] nodes) : ITreeSource
    {
        public int ChildCount(int node) => node == TreeState.Root ? Roots.Length : nodes[node].Children.Length;

        public int Child(int node, int index) => node == TreeState.Root ? Roots[index] : nodes[node].Children[index];

        public bool HasChildren(int node) => nodes[node].Children.Length > 0;

        public int Parent(int node) => nodes[node].Parent;

        public void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected) =>
            buffer.SetString(area.X, area.Y, nodes[node].Name,
                HasChildren(node) ? Theme.Heading(Theme.Blue) : Theme.Body, area.Width, Overflow.Ellipsis);
    }
}
