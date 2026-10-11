using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// The code beside each widget: a dim panel with the page's snippet, lightly highlighted (keywords, type and member
/// names, literals, comments). The snippet is a const, so a frame only walks its chars: nothing is allocated.
/// </summary>
internal static class CodePanel
{
    /// <summary>Columns the panel adds around the code: a border and a blank column on each side.</summary>
    public const int Chrome = 4;

    private static readonly string[] Keywords =
    [
        "new", "var", "if", "else", "for", "foreach", "in", "while", "return", "true", "false", "null", "ref", "out",
        "int", "long", "double", "bool", "char", "string", "default", "static", "readonly", "private", "public",
        "const", "void", "switch", "case", "break", "this", "stackalloc", "is", "not", "and", "or",
    ];

    public static int LineCount(ReadOnlySpan<char> code)
    {
        int lines = 0;
        foreach (ReadOnlySpan<char> _ in code.EnumerateLines())
        {
            lines++;
        }

        return lines;
    }

    public static int Width(ReadOnlySpan<char> code)
    {
        int width = 0;
        foreach (ReadOnlySpan<char> line in code.EnumerateLines())
        {
            width = Math.Max(width, TextWidth.Of(line));
        }

        return width;
    }

    /// <summary>The panel in <paramref name="area"/>, the code scrolled down <paramref name="scroll"/> lines (clamped here).</summary>
    public static void Render(CellBuffer buffer, Rect area, Page page, ref int scroll)
    {
        var block = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Faded,
            Style = Theme.Dialog,
            Title = " CODE ",
            TitleStyle = Theme.Heading(Theme.Muted),
            Footer = page.FileLabel,
            FooterAlignment = Alignment.Right,
        };
        buffer.Fill(area, Theme.Dialog);
        buffer.Render(block, area);

        Rect inner = block.Inner(area).Inset(1, 0);
        if (inner.IsEmpty)
        {
            return;
        }

        scroll = Math.Clamp(scroll, 0, Math.Max(0, page.CodeLines - inner.Height));
        if (page.CodeLines > inner.Height)
        {
            // In the right border's blank column: the code keeps its width.
            buffer.Render(new Scrollbar(page.CodeLines, inner.Height, scroll)
            {
                ThumbStyle = Theme.Accent(Theme.Muted),
                TrackStyle = Theme.Faded,
            }, new Rect(inner.Right, inner.Y, 1, inner.Height));
        }

        int row = -scroll;
        foreach (ReadOnlySpan<char> line in page.Code.AsSpan().EnumerateLines())
        {
            if (row >= inner.Height)
            {
                break;
            }

            if (row >= 0)
            {
                Line(buffer, inner.X, inner.Y + row, inner.Right, line);
            }

            row++;
        }
    }

    /// <summary>One line, token by token; what doesn't fit is cut.</summary>
    private static void Line(CellBuffer buffer, int x, int y, int right, ReadOnlySpan<char> line)
    {
        int i = 0;
        while (i < line.Length && x < right)
        {
            char c = line[i];
            int end = i + 1;
            Style style;
            if (c == '/' && end < line.Length && line[end] == '/')
            {
                end = line.Length;
                style = Theme.CodeComment;
            }
            else if (c is '"' or '\'')
            {
                while (end < line.Length && line[end] != c)
                {
                    end += line[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end + 1, line.Length);
                style = Theme.CodeString;
            }
            else if (char.IsAsciiDigit(c))
            {
                while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '.'))
                {
                    end++;
                }

                style = Theme.CodeNumber;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                {
                    end++;
                }

                style = IsKeyword(line[i..end]) ? Theme.CodeKeyword : char.IsUpper(c) ? Theme.CodeType : Theme.CodeName;
            }
            else
            {
                while (end < line.Length && !StartsToken(line, end))
                {
                    end++;
                }

                style = Theme.CodePunctuation;
            }

            x = buffer.SetString(x, y, line[i..end], style, right - x);
            i = end;
        }
    }

    private static bool StartsToken(ReadOnlySpan<char> line, int i)
    {
        char c = line[i];
        return char.IsLetterOrDigit(c) || c is '_' or '"' or '\''
            || (c == '/' && i + 1 < line.Length && line[i + 1] == '/');
    }

    private static bool IsKeyword(ReadOnlySpan<char> word)
    {
        foreach (string keyword in Keywords)
        {
            if (word.SequenceEqual(keyword))
            {
                return true;
            }
        }

        return false;
    }
}
