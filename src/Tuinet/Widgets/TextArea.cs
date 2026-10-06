namespace Tuinet.Widgets;

/// <summary>
/// Editable multi-line text for <see cref="TextArea"/>: a gap buffer with a line index, caret and selection,
/// undo/redo, soft wrap, and the viewport the last render showed (for keys and the mouse). Owned by the app. Lines
/// end with '\n'; pasted CR LF and CR become '\n' and tabs four spaces. Rendering only measures the lines on screen,
/// so a frame costs the same for 10 lines or 100,000.
/// </summary>
public sealed class TextAreaState : EditableText
{
    private const string Newline = "\n";

    private Rect _view;
    private bool _wrapped;
    private int _topLine;
    private int _topRow;    // visual row of _topLine at the top of the view (when wrapped)
    private int _scrollX;   // columns scrolled off the left (when not wrapped)
    private bool _dragging;

    // The last visual row the last render drew, valid while no edit or resize has happened since.
    private int _bottomLine;
    private int _bottomRow;
    private int _shownVersion = -1;

    /// <summary>Text with the caret at its start.</summary>
    public TextAreaState(string? text = null)
        : base(multiline: true, caretAtEndOnSet: false, text)
    {
    }

    /// <summary>Wrap long lines at word boundaries to the width (the default), or scroll sideways.</summary>
    public bool SoftWrap { get; set; } = true;

    public int LineCount => Lines;

    /// <summary>The text of a line, without its '\n'. Valid until the next edit or line read.</summary>
    public ReadOnlySpan<char> Line(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Lines, nameof(index));
        return LineSpan(index);
    }

    /// <summary>Offset where <paramref name="line"/> starts.</summary>
    public int LineStart(int line)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)line, (uint)Lines, nameof(line));
        return LineStartAt(line);
    }

    /// <summary>The line holding <paramref name="offset"/>.</summary>
    public int LineOf(int offset) => LineAt(Math.Clamp(offset, 0, Length));

    public int CaretLine => LineAt(Caret);

    /// <summary>The caret's offset into its line, in chars.</summary>
    public int CaretColumn => Caret - LineStartAt(CaretLine);

    /// <summary>First line in view at the last render (possibly partly scrolled off, when wrapped).</summary>
    public int TopLine => _topLine;

    /// <summary>Where the text (not the line numbers) was last rendered, for mouse hit-testing.</summary>
    public Rect Area => _view;

    internal int TopRow => _topRow;

    /// <summary>The render drew rows down to <paramref name="line"/>, <paramref name="row"/>.</summary>
    internal void Shown(int line, int row)
    {
        _bottomLine = line;
        _bottomRow = row;
        _shownVersion = Version;
    }
    internal int ScrollX => _scrollX;

    /// <summary>Columns rows wrap at, as last rendered; <see cref="int.MaxValue"/> when not wrapping.</summary>
    internal int WrapWidth => _wrapped ? Math.Max(1, _view.Width) : int.MaxValue;

    /// <summary>
    /// Apply an editing key. Returns false for keys left to the app (Tab, Esc, Ctrl+Enter, Ctrl+↑…). On top of the
    /// shared keys (see <see cref="EditableText"/>): Enter inserts a line break, ↑/↓ and PageUp/PageDown move by
    /// screen rows keeping the column, Home/End go to the line's start/end (with Ctrl, the text's), Shift selects,
    /// and Ctrl+A selects all.
    /// </summary>
    public bool Handle(KeyEvent key)
    {
        if (key.Kind == KeyKind.Release)
        {
            return false;
        }

        Modifiers mods = key.Modifiers;
        bool extend = (mods & Modifiers.Shift) != 0;
        bool plain = (mods & ~Modifiers.Shift) == 0;
        int page = Math.Max(1, _view.Height);
        switch (key.Code)
        {
            case KeyCode.Enter when plain:
                Type(Newline);
                return true;
            case KeyCode.Up when plain:
                MoveRows(-1, extend);
                return true;
            case KeyCode.Down when plain:
                MoveRows(1, extend);
                return true;
            case KeyCode.PageUp when plain:
                MoveRows(-page, extend);
                return true;
            case KeyCode.PageDown when plain:
                MoveRows(page, extend);
                return true;
            case KeyCode.Home:
                MoveTo((mods & Modifiers.Ctrl) != 0 ? 0 : LineStartAt(CaretLine), extend);
                return true;
            case KeyCode.End:
                MoveTo((mods & Modifiers.Ctrl) != 0 ? Length : LineEndAt(CaretLine), extend);
                return true;
            case KeyCode.Char when mods == Modifiers.Ctrl && key.Rune.Value == 'a':
                SelectAll();
                return true;
            default:
                return HandleCommon(key);
        }
    }

    /// <summary>
    /// Click places the caret (Shift+click selects to there), dragging selects (scrolling when the pointer leaves
    /// the top or bottom), and the wheel scrolls by <paramref name="wheelRows"/> rows per notch (sideways when not
    /// wrapping). Uses the layout of the last render. Returns whether the event was used.
    /// </summary>
    public bool HandleMouse(MouseEvent ev, int wheelRows = 3)
    {
        if (ev.IsWheel && ev.IsIn(_view))
        {
            ScrollBy(ev.WheelDelta * wheelRows);
            return true;
        }

        if (ev.Kind is MouseKind.ScrollLeft or MouseKind.ScrollRight && ev.IsIn(_view))
        {
            if (!_wrapped)
            {
                _scrollX = Math.Max(0, _scrollX + (ev.Kind == MouseKind.ScrollLeft ? -wheelRows : wheelRows));
            }

            return true;
        }

        if (_dragging && ev.Kind == MouseKind.Drag)
        {
            int y = ev.Y;
            if (y < _view.Y)
            {
                ScrollBy(-1);
                y = _view.Y;
            }
            else if (y >= _view.Bottom)
            {
                ScrollBy(1);
                y = _view.Bottom - 1;
            }

            MoveTo(OffsetAt(ev.X, y), extend: true);
            return true;
        }

        if (ev.Kind == MouseKind.Up && _dragging)
        {
            _dragging = false;
            return true;
        }

        if (!ev.IsClickIn(_view))
        {
            return false;
        }

        MoveTo(OffsetAt(ev.X, ev.Y), extend: (ev.Modifiers & Modifiers.Shift) != 0);
        _dragging = true;
        return true;
    }

    private protected override void OnReset()
    {
        _topLine = 0;
        _topRow = 0;
        _scrollX = 0;
    }

    /// <summary>
    /// Fix this frame's viewport in <paramref name="view"/>: clamp it to the text and, when <paramref name="reveal"/>
    /// and the caret moved since, scroll the least that brings the caret into view.
    /// </summary>
    internal void Arrange(Rect view, bool reveal)
    {
        if (view != _view || SoftWrap != _wrapped)
        {
            _shownVersion = -1;
        }

        _view = view;
        _wrapped = SoftWrap;
        if (_topLine >= Lines)
        {
            _topLine = Lines - 1;
            _topRow = 0;
        }

        _topRow = _wrapped ? Math.Min(_topRow, RowCount(_topLine) - 1) : 0;
        if (_wrapped)
        {
            _scrollX = 0;
        }

        if (reveal && CaretMoved)
        {
            Reveal();
            CaretMoved = false;
        }
    }

    private void Reveal()
    {
        int width = WrapWidth;
        int height = Math.Max(1, _view.Height);
        int line = LineAt(Caret);
        int column = Caret - LineStartAt(line);
        int row = RowOf(LineSpan(line), width, column, out _);

        // After moves alone, the rows the last frame showed tell whether the caret is in view.
        if (_shownVersion == Version
            && (line > _topLine || line == _topLine && row >= _topRow)
            && (line < _bottomLine || line == _bottomLine && row <= _bottomRow))
        {
        }
        else if (line < _topLine || line == _topLine && row < _topRow)
        {
            _topLine = line;
            _topRow = row;
        }
        else
        {
            // Rows from the top of the view down to the caret's, counting no further than the height.
            int rows = row + 1 - (line == _topLine ? _topRow : 0);
            for (int l = _topLine; l < line && rows <= height; l++)
            {
                rows += RowCount(l) - (l == _topLine ? _topRow : 0);
            }

            if (rows > height)
            {
                // Put the caret on the bottom row: walk up height - 1 rows from it.
                int up = height - 1;
                while (up > row)
                {
                    up -= row + 1;
                    line--;
                    row = RowCount(line) - 1;
                }

                _topLine = line;
                _topRow = row - up;
            }
        }

        if (!_wrapped)
        {
            line = LineAt(Caret);
            int x = TextWidth.Of(LineSpan(line)[..column]);
            if (x < _scrollX)
            {
                _scrollX = x;
            }
            else if (x >= _scrollX + _view.Width)
            {
                _scrollX = x - _view.Width + 1;
            }
        }
    }

    /// <summary>Move the caret by visual rows, aiming for the column it had when vertical movement began.</summary>
    private void MoveRows(int rows, bool extend)
    {
        int width = WrapWidth;
        int line = LineAt(Caret);
        int column = Caret - LineStartAt(line);
        ReadOnlySpan<char> text = LineSpan(line);
        int row = RowOf(text, width, column, out int rowStart);
        if (DesiredX < 0)
        {
            DesiredX = TextWidth.Of(text[rowStart..column]);
        }

        // Past the first or last row, the caret goes to the start or end of the text.
        for (; rows < 0; rows++)
        {
            if (row > 0)
            {
                row--;
            }
            else if (line > 0)
            {
                line--;
                row = RowCount(line) - 1;
            }
            else
            {
                MoveTo(0, extend, keepColumn: true);
                return;
            }
        }

        for (; rows > 0; rows--)
        {
            if (row < RowCount(line) - 1)
            {
                row++;
            }
            else if (line < Lines - 1)
            {
                line++;
                row = 0;
            }
            else
            {
                MoveTo(Length, extend, keepColumn: true);
                return;
            }
        }

        text = LineSpan(line);
        int start = RowStart(text, width, row);
        int end = RowEnd(text, start, width);
        MoveTo(LineStartAt(line) + OffsetAtColumn(text, start, end, DesiredX), extend, keepColumn: true);
    }

    /// <summary>Scroll the view by visual rows, without moving the caret; stops with the last row at the bottom.</summary>
    private void ScrollBy(int rows)
    {
        _shownVersion = -1;
        for (; rows < 0; rows++)
        {
            if (_topRow > 0)
            {
                _topRow--;
            }
            else if (_topLine > 0)
            {
                _topLine--;
                _topRow = RowCount(_topLine) - 1;
            }
        }

        int height = Math.Max(1, _view.Height);
        for (; rows > 0; rows--)
        {
            int below = -_topRow;
            for (int line = _topLine; line < Lines && below <= height; line++)
            {
                below += RowCount(line);
            }

            if (below <= height)
            {
                return;
            }

            if (_topRow < RowCount(_topLine) - 1)
            {
                _topRow++;
            }
            else
            {
                _topLine++;
                _topRow = 0;
            }
        }
    }

    /// <summary>The offset shown at a cell, as the last render laid the text out; past the last row, the end.</summary>
    private int OffsetAt(int x, int y)
    {
        int width = WrapWidth;
        int line = _topLine;
        ReadOnlySpan<char> text = LineSpan(line);
        int start = RowStart(text, width, _topRow);
        for (int row = _view.Y; row < y; row++)
        {
            int end = RowEnd(text, start, width);
            if (end < text.Length)
            {
                start = end;
            }
            else if (line + 1 < Lines)
            {
                line++;
                text = LineSpan(line);
                start = 0;
            }
            else
            {
                return Length;
            }
        }

        return LineStartAt(line) + OffsetAtColumn(text, start, RowEnd(text, start, width), x - _view.X + _scrollX);
    }

    private int RowCount(int line) => _wrapped ? RowCount(LineSpan(line), WrapWidth) : 1;

    // ---- Wrapping: a line splits into visual rows; each row is [start, end) and the next starts at end. ----

    /// <summary>
    /// End of the visual row of <paramref name="line"/> that starts at <paramref name="start"/>: after the last space
    /// that fits (spaces past the edge hang off the row), mid-word for words longer than the width, and at least
    /// one cluster.
    /// </summary>
    internal static int RowEnd(ReadOnlySpan<char> line, int start, int width)
    {
        if (width == int.MaxValue)
        {
            return line.Length;
        }

        int x = 0;
        int i = start;
        int lastBreak = -1;
        while (i < line.Length)
        {
            int n = ClusterLength(line[i..]);
            int w = TextWidth.Of(line.Slice(i, n));
            bool space = n == 1 && line[i] == ' ';
            if (x + w > width)
            {
                if (space)
                {
                    while (i < line.Length && line[i] == ' ')
                    {
                        i++;
                    }

                    return i;
                }

                return lastBreak > start ? lastBreak : i > start ? i : i + n;
            }

            x += w;
            i += n;
            if (space)
            {
                lastBreak = i;
            }
        }

        return line.Length;
    }

    internal static int RowCount(ReadOnlySpan<char> line, int width)
    {
        if (width == int.MaxValue)
        {
            return 1;
        }

        int rows = 0;
        int start = 0;
        do
        {
            start = RowEnd(line, start, width);
            rows++;
        }
        while (start < line.Length);
        return rows;
    }

    /// <summary>The row holding <paramref name="column"/> (an offset into the line); a row's end belongs to the next row, except the last.</summary>
    internal static int RowOf(ReadOnlySpan<char> line, int width, int column, out int rowStart)
    {
        int start = 0;
        for (int row = 0; ; row++)
        {
            int end = RowEnd(line, start, width);
            if (column < end || end >= line.Length)
            {
                rowStart = start;
                return row;
            }

            start = end;
        }
    }

    internal static int RowStart(ReadOnlySpan<char> line, int width, int row)
    {
        int start = 0;
        for (int i = 0; i < row && start < line.Length; i++)
        {
            start = RowEnd(line, start, width);
        }

        return start;
    }

    /// <summary>
    /// The offset in row <paramref name="start"/>..<paramref name="end"/> at screen column <paramref name="x"/>: before
    /// the cluster covering it, or past the row's last cluster (only the line's last row can put the caret at its end).
    /// </summary>
    internal static int OffsetAtColumn(ReadOnlySpan<char> line, int start, int end, int x)
    {
        int at = 0;
        int last = start;
        for (int i = start; i < end;)
        {
            int n = ClusterLength(line[i..]);
            int w = TextWidth.Of(line.Slice(i, n));
            if (x < at + w)
            {
                return i;
            }

            at += w;
            last = i;
            i += n;
        }

        return end >= line.Length ? end : last;
    }
}

/// <summary>
/// Draws a <see cref="TextAreaState"/>: the lines in view, soft-wrapped or scrolled sideways, with optional line
/// numbers, a placeholder, and when <see cref="Focused"/> the selection and the real terminal cursor at the caret.
/// Only visible lines are measured.
/// </summary>
public readonly ref struct TextArea : IStatefulWidget<TextAreaState>
{
    public TextArea()
    {
    }

    /// <summary>Text style; the area is erased with it (default colors keep the background underneath).</summary>
    public Style Style { get; init; }

    /// <summary>Layered over <see cref="Style"/> for selected text; reverse video by default.</summary>
    public Style SelectionStyle { get; init; } = new(Color.Default, Color.Default, Attr.Reverse);

    /// <summary>Shown while the text is empty.</summary>
    public ReadOnlySpan<char> Placeholder { get; init; }

    public Style PlaceholderStyle { get; init; }

    /// <summary>Line numbers in a gutter on the left (on a wrapped line's first row only).</summary>
    public bool LineNumbers { get; init; }

    public Style LineNumberStyle { get; init; }

    /// <summary>Shows the terminal cursor at the caret, and the selection; scrolls to keep the caret in view.</summary>
    public bool Focused { get; init; }

    /// <summary>Shape of the caret while <see cref="Focused"/>: a blinking bar, as in GUI text fields.</summary>
    public CursorShape CursorShape { get; init; } = CursorShape.BlinkingBar;

    public void Render(Rect area, CellBuffer buffer, ref TextAreaState state)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        buffer.Erase(area, Style);

        Rect text = area;
        int digits = LineNumbers ? Digits(state.LineCount) : 0;
        if (digits > 0 && area.Width > digits + 1)
        {
            text = new Rect(area.X + digits + 1, area.Y, area.Width - digits - 1, area.Height);
        }
        else
        {
            digits = 0;
        }

        state.Arrange(text, Focused);

        if (state.IsEmpty && !Placeholder.IsEmpty)
        {
            buffer.SetString(text.X, text.Y, Placeholder, Style.Patch(PlaceholderStyle), text.Width, Overflow.Ellipsis);
        }

        int width = state.WrapWidth;
        int scrollX = state.ScrollX;
        int selectionStart = Focused ? state.SelectionStart : 0;
        int selectionEnd = Focused ? state.SelectionEnd : 0;
        Style selected = Style.Patch(SelectionStyle);
        Style numbers = Style.Patch(LineNumberStyle);
        int caret = state.Caret;
        int caretLine = state.CaretLine;
        int caretX = -1;
        int caretY = -1;
        Span<char> number = stackalloc char[11];

        int y = text.Y;
        int row = state.TopRow;
        int bottomLine = state.TopLine;
        int bottomRow = row;
        for (int line = state.TopLine; line < state.LineCount && y < text.Bottom; line++)
        {
            int lineStart = state.LineStart(line);
            ReadOnlySpan<char> chars = state.Line(line);
            if (digits > 0 && row == 0)
            {
                (line + 1).TryFormat(number, out int n);
                buffer.SetString(area.X + digits - n, y, number[..n], numbers);
            }

            int start = row == 0 ? 0 : TextAreaState.RowStart(chars, width, row);
            while (y < text.Bottom)
            {
                int end = TextAreaState.RowEnd(chars, start, width);
                bool last = end >= chars.Length;

                // Up to three runs: before, inside and after the selection. A selected line break shows as one
                // selected cell after the text. Wrapped rows fit by construction; unwrapped ones are measured only
                // when something follows on the row, since a run cut at the edge ends it.
                int from = Math.Clamp(selectionStart - lineStart, start, end);
                int to = Math.Clamp(selectionEnd - lineStart, start, end);
                int lineEnd = lineStart + chars.Length;
                bool lineBreak = last && line + 1 < state.LineCount && selectionStart <= lineEnd && selectionEnd > lineEnd;
                bool measure = width == int.MaxValue;
                int x = text.X;
                int skip = scrollX;
                bool fits = Draw(buffer, ref x, ref skip, y, text.Right, chars[start..from], Style, measure && (from < end || lineBreak))
                    && Draw(buffer, ref x, ref skip, y, text.Right, chars[from..to], selected, measure && (to < end || lineBreak))
                    && Draw(buffer, ref x, ref skip, y, text.Right, chars[to..end], Style, measure && lineBreak);
                if (lineBreak && fits && skip == 0 && x < text.Right)
                {
                    buffer.SetString(x, y, " ", selected, 1);
                }

                if (line == caretLine && caret - lineStart >= start && (caret - lineStart < end || last))
                {
                    caretX = text.X + TextWidth.Of(chars[start..(caret - lineStart)]) - scrollX;
                    caretY = y;
                }

                bottomLine = line;
                bottomRow = row++;
                y++;
                if (last)
                {
                    break;
                }

                start = end;
            }

            row = 0;
        }

        state.Shown(bottomLine, bottomRow);
        if (Focused && caretY >= 0)
        {
            buffer.SetCursor(Math.Clamp(caretX, text.X, text.Right - 1), caretY, CursorShape);
        }
    }

    /// <summary>
    /// Draw a run at <paramref name="x"/>, first skipping <paramref name="skip"/> columns (a wide glyph cut by the left
    /// edge leaves blanks). With <paramref name="measure"/>, returns false when it didn't all fit, so the rest of the
    /// row is not drawn.
    /// </summary>
    private static bool Draw(CellBuffer buffer, ref int x, ref int skip, int y, int right, ReadOnlySpan<char> run, Style style, bool measure)
    {
        while (skip > 0 && !run.IsEmpty)
        {
            int n = EditableText.ClusterLength(run);
            int w = TextWidth.Of(run[..n]);
            x += Math.Max(0, w - skip);
            skip = Math.Max(0, skip - w);
            run = run[n..];
        }

        if (run.IsEmpty)
        {
            return true;
        }

        int columns = right - x;
        bool fits = !measure || 2 * run.Length <= columns || Fits(run, columns);
        x = buffer.SetString(x, y, run, style, columns);
        return fits;
    }

    /// <summary>Whether <paramref name="text"/> fits in <paramref name="columns"/>, measuring no further than that.</summary>
    private static bool Fits(ReadOnlySpan<char> text, int columns)
    {
        int width = 0;
        for (int i = 0; i < text.Length;)
        {
            int n = EditableText.ClusterLength(text[i..]);
            width += TextWidth.Of(text.Slice(i, n));
            if (width > columns)
            {
                return false;
            }

            i += n;
        }

        return true;
    }

    private static int Digits(int value)
    {
        int digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }
}
