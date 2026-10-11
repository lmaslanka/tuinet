using Tuinet;
using Tuinet.Samples.Showcase;

namespace Tuinet.Benchmarks;

/// <summary>Bytes per frame of the Showcase sample at 120×40, through a real <see cref="Terminal"/>.</summary>
internal sealed class ShowcaseBytes : IDisposable
{
    private readonly Terminal _terminal;
    private readonly ShowcaseApp _app = new();

    public ShowcaseBytes(bool repeat) => _terminal = NullTty.Open(120, 40, repeat);

    /// <summary>The first frame: everything painted over the cleared screen.</summary>
    public int FirstFrame() => Frame();

    /// <summary>Handle <paramref name="ev"/>, then draw: bytes of that frame.</summary>
    public int After(Event ev)
    {
        _app.Handle(ev, 0);
        return Frame();
    }

    private int Frame()
    {
        CellBuffer frame = _terminal.BeginFrame();
        _app.Render(frame, 0);
        _terminal.Present();
        return _terminal.LastFrameBytes;
    }

    public void Dispose() => _terminal.Dispose();
}
