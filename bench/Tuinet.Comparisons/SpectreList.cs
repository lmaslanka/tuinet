using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using SpectreColor = Spectre.Console.Color;
using SpectreStyle = Spectre.Console.Style;
using SpectreText = Spectre.Console.Text;

namespace Tuinet.Comparisons;

/// <summary>
/// Spectre.Console: a live display (<c>AnsiConsole.Live</c>) of a <see cref="Panel"/> over the visible rows, the
/// usual way to animate with Spectre. It has no virtualized list widget, so the app slices out the visible rows
/// itself and hands a new renderable to <see cref="LiveDisplayContext.UpdateTarget"/> each frame; Spectre then
/// moves the cursor back to the top of the display and writes the whole renderable again.
/// </summary>
/// <remarks>
/// <see cref="LiveDisplay.Start(Action{LiveDisplayContext})"/> blocks for the life of the display, so it runs on
/// a parked thread and frames are pushed from the caller's thread, which is where the rendering happens.
/// </remarks>
internal sealed class SpectreList : IListScenario
{
    private static readonly SpectreStyle Text = new(Rgb(Scenario.Text));
    private static readonly SpectreStyle Border = new(Rgb(Scenario.Border));
    private static readonly SpectreStyle Selected = new(Rgb(Scenario.SelectedFg), Rgb(Scenario.SelectedBg));
    private static readonly SpectreStyle Status = new(Rgb(Scenario.StatusFg));
    private const int InnerWidth = Scenario.Width - 2;

    private readonly CountingWriter _writer;
    private readonly IAnsiConsole _console;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private LiveDisplayContext? _context;
    private int _offset;
    private long _frame;

    /// <param name="capture">Also copy the output here (to replay or inspect it), or null.</param>
    public SpectreList(Stream? capture = null)
    {
        _writer = new CountingWriter(capture);
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.Yes,
            Out = new Output(_writer),
        });
        _console.Profile.Width = Scenario.Width;
        _console.Profile.Height = Scenario.Height;
        _console.Profile.Capabilities.Unicode = true;

        using var started = new ManualResetEventSlim();
        _thread = new Thread(() => _console.Live(Build(Scenario.Selected(0))).Start(context =>
        {
            _context = context;
            started.Set();
            _stop.Wait();
        }))
        {
            IsBackground = true,
            Name = "Spectre live display",
        };
        _thread.Start();
        started.Wait();
    }

    public string Name => "Spectre.Console";
    public long Bytes => _writer.Bytes;
    public long Writes => _writer.Writes;

    public void Frame() => _context!.UpdateTarget(Build(Scenario.Selected(_frame++)));

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }

    /// <summary>The visible rows (scrolled to keep <paramref name="selected"/> in view) in a bordered panel, then the status line.</summary>
    private Rows Build(int selected)
    {
        if (selected < _offset)
        {
            _offset = selected;
        }
        else if (selected >= _offset + Scenario.Viewport)
        {
            _offset = selected - Scenario.Viewport + 1;
        }

        var lines = new IRenderable[Scenario.Viewport];
        for (int row = 0; row < lines.Length; row++)
        {
            int index = _offset + row;
            string item = Scenario.Items[index];
            lines[row] = index == selected ? new SpectreText(item.PadRight(InnerWidth), Selected) : new SpectreText(item, Text);
        }

        var panel = new Panel(new Rows(lines))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Border,
            Header = new PanelHeader(Scenario.Title),
            Padding = new Padding(0),
            Expand = true,
            Height = Scenario.Height - 1,
        };

        return new Rows(panel, new SpectreText(Scenario.Status, Status));
    }

    private static SpectreColor Rgb((byte R, byte G, byte B) c) => new(c.R, c.G, c.B);

    /// <summary>A fixed-size terminal over the counting writer.</summary>
    private sealed class Output(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer => writer;
        public bool IsTerminal => true;
        public int Width => Scenario.Width;
        public int Height => Scenario.Height;
        public void SetEncoding(Encoding encoding) { }
    }

    /// <summary>
    /// Counts the UTF-8 bytes and the write calls that reach it. Spectre writes each style change and each text
    /// run as its own call; on <c>Console.Out</c> (auto-flushing) each one is a write syscall.
    /// </summary>
    private sealed class CountingWriter(Stream? capture) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public long Bytes { get; private set; }
        public long Writes { get; private set; }

        public override void Write(char value) => Count([value]);
        public override void Write(string? value) => Count(value);
        public override void Write(char[] buffer, int index, int count) => Count(buffer.AsSpan(index, count));
        public override void Write(ReadOnlySpan<char> buffer) => Count(buffer);

        private void Count(ReadOnlySpan<char> text)
        {
            Bytes += Encoding.UTF8.GetByteCount(text);
            Writes++;
            capture?.Write(Encoding.UTF8.GetBytes(text.ToArray()));
        }
    }
}
