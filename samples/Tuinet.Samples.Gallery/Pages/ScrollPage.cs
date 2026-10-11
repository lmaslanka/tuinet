using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// Standalone <see cref="Scrollbar"/>s: a log wider and longer than its view, with a vertical bar on the right and a
/// horizontal one underneath. Arrows, PgUp/PgDn and Home/End scroll; the wheel scrolls; a press on a bar jumps there
/// and dragging keeps going (mapped back with <see cref="Scrollbar.PositionAt"/>).
/// </summary>
public sealed class ScrollPage : Page
{
    private const string Snippet = """
        var vbar = new Rect(view.Right, view.Y, 1, view.Height);
        var hbar = new Rect(view.X, view.Bottom, view.Width, 1);
        buffer.Render(new Scrollbar(Lines.Length,
            view.Height, _top)
        {
            ThumbStyle = Theme.Accent(Theme.Blue),
            TrackStyle = Theme.Faded,
        }, vbar);
        buffer.Render(new Scrollbar(Width, view.Width, _left)
        {
            Orientation = Direction.Horizontal,
            ThumbStyle = Theme.Accent(Theme.Amber),
            TrackStyle = Theme.Faded,
        }, hbar);
        // …
        if (_drag == Vertical)
            _top = Scrollbar.PositionAt(_vbar, m.X, m.Y,
                Lines.Length, _view.Height);
        """;

    private static readonly (string, string)[] Keys = [("↑/↓ ←/→", "scroll"), ("pgup/pgdn", "page"), ("drag", "a bar")];

    private const int None = 0;
    private const int Vertical = 1;
    private const int Horizontal = 2;

    private static readonly string[] Levels = ["INFO ", "DEBUG", "WARN ", "INFO ", "TRACE"];
    private static readonly string[] Messages =
    [
        "frame presented: 1 write, rows skipped by memcmp, gaps jumped with relative cursor moves",
        "input burst coalesced: 12 key repeats handled before a single frame was drawn",
        "scroll region moved 18 rows; only the new rows were painted (DECSTBM + IL/DL)",
        "style delta: fg changed, bg kept, bold dropped; erase-to-end-of-line for the trailing blanks",
        "resize to 110x34: back buffer reallocated, next frame repaints everything once",
    ];

    /// <summary>Sixty long lines, built once.</summary>
    private static readonly string[] Lines = Enumerable.Range(0, 60)
        .Select(i => $"{i + 1,3}  12:{i / 6 % 60:D2}:{i * 7 % 60:D2}.{i * 37 % 1000:D3}  {Levels[i % Levels.Length]}  {Messages[i * 3 % Messages.Length]}")
        .ToArray();

    /// <summary>Where the level starts in a line: after the number and the time.</summary>
    private const int LevelColumn = 19;

    /// <summary>The widest line: what the horizontal bar measures.</summary>
    private static readonly int Width = Lines.Max(line => line.Length);

    private int _top;
    private int _left;
    private int _drag;
    private Rect _view;
    private Rect _vbar;
    private Rect _hbar;

    public ScrollPage() : base("Scroll", "Scrollbar · PositionAt", Snippet, Keys)
    {
    }

    public int Top => _top;
    public int Left => _left;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Mouse)
        {
            return HandleMouse(ev.Mouse);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        int page = Math.Max(1, _view.Height - 1);
        if (key.Is(KeyCode.Up) || key.IsChar('k')) _top--;
        else if (key.Is(KeyCode.Down) || key.IsChar('j')) _top++;
        else if (key.Is(KeyCode.PageUp)) _top -= page;
        else if (key.Is(KeyCode.PageDown)) _top += page;
        else if (key.Is(KeyCode.Home)) (_top, _left) = (0, 0);
        else if (key.Is(KeyCode.End)) _top = Lines.Length;
        else if (key.Is(KeyCode.Left) || key.IsChar('h')) _left -= 4;
        else if (key.Is(KeyCode.Right) || key.IsChar('l')) _left += 4;
        else return false;

        Clamp();
        return true;
    }

    /// <summary>A press on a bar starts a drag that lasts until the button comes up, wherever the pointer goes.</summary>
    private bool HandleMouse(MouseEvent m)
    {
        if (m.IsClickIn(_vbar)) _drag = Vertical;
        else if (m.IsClickIn(_hbar)) _drag = Horizontal;
        else if (m.Kind == MouseKind.Up && _drag != None)
        {
            _drag = None;
            return true;
        }
        else if (m.IsWheel && m.IsIn(_view))
        {
            _top += m.WheelDelta * 3;
            Clamp();
            return true;
        }
        else if (m.Kind is MouseKind.ScrollLeft or MouseKind.ScrollRight && m.IsIn(_view))
        {
            _left += m.Kind == MouseKind.ScrollLeft ? -4 : 4;
            Clamp();
            return true;
        }

        if (m.Kind is not (MouseKind.Down or MouseKind.Drag) || _drag == None)
        {
            return false;
        }

        if (_drag == Vertical)
            _top = Scrollbar.PositionAt(_vbar, m.X, m.Y,
                Lines.Length, _view.Height);
        else
            _left = Scrollbar.PositionAt(_hbar, m.X, m.Y,
                Width, _view.Width, Direction.Horizontal);

        return true;
    }

    private void Clamp()
    {
        _top = Math.Clamp(_top, 0, Math.Max(0, Lines.Length - _view.Height));
        _left = Math.Clamp(_left, 0, Math.Max(0, Width - _view.Width));
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // The log in a box, leaving a column for the vertical bar and a row for the horizontal one.
        var box = new Rect(area.X, area.Y, area.Width, Math.Max(0, area.Height - 2));
        var block = new Block { BorderType = BorderType.Rounded, BorderStyle = Theme.Faded, Title = " app.log ", TitleStyle = Theme.Dim };
        buffer.Render(block, box);
        Rect inner = block.Inner(box);
        var view = new Rect(inner.X, inner.Y, inner.Width - 1, inner.Height - 1);
        _view = view;
        Clamp();

        for (int row = 0; row < view.Height && _top + row < Lines.Length; row++)
        {
            ReadOnlySpan<char> line = Lines[_top + row];
            Style style = line[LevelColumn] == 'W' ? Theme.Accent(Theme.Amber) : Theme.Body;
            buffer.SetString(view.X, view.Y + row, line[Math.Min(_left, line.Length)..], style, view.Width);
        }

        var vbar = new Rect(view.Right, view.Y, 1, view.Height);
        var hbar = new Rect(view.X, view.Bottom, view.Width, 1);
        buffer.Render(new Scrollbar(Lines.Length, view.Height, _top)
        {
            ThumbStyle = Theme.Accent(Theme.Blue),
            TrackStyle = Theme.Faded,
        }, vbar);
        buffer.Render(new Scrollbar(Width, view.Width, _left)
        {
            Orientation = Direction.Horizontal,
            ThumbStyle = Theme.Accent(Theme.Amber),
            TrackStyle = Theme.Faded,
        }, hbar);
        (_vbar, _hbar) = (vbar, hbar);

        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[8]);
        status.Append("lines ", Theme.Dim);
        status.Append(_top + 1, Theme.Strong);
        status.Append("–", Theme.Dim);
        status.Append(Math.Min(Lines.Length, _top + view.Height), Theme.Strong);
        status.Append(" of ", Theme.Dim);
        status.Append(Lines.Length, Theme.Strong);
        status.Append(" · column ", Theme.Dim);
        status.Append(_left + 1, Theme.Strong);
        if (area.Height > 2)
        {
            buffer.SetText(area.X, area.Bottom - 1, status.Build(), area.Width);
        }
    }
}
