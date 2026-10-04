// Draws one case of clusters.txt per row: "a", the case's pieces (each a separate SetString, so separate
// cells), then "b", with "END" at column 12. 'r' forces a full repaint, 'q' quits.
// tools/e2e/e2e.py compares tmux's resulting screen with a reference that places every cell explicitly.
using Tuinet;

string[][] rows = [.. File.ReadLines(args[0])
    .Where(line => line.Length > 0 && !line.StartsWith('#'))
    .Select(line => line.Split('\t').Select(piece => piece[..piece.LastIndexOf(' ')]).ToArray())];

using var terminal = Terminal.Open();
Draw();
while (true)
{
    if (!terminal.Poll(out Event ev, Timeout.Infinite) || ev.Kind != EventKind.Key)
    {
        continue;
    }

    if (ev.Key.IsChar('q'))
    {
        break;
    }

    if (ev.Key.IsChar('r'))
    {
        terminal.Invalidate();
    }

    Draw();
}

void Draw()
{
    CellBuffer frame = terminal.BeginFrame();
    for (int y = 0; y < rows.Length; y++)
    {
        int x = frame.SetString(2, y, "a");
        foreach (string piece in rows[y])
        {
            x = frame.SetString(x, y, piece);
        }

        frame.SetString(x, y, "b");
        frame.SetString(12, y, "END");
    }

    terminal.Present();
}
