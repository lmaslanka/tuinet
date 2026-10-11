namespace Tuinet.Screenshots;

/// <summary>
/// Everything the renderer draws on one screen (<c>--sheet</c>): box drawing, blocks, braille, attributes, the
/// palettes, wide glyphs, clusters and a cursor. For checking renderer changes by eye; not a README image.
/// </summary>
internal static class TestSheet
{
    public static Shot Shot { get; } = new("renderer-sheet", "tuinet · renderer sheet", 80, 30,
        () => new DelegateApp((_, _) => true, (buffer, _) => Draw(buffer)), _ => { });

    private static void Draw(CellBuffer b)
    {
        var dim = new Style(Color.Indexed(8), default);
        int y = 1;
        b.SetString(2, y, "┌─┬─┐ ╭─┬─╮ ┏━┳━┓ ╔═╦═╗ ┍━┑ ╓─╖ ┄┄ ┈┈ ╌╌ ╱╲╳");
        b.SetString(2, y + 1, "│ │ │ │ │ │ ┃ ┃ ┃ ║ ║ ║ │ │ ║ ║ ┆  ┊  ╎  ╲╱");
        b.SetString(2, y + 2, "├─┼─┤ ├─┼─┤ ┣━╋━┫ ╠═╬═╣ ┝━┥ ╟─╢ ┆  ┊  ╎");
        b.SetString(2, y + 3, "└─┴─┘ ╰─┴─╯ ┗━┻━┛ ╚═╩═╝ ┕━┙ ╙─╜ ╴╶╵╷ ╼╾╽╿");
        b.SetString(48, y, "╒═╤═╕ ┎┰┒ ╆╅", new Style(Color.Indexed(14), default));
        b.SetString(48, y + 1, "╞═╪═╡ ┠╂┨ ╄╃", new Style(Color.Indexed(14), default));
        b.SetString(48, y + 2, "╘═╧═╛ ┖┸┚ ┿╋", new Style(Color.Indexed(14), default));

        y = 6;
        b.SetString(2, y, "▁▂▃▄▅▆▇█ ▏▎▍▌▋▊▉█ ▀▐▔▕ ▖▗▘▙▚▛▜▝▞▟ ░░░▒▒▒▓▓▓", new Style(Color.Hex(0x34D399), default));
        b.SetString(2, y + 1, "████████ ▔▔▔▔ ▁▁▁▁ ░░░░░░░░ ▒▒▒▒▒▒▒▒ ▓▓▓▓▓▓▓▓", new Style(Color.Hex(0xF5A623), Color.Hex(0x1C2433)));
        b.SetString(2, y + 2, "⠁⠃⠇⡇⣇⣧⣷⣿ ⢀⣀⣤⣶⣿ ⡏⢹⣸⠟ ⠀⠿⠿⠀", new Style(Color.Hex(0x38BDF8), default));

        y = 10;
        int x = 2;
        foreach ((string label, Attr attr) in (ReadOnlySpan<(string, Attr)>)[
            ("bold", Attr.Bold), ("dim", Attr.Dim), ("italic", Attr.Italic), ("underline", Attr.Underline),
            ("strike", Attr.Strike), ("reverse", Attr.Reverse), ("bold italic", Attr.Bold | Attr.Italic), ("hidden", Attr.Hidden)])
        {
            x = b.SetString(x, y, label, new Style(default, default, attr)) + 2;
        }

        b.SetString(2, y + 1, "Regular text, ascii !\"#$%&'()*+,-./0123456789:;<=>?@[\\]^_`{|}~ → ← ↑ ↓ ↗ ✓ ✗ … · • ● ▲ ▼ ■ □");

        y = 13;
        for (int i = 0; i < 16; i++)
        {
            b.SetString(2 + i * 4, y, $"{i,3}", new Style(Color.Indexed((byte)i), default));
            b.SetString(2 + i * 4, y + 1, "   ", new Style(default, Color.Indexed((byte)i)));
        }

        for (int i = 16; i < 256; i++)
        {
            int column = (i - 16) % 72, row = (i - 16) / 72;
            b.SetString(2 + column, y + 3 + row, " ", new Style(default, Color.Indexed((byte)i)));
        }

        for (int i = 0; i < 72; i++)
        {
            byte v = (byte)(i * 255 / 71);
            b.SetString(2 + i, y + 8, "▀", new Style(Color.Rgb(v, (byte)(255 - v), 128), Color.Rgb(255, v, (byte)(255 - v))));
        }

        y = 23;
        b.SetString(2, y, "wide: 日本語 한국어 ｆｕｌｌ  emoji: 👍 🎉 🚀  cluster: 👨‍👩‍👧 🇵🇱 é", default);
        b.SetString(2, y + 1, "defaults: fg/bg ", default);
        b.SetString(18, y + 1, " indexed 8 on 0 ", new Style(Color.Indexed(8), Color.Indexed(0)));
        int end = b.SetString(2, y + 3, "cursor (bar) after this:", dim);
        b.SetCursor(end + 1, y + 3, CursorShape.Bar);
    }
}
