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

/// <summary>Multi-line text ('\n'-separated) with wrapping, alignment and vertical scroll, in one style or styled runs.</summary>
public readonly ref struct Paragraph : IWidget
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

    /// <summary>Visual lines to skip from the top.</summary>
    public int Scroll { get; init; }

    /// <summary>
    /// Draw a scrollbar in the rightmost column; text wraps one column narrower while it shows. Measuring the
    /// text costs a pass over it per frame. Clicks map to a <see cref="Scroll"/> with
    /// <see cref="Widgets.Scrollbar.PositionAt"/> over that column.
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

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        // Auto first checks the full width, stopping one line past the height, so text that fits costs little.
        if (Scrollbar == ScrollbarMode.Always
            || (Scrollbar == ScrollbarMode.Auto && LineCount(Text, area.Width, Wrap, area.Height + 1) > area.Height))
        {
            var bar = new Rect(area.Right - 1, area.Y, 1, area.Height);
            area = new Rect(area.X, area.Y, area.Width - 1, area.Height);
            buffer.Render(new Scrollbar(LineCount(Text, area.Width, Wrap), area.Height, Scroll)
            {
                ThumbStyle = ScrollbarThumbStyle,
                TrackStyle = ScrollbarTrackStyle,
            }, bar);
            if (area.IsEmpty)
            {
                return;
            }
        }

        int row = -Scroll;
        var lines = new LineEnumerator(Text, area.Width, Wrap);
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
                    var styled = new StyledText(Text, _runs, Style);
                    buffer.SetText(x, area.Y + row, styled.Slice(lines.CurrentStart, line.Length), area.Right - x);
                }
            }

            row++;
        }
    }

    /// <summary>Yields visual lines: splits on '\n', then wraps each to the width.</summary>
    private ref struct LineEnumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly int _width;
        private readonly TextWrap _wrap;
        private int _pos;
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

        public bool MoveNext()
        {
            if (_done)
            {
                return false;
            }

            ReadOnlySpan<char> rest = _text[_pos..];
            int newline = rest.IndexOf('\n');
            ReadOnlySpan<char> logical = newline < 0 ? rest : rest[..newline];

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
            else if (newline >= 0)
            {
                _pos += newline + 1;
            }
            else
            {
                _done = true;
            }

            return true;
        }

        /// <summary>Chars of <paramref name="line"/> that fit in the width (at least one grapheme cluster).</summary>
        private readonly int Fit(ReadOnlySpan<char> line)
        {
            int width = 0;
            int i = 0;
            int lastBreak = -1;
            while (i < line.Length)
            {
                int consumed = Graphemes.Next(line[i..], out Rune rune, out int w, out bool multi);
                if (width + w > _width)
                {
                    if (i == 0)
                    {
                        return consumed;
                    }

                    return _wrap == TextWrap.Word && lastBreak > 0 ? lastBreak : i;
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
