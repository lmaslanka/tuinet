using System.Text;

namespace Tuinet.Widgets;

/// <summary>One column of a <see cref="Table{TSource}"/>: header text, width and how cells sit in it.</summary>
/// <param name="Header">Header text, aligned like the column's cells.</param>
/// <param name="Width">Share of the row; columns are laid out with <see cref="Layout.Horizontal"/>.</param>
/// <param name="Alignment">Where text sits in the column. Right-align numbers so their digits line up.</param>
/// <param name="Overflow">How text wider than the column is cut.</param>
public readonly record struct TableColumn(
    string Header,
    Constraint Width,
    Alignment Alignment = Alignment.Left,
    Overflow Overflow = Overflow.Ellipsis);

/// <summary>Rows for a <see cref="Table{TSource}"/>. Only visible rows are asked for (virtualized).</summary>
public interface ITableSource
{
    int RowCount { get; }

    /// <summary>
    /// Text of one cell: a stored string as a span (no copy), or text formatted into
    /// <paramref name="scratch"/> (numbers, dates) and sliced. <paramref name="style"/> is layered
    /// over the row style, so selection and striping still show.
    /// </summary>
    ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style);
}

/// <summary>Helpers for <see cref="Table{TSource}"/>.</summary>
public static class Table
{
    /// <summary>Cells handed to <see cref="ITableSource.Cell"/> as scratch space.</summary>
    public const int ScratchLength = 128;

    /// <summary>
    /// Widest cell of <paramref name="column"/> (and <paramref name="header"/>) over the first
    /// <paramref name="maxRows"/> rows. O(rows): call it when the data changes and cache the result as
    /// <see cref="Constraint.Length"/>, not every frame. Add 2 for a column that shows a sort arrow.
    /// </summary>
    public static int Measure<TSource>(TSource source, int column, ReadOnlySpan<char> header = default, int maxRows = int.MaxValue)
        where TSource : ITableSource, allows ref struct
    {
        // Scoped to match the stack scratch, so the source may return a slice of it.
        scoped TSource local = source;
        Span<char> scratch = stackalloc char[ScratchLength];
        int width = TextWidth.Of(header);
        int rows = Math.Min(source.RowCount, maxRows);
        for (int row = 0; row < rows; row++)
        {
            width = Math.Max(width, TextWidth.Of(local.Cell(row, column, scratch, out _)));
        }

        return width;
    }
}

/// <summary>
/// A scrolling, selectable table: a header row over virtualized rows, laid out in columns with
/// per-column alignment. Selection and scroll live in a <see cref="ListState"/>, as for <see cref="ListView{TSource}"/>.
/// </summary>
public readonly ref struct Table<TSource> : IStatefulWidget<ListState>
    where TSource : ITableSource, allows ref struct
{
    private const int MaxStackColumns = 64;

    private readonly TSource _source;
    private readonly ReadOnlySpan<TableColumn> _columns;

    public Table(TSource source, ReadOnlySpan<TableColumn> columns)
    {
        _source = source;
        _columns = columns;
    }

    /// <summary>Layered over every body row.</summary>
    public Style Style { get; init; }

    /// <summary>Layered over the header row.</summary>
    public Style HeaderStyle { get; init; }

    /// <summary>Draw a '─' rule under the header (with '┼' where it crosses <see cref="ColumnSeparator"/>).</summary>
    public bool HeaderSeparator { get; init; }

    /// <summary>Empty columns between adjacent columns.</summary>
    public int ColumnSpacing { get; init; } = 1;

    /// <summary>Drawn in the gap between columns (e.g. '│'); '\0' for none. Needs <see cref="ColumnSpacing"/> ≥ 1.</summary>
    public char ColumnSeparator { get; init; }

    /// <summary>Style of the column separators and the header rule.</summary>
    public Style SeparatorStyle { get; init; }

    /// <summary>Layered over odd rows (zebra striping). Stripes follow the row index, so they don't shift on scroll.</summary>
    public Style AlternateRowStyle { get; init; }

    /// <summary>Layered over the selected row after its cells render.</summary>
    public Style SelectedStyle { get; init; }

    /// <summary>Drawn before the selected row (e.g. "> "); other rows and the header are indented by its width.</summary>
    public ReadOnlySpan<char> HighlightSymbol { get; init; }

    /// <summary>Layered over <see cref="SelectedStyle"/> for the highlight symbol.</summary>
    public Style HighlightSymbolStyle { get; init; }

    /// <summary>Column whose header shows a sort arrow, or -1. Sorting itself is up to the source.</summary>
    public int SortColumn { get; init; } = -1;

    /// <summary>Show '▼' instead of '▲' on <see cref="SortColumn"/>.</summary>
    public bool SortDescending { get; init; }

    public void Render(Rect area, CellBuffer buffer, ref ListState state)
    {
        area = area.Intersect(buffer.Area);
        int headerRows = HeaderSeparator ? 2 : 1;
        int count = _source.RowCount;
        int header = Math.Min(area.Height, headerRows);
        state.Follow(count, area, new Rect(area.X, area.Y + header, area.Width, area.Height - header));
        int n = _columns.Length;
        if (area.IsEmpty || n == 0)
        {
            return;
        }

        int indent = HighlightSymbol.IsEmpty ? 0 : TextWidth.Of(HighlightSymbol);
        Span<Rect> cells = n <= MaxStackColumns ? stackalloc Rect[n] : new Rect[n];
        LayoutColumns(area, indent, cells);

        RenderHeader(buffer, area, cells);
        if (HeaderSeparator && area.Height > 1)
        {
            RenderRule(buffer, area, cells);
        }

        // Scoped to match the stack scratch, so the source may return a slice of it.
        scoped TSource source = _source;
        Span<char> scratch = stackalloc char[Table.ScratchLength];
        int top = area.Y + headerRows;
        int rows = Math.Clamp(count - state.Offset, 0, Math.Max(0, area.Height - headerRows));
        for (int line = 0; line < rows; line++)
        {
            int index = state.Offset + line;
            int y = top + line;
            var row = new Rect(area.X, y, area.Width, 1);
            buffer.SetStyle(row, Style);
            if ((index & 1) == 1)
            {
                buffer.SetStyle(row, AlternateRowStyle);
            }

            for (int i = 0; i < n; i++)
            {
                ReadOnlySpan<char> text = source.Cell(index, i, scratch, out Style style);
                DrawAligned(buffer, cells[i].X, y, cells[i].Width, text, style, _columns[i].Alignment, _columns[i].Overflow);
            }
        }

        // Separators run down the whole body at once, then the selection is layered over its row.
        RenderSeparators(buffer, area, new Rect(area.X, top, area.Width, rows), cells, ColumnSeparator);
        int selected = state.Selected - state.Offset;
        if (selected >= 0 && selected < rows)
        {
            var row = new Rect(area.X, top + selected, area.Width, 1);
            buffer.SetStyle(row, SelectedStyle);
            if (indent > 0)
            {
                buffer.SetString(row.X, row.Y, HighlightSymbol, SelectedStyle.Patch(HighlightSymbolStyle), row.Width);
            }
        }
    }

    /// <summary>
    /// The column whose header is at cell (<paramref name="x"/>, <paramref name="y"/>) at the last render, or -1:
    /// e.g. to sort on a header click. Build the table with the same columns and settings as for render.
    /// </summary>
    public int HeaderColumnAt(int x, int y, in ListState state)
    {
        Rect area = state.Area;
        int n = _columns.Length;
        if (area.IsEmpty || n == 0 || y != area.Y || !area.Contains(x, y))
        {
            return -1;
        }

        int indent = HighlightSymbol.IsEmpty ? 0 : TextWidth.Of(HighlightSymbol);
        Span<Rect> cells = n <= MaxStackColumns ? stackalloc Rect[n] : new Rect[n];
        LayoutColumns(area, indent, cells);
        for (int i = 0; i < n; i++)
        {
            if (x >= cells[i].X && x < cells[i].Right)
            {
                return i;
            }
        }

        return -1;
    }

    private void LayoutColumns(Rect area, int indent, Span<Rect> cells)
    {
        int n = _columns.Length;
        var content = new Rect(area.X + indent, area.Y, Math.Max(0, area.Width - indent), 1);
        Span<Constraint> widths = n <= MaxStackColumns ? stackalloc Constraint[n] : new Constraint[n];
        for (int i = 0; i < n; i++)
        {
            widths[i] = _columns[i].Width;
        }

        Layout.Horizontal(content, widths, cells, ColumnSpacing);
    }

    private void RenderHeader(CellBuffer buffer, Rect area, ReadOnlySpan<Rect> cells)
    {
        int y = area.Y;
        buffer.SetStyle(new Rect(area.X, y, area.Width, 1), HeaderStyle);
        for (int i = 0; i < cells.Length; i++)
        {
            TableColumn column = _columns[i];
            int x = cells[i].X;
            int width = cells[i].Width;
            if (i != SortColumn || width < 3)
            {
                DrawAligned(buffer, x, y, width, column.Header, HeaderStyle, column.Alignment, column.Overflow);
                continue;
            }

            // Arrow plus a space: on the left of right-aligned headers so the text still hugs the edge.
            const int arrow = 2;
            int textWidth = Math.Min(TextWidth.Of(column.Header), width - arrow);
            int start = x + Offset(width, textWidth + arrow, column.Alignment);
            int textX = column.Alignment == Alignment.Right ? start + arrow : start;
            int arrowX = column.Alignment == Alignment.Right ? start : start + textWidth + 1;
            buffer.SetString(textX, y, column.Header, HeaderStyle, textWidth, column.Overflow);
            buffer.SetRune(arrowX, y, new Rune(SortDescending ? '▼' : '▲'), HeaderStyle);
        }

        RenderSeparators(buffer, area, new Rect(area.X, y, area.Width, 1), cells, ColumnSeparator);
    }

    private void RenderRule(CellBuffer buffer, Rect area, ReadOnlySpan<Rect> cells)
    {
        var rule = new Rect(area.X, area.Y + 1, area.Width, 1);
        buffer.SetRune(rule, new Rune('─'), SeparatorStyle);
        RenderSeparators(buffer, area, rule, cells, ColumnSeparator == '\0' ? '\0' : '┼');
    }

    /// <summary>Draw <paramref name="separator"/> in each column gap over the rows of <paramref name="band"/>.</summary>
    private void RenderSeparators(CellBuffer buffer, Rect area, Rect band, ReadOnlySpan<Rect> cells, char separator)
    {
        if (separator == '\0' || ColumnSpacing < 1 || band.Height <= 0)
        {
            return;
        }

        var rune = new Rune(separator);
        for (int i = 0; i < cells.Length - 1; i++)
        {
            int x = cells[i].Right + (ColumnSpacing - 1) / 2;
            if (x >= area.Right || cells[i + 1].Width == 0)
            {
                break;
            }

            buffer.SetRune(new Rect(x, band.Y, 1, band.Height), rune, SeparatorStyle);
        }
    }

    private static void DrawAligned(CellBuffer buffer, int x, int y, int width, ReadOnlySpan<char> text, Style style,
        Alignment alignment, Overflow overflow)
    {
        if (width <= 0 || text.IsEmpty)
        {
            return;
        }

        if (alignment == Alignment.Left)
        {
            buffer.SetString(x, y, text, style, width, overflow);
            return;
        }

        // Measured already: text that fits needs no second measurement for the ellipsis check.
        int textWidth = TextWidth.Of(text);
        int offset = Offset(width, textWidth, alignment);
        buffer.SetString(x + offset, y, text, style, width - offset, textWidth <= width ? Overflow.Clip : overflow);
    }

    /// <summary>Columns to skip so <paramref name="used"/> sits at <paramref name="alignment"/> in <paramref name="width"/>.</summary>
    private static int Offset(int width, int used, Alignment alignment)
    {
        int slack = Math.Max(0, width - used);
        return alignment switch
        {
            Alignment.Center => slack / 2,
            Alignment.Right => slack,
            _ => 0,
        };
    }
}

/// <summary>A table source over rows of strings (row-major), drawn in one style. Missing cells are blank.</summary>
public readonly ref struct TextRows : ITableSource
{
    private readonly ReadOnlySpan<string[]> _rows;
    private readonly Style _style;

    public TextRows(ReadOnlySpan<string[]> rows, Style style = default)
    {
        _rows = rows;
        _style = style;
    }

    public int RowCount => _rows.Length;

    public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
    {
        style = _style;
        string[] cells = _rows[row];
        return (uint)column < (uint)cells.Length ? cells[column] : default;
    }
}
