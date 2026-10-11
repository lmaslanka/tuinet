using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using TgColor = Terminal.Gui.Drawing.Color;

namespace Tuinet.Comparisons;

/// <summary>
/// Terminal.Gui v2: a <see cref="ListView"/> in a rounded <see cref="FrameView"/> plus a <see cref="Label"/>, run as
/// an <see cref="IApplication"/> session. Each frame sets <see cref="ListView.SelectedItem"/> (which scrolls it into
/// view) and calls <see cref="IApplication.LayoutAndDraw"/>, the step the main loop runs after input: layout, draw
/// into the driver's buffer, then <c>Driver.Refresh</c>, which turns dirty cells into ANSI.
/// </summary>
/// <remarks>
/// The driver is the stock (internal) <c>DriverImpl</c> wired like Terminal.Gui's own perf tests do, with
/// <see cref="CountingOutput"/> in place of <see cref="AnsiOutput"/>. AnsiOutput can't be measured as is: away from
/// a real tty it drops into a degraded mode that skips cursor-positioning sequences, and on a tty it writes to it.
/// </remarks>
internal sealed class TerminalGuiList : IListScenario
{
    private readonly CountingOutput _output;
    private readonly IApplication _app;
    private readonly Runnable _top;
    private readonly ListView _list;
    private long _frame;

    /// <param name="capture">Also copy the output here (to replay or inspect it), or null.</param>
    public TerminalGuiList(Stream? capture = null)
    {
        _output = new CountingOutput(capture);
        var factory = new AnsiComponentFactory(null, _output, null);
        var input = new AnsiInputProcessor(new ConcurrentQueue<char>());
        var buffer = new OutputBufferImpl();
        ISizeMonitor sizeMonitor = factory.CreateSizeMonitor(_output, buffer);

        // DriverImpl is internal (apps get one from IApplication.Init, which can't take a custom output), so
        // build it through its public constructor by reflection, the same wiring as Terminal.Gui's own tests.
        Type driverType = typeof(IDriver).Assembly.GetType("Terminal.Gui.Drivers.DriverImpl", throwOnError: true)!;
        var driver = (IDriver)Activator.CreateInstance(driverType, factory, input, buffer, _output, new AnsiRequestScheduler(input.GetParser()), sizeMonitor)!;
        sizeMonitor.Initialize(driver);
        driver.SetScreenSize(Scenario.Width, Scenario.Height);

        _app = Application.Create();
        _app.Driver = driver;

        Attribute selected = new(Rgb(Scenario.SelectedFg), Rgb(Scenario.SelectedBg));
        _list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        _list.SetScheme(new Scheme(Plain(Scenario.Text)) { Focus = selected, Active = selected });
        _list.SetSource(new ObservableCollection<string>(Scenario.Items));

        var frame = new FrameView { Title = Scenario.Title, BorderStyle = LineStyle.Rounded, Width = Dim.Fill(), Height = Dim.Fill(1) };
        frame.SetScheme(new Scheme(Plain(Scenario.Border)) { Focus = Plain(Scenario.Border) }); // title in border color, not inverted
        frame.Add(_list);

        var status = new Label { Text = Scenario.Status, Y = Pos.AnchorEnd(1) };
        status.SetScheme(new Scheme(Plain(Scenario.StatusFg)));

        _top = new Runnable { Width = Dim.Fill(), Height = Dim.Fill() };
        _top.SetScheme(new Scheme(Plain(Scenario.Text)));
        _top.Add(frame, status);
        _app.Begin(_top);
        _list.SetFocus();
        _list.SelectedItem = Scenario.Selected(0);
        _app.LayoutAndDraw();
    }

    public string Name => "Terminal.Gui";
    public long Bytes => _output.Bytes;
    public long Writes => _output.Writes;

    public void Frame()
    {
        _list.SelectedItem = Scenario.Selected(++_frame);
        _app.LayoutAndDraw();
    }

    public void Dispose()
    {
        _top.Dispose();
        _app.Dispose();
    }

    private static TgColor Rgb((byte R, byte G, byte B) c) => new(c.R, c.G, c.B);

    /// <summary>Foreground <paramref name="fg"/> on the terminal's default background.</summary>
    private static Attribute Plain((byte R, byte G, byte B) fg) => new(Rgb(fg), TgColor.None);

    /// <summary>
    /// <see cref="AnsiOutput"/>'s Unix path up to the write syscall: same cursor moves, same UTF-8 encoding of each
    /// chunk, but the bytes are counted instead of written.
    /// </summary>
    private sealed class CountingOutput(Stream? capture) : OutputBase, IOutput
    {
        private Cursor _cursor = new();

        public long Bytes { get; private set; }
        public long Writes { get; private set; }

        public System.Drawing.Size GetSize() => new(Scenario.Width, Scenario.Height);
        public void SetSize(int width, int height) { }
        public Cursor GetCursor() => _cursor;
        public void Suspend() { }
        public void Dispose() { }

        public void SetCursor(Cursor cursor)
        {
            if (!cursor.IsVisible)
            {
                Write(EscSeqUtils.CSI_HideCursor);
            }
            else
            {
                if (_cursor.Style != cursor.Style)
                {
                    Write(EscSeqUtils.CSI_SetCursorStyle(cursor.Style));
                }

                Write(EscSeqUtils.CSI_ShowCursor);
            }

            SetCursorPositionImpl(cursor.Position?.X ?? 0, cursor.Position?.Y ?? 0);
            _cursor = cursor;
        }

        public void Write(ReadOnlySpan<char> text)
        {
            StringBuilder captured = new();
            captured.Append(text);
            base.Write(captured);
            Count(Encoding.UTF8.GetBytes(text.ToArray()));
        }

        protected override void Write(StringBuilder output)
        {
            base.Write(output);
            Count(Encoding.UTF8.GetBytes(output.ToString()));
        }

        protected override bool SetCursorPositionImpl(int col, int row)
        {
            if (_cursor.Position is { } at && at.X == col && at.Y == row)
            {
                return false;
            }

            Write(EscSeqUtils.CSI_SetCursorPosition(row + 1, col + 1));
            return true;
        }

        private void Count(byte[] utf8)
        {
            Bytes += utf8.Length;
            Writes++;
            capture?.Write(utf8);
        }
    }
}
