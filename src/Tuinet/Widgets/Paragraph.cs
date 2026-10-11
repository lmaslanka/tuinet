using System.Text;

namespace Tuinet.Widgets;

public enum TextWrap : byte
{
    /// <summary>Each line is clipped at the right edge.</summary>
    None,

    /// <summary>Break at spaces; words longer than a line are broken mid-word.</summary>
    Word,

    /// <summary>Break at the edge, mid-word.</summary>
    Char,
}

/// <summary>
/// Caches a <see cref="Paragraph"/>'s line layout between frames, so a long text renders in O(visible rows) at any
/// <see cref="Paragraph.Scroll"/> and a scrollbar doesn't re-measure it every frame. Owned by the app and passed to
/// <see cref="WidgetExtensions.Render{TWidget, TState}(CellBuffer, TWidget, Rect, ref TState)"/>. Each frame the
/// cached layout is checked against the text (a vectorized compare of its chars, far cheaper than wrapping them)
/// and rebuilt only when the text, the wrap mode or the width changed (see <see cref="VerifyText"/>); a rebuild is
/// the only time it allocates.
/// Layouts for the two most recent widths are kept, since a paragraph with <see cref="ScrollbarMode.Auto"/>
/// measures at the full width and lays the text out one column narrower.
/// </summary>
public sealed class ParagraphState
{
    private char[] _text = [];
    private int _length = -1;
    private TextWrap _wrap;
    private Layout _recent = new() { Width = -1, Starts = [] };
    private Layout _older = new() { Width = -1, Starts = [] };

    /// <summary>
    /// Compare the text with the cached copy every frame (the default): the layout can never go stale. The compare is
    /// vectorized but reads the whole text (about 40 µs per MB), so an app that calls <see cref="Invalidate"/> whenever
    /// its text changes can turn it off; a text of another length is always re-laid out.
    /// </summary>
    public bool VerifyText { get; set; } = true;

    /// <summary>Drop the cached layout: the next frame lays the text out again.</summary>
    public void Invalidate() => _length = -1;

    /// <summary>
    /// Visual lines <paramref name="text"/> takes at <paramref name="width"/> (as <see cref="Paragraph.LineCount(ReadOnlySpan{char}, int, TextWrap)"/>),
    /// from the cache when it is current: measure before rendering, e.g. to clamp the scroll, at no extra cost.
    /// </summary>
    public int LineCount(ReadOnlySpan<char> text, int width, TextWrap wrap) => Lines(text, width, wrap).Length;

    /// <summary>Offsets where the visual lines of <paramref name="text"/> start at <paramref name="width"/>.</summary>
    internal ReadOnlySpan<int> Lines(ReadOnlySpan<char> text, int width, TextWrap wrap)
    {
        width = Math.Max(1, width);
        if (wrap != _wrap || text.Length != _length || (VerifyText && !text.SequenceEqual(_text.AsSpan(0, _length))))
        {
            if (_text.Length < text.Length)
            {
                _text = new char[Math.Max(text.Length, 2 * _text.Length)];
            }

            text.CopyTo(_text);
            _length = text.Length;
            _wrap = wrap;
            _recent.Width = -1;
            _older.Width = -1;
        }

        if (_recent.Width != width)
        {
            if (_older.Width != width)
            {
                Build(ref _older, text, width, wrap);
            }

            (_recent, _older) = (_older, _recent);
        }

        return _recent.Starts.AsSpan(0, _recent.Count);
    }

    private static void Build(ref Layout layout, ReadOnlySpan<char> text, int width, TextWrap wrap)
    {
        int count = 0;
        var lines = new Paragraph.LineEnumerator(text, width, wrap);
        while (lines.MoveNext())
        {
            if (count == layout.Starts.Length)
            {
                Array.Resize(ref layout.Starts, Math.Max(64, 2 * count));
            }

            layout.Starts[count++] = lines.CurrentStart;
        }

        layout.Count = count;
        layout.Width = width;
    }

    private struct Layout
    {
        public int Width;
        public int[] Starts;
        public int Count;
    }
}

/// <summary>
/// Multi-line text ('\n'-separated) with wrapping, alignment and vertical scroll, in one style or styled runs.
/// Rendering wraps the lines in view each frame; with a <see cref="ParagraphState"/>, only those.
/// </summary>
public readonly ref struct Paragraph : IWidget, IStatefulWidget<ParagraphState>
{
    private readonly ReadOnlySpan<StyledRun> _runs;

    public Paragraph(ReadOnlySpan<char> text, Style style = default)
    {
        Text = text;
        Style = style;
    }

    /// <summary>Styled text: runs keep their styles across wrapped lines.</summary>
    public Paragraph(StyledText text)
    {
        Text = text.Text;
        Style = text.Style;
        _runs = text.Runs;
    }

    public ReadOnlySpan<char> Text { get; init; }
    public Style Style { get; init; }
    public TextWrap Wrap { get; init; }
    public Alignment Alignment { get; init; }

    /// <summary>
    /// Visual lines to skip from the top. Without a <see cref="ParagraphState"/> the skipped lines are still wrapped
    /// each frame, so a frame costs O(scroll + visible lines); with one it costs O(visible lines).
    /// </summary>
    public int Scroll { get; init; }

    /// <summary>
    /// Draw a scrollbar in the rightmost column; text wraps one column narrower while it shows. Measuring the
    /// text costs a pass over it per frame, unless a <see cref="ParagraphState"/> caches it. Clicks map to a
    /// <see cref="Scroll"/> with <see cref="Widgets.Scrollbar.PositionAt"/> over that column.
    /// </summary>
    public ScrollbarMode Scrollbar { get; init; }

    public Style ScrollbarThumbStyle { get; init; }
    public Style ScrollbarTrackStyle { get; init; }

    /// <summary>Number of visual lines <paramref name="text"/> takes at <paramref name="width"/>.</summary>
    public static int LineCount(ReadOnlySpan<char> text, int width, TextWrap wrap) => LineCount(text, width, wrap, int.MaxValue);

    /// <summary>Visual lines, counting no further than <paramref name="limit"/>.</summary>
    private static int LineCount(ReadOnlySpan<char> text, int width, TextWrap wrap, int limit)
    {
        int count = 0;
        var lines = new LineEnumerator(text, width, wrap);
        while (count < limit && lines.MoveNext())
        {
            count++;
        }

        return count;
    }

    public void Render(Rect area, CellBuffer buffer) => Render(area, buffer, null);

    /// <summary>Render with the line layout cached in <paramref name="state"/>: O(visible lines) at any <see cref="Scroll"/>.</summary>
    public void Render(Rect area, CellBuffer buffer, ref ParagraphState state) => Render(area, buffer, state);

    private void Render(Rect area, CellBuffer buffer, ParagraphState? state)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        bool bar = Scrollbar == ScrollbarMode.Always;
        if (Scrollbar == ScrollbarMode.Auto)
        {
            // Auto first checks the full width, stopping one line past the height, so text that fits costs little.
            int count = state is null ? LineCount(Text, area.Width, Wrap, area.Height + 1) : state.LineCount(Text, area.Width, Wrap);
            bar = count > area.Height;
        }

        if (bar)
        {
            var track = new Rect(area.Right - 1, area.Y, 1, area.Height);
            area = new Rect(area.X, area.Y, area.Width - 1, area.Height);
            int count = state is null ? LineCount(Text, area.Width, Wrap) : state.LineCount(Text, area.Width, Wrap);
            buffer.Render(new Scrollbar(count, area.Height, Scroll)
            {
                ThumbStyle = ScrollbarThumbStyle,
                TrackStyle = ScrollbarTrackStyle,
            }, track);
            if (area.IsEmpty)
            {
                return;
            }
        }

        var lines = new LineEnumerator(Text, area.Width, Wrap);
        int row = -Scroll;
        if (state is not null && Scroll > 0)
        {
            ReadOnlySpan<int> starts = state.Lines(Text, area.Width, Wrap);
            if (Scroll >= starts.Length)
            {
                return;
            }

            lines.Seek(starts[Scroll]);
            row = 0;
        }

        // Styled text: the run cursor is carried from line to line instead of walked from the start for each.
        var rest = new StyledText(Text, _runs, Style);
        int restStart = 0;
        while (row < area.Height && lines.MoveNext())
        {
            if (row >= 0)
            {
                ReadOnlySpan<char> line = lines.Current;
                int x = area.X;
                if (Alignment != Alignment.Left)
                {
                    int slack = Math.Max(0, area.Width - TextWidth.Of(line));
                    x += Alignment == Alignment.Center ? slack / 2 : slack;
                }

                if (_runs.IsEmpty)
                {
                    buffer.SetString(x, area.Y + row, line, Style, area.Right - x);
                }
                else
                {
                    int skip = lines.CurrentStart - restStart;
                    rest = rest.Slice(skip, rest.Text.Length - skip);
                    restStart = lines.CurrentStart;
                    buffer.SetText(x, area.Y + row, rest.Slice(0, line.Length), area.Right - x);
                }
            }

            row++;
        }
    }

    /// <summary>
    /// Yields visual lines: splits on '\n', then wraps each to the width. The end of a logical line is found once,
    /// not once per visual line, so a long line costs O(length) to enumerate.
    /// </summary>
    internal ref struct LineEnumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly int _width;
        private readonly TextWrap _wrap;
        private int _pos;
        private int _end = -1;   // end of the logical line _pos is in, once found
        private bool _done;

        public LineEnumerator(ReadOnlySpan<char> text, int width, TextWrap wrap)
        {
            _text = text;
            _width = Math.Max(1, width);
            _wrap = wrap;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        /// <summary>Offset of <see cref="Current"/> in the text.</summary>
        public int CurrentStart { get; private set; }

        /// <summary>Continue from <paramref name="position"/>, which must be where a visual line starts.</summary>
        public void Seek(int position)
        {
            _pos = position;
            _end = -1;
            _done = false;
        }

        public bool MoveNext()
        {
            if (_done)
            {
                return false;
            }

            if (_end < 0)
            {
                int newline = _text[_pos..].IndexOf('\n');
                _end = newline < 0 ? _text.Length : _pos + newline;
            }

            ReadOnlySpan<char> logical = _text[_pos.._end];
            CurrentStart = _pos;
            int take = _wrap == TextWrap.None ? logical.Length : Fit(logical);
            Current = logical[..take].TrimEnd('\r');
            if (_wrap == TextWrap.Word && take < logical.Length)
            {
                Current = Current.TrimEnd(' ');
                while (take < logical.Length && logical[take] == ' ')
                {
                    take++;
                }
            }

            if (take < logical.Length)
            {
                _pos += take;
            }
            else if (_end < _text.Length)
            {
                _pos = _end + 1;
                _end = -1;
            }
            else
            {
                _done = true;
            }

            return true;
        }

        /// <summary>
        /// Chars of <paramref name="line"/> that fit in the width (at least one grapheme cluster). Runs of printable
        /// ASCII and of other letters without marks are measured without segmenting; only clusters are.
        /// </summary>
        private readonly int Fit(ReadOnlySpan<char> line)
        {
            int width = 0;
            int i = 0;
            int lastBreak = -1;
            while (i < line.Length)
            {
                int room = _width - width;
                ReadOnlySpan<char> rest = line[i..];
                int run = Graphemes.AsciiRun(rest, room + 1);   // one past the room: the char that doesn't fit
                int columns = run;
                if (run == 0)
                {
                    run = Graphemes.SimpleRun(rest, room, out columns);
                }

                if (run > 0)
                {
                    if (columns > room)
                    {
                        // ASCII up to and past the edge: the char at the edge doesn't fit.
                        int edge = i + room;
                        if (_wrap == TextWrap.Word && line[edge] != ' ')   // a space at the edge hangs off the line
                        {
                            int space = line[i..edge].LastIndexOf(' ');
                            if (space >= 0)
                            {
                                lastBreak = i + space + 1;
                            }

                            if (lastBreak > 0)
                            {
                                return lastBreak;
                            }
                        }

                        return edge;
                    }

                    if (_wrap == TextWrap.Word)
                    {
                        int space = line.Slice(i, run).LastIndexOf(' ');
                        if (space >= 0)
                        {
                            lastBreak = i + space + 1;
                        }
                    }

                    width += columns;
                    i += run;
                    continue;
                }

                int consumed = Graphemes.Next(rest, out Rune rune, out int w, out bool multi);
                if (width + w > _width)
                {
                    if (i == 0)
                    {
                        return consumed;
                    }

                    bool space = rune.Value == ' ' && !multi;
                    return _wrap == TextWrap.Word && !space && lastBreak > 0 ? lastBreak : i;
                }

                width += w;
                i += consumed;
                if (rune.Value == ' ' && !multi)
                {
                    lastBreak = i;
                }
            }

            return line.Length;
        }
    }
}
