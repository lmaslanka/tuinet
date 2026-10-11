using BenchmarkDotNet.Attributes;
using Tuinet;
using Tuinet.Widgets;

namespace Tuinet.Benchmarks;

/// <summary>
/// A 100,000-line document in a 200×60 <see cref="TextArea"/>, caret in the middle: rendering only measures the
/// lines on screen, and typing only touches the caret's line.
/// </summary>
[MemoryDiagnoser]
public class TextAreaBenchmarks
{
    private readonly CellBuffer _buffer = new(200, 60);
    private TextAreaState _wrapped = null!;
    private TextAreaState _unwrapped = null!;
    private TextAreaState _cyrillic = null!;
    private string _screen = null!;
    private string _long = null!;
    private readonly ParagraphState _paragraph = new();

    [GlobalSetup]
    public void Setup()
    {
        string text = string.Join('\n', Enumerable.Range(0, 100_000)
            .Select(i => $"{i:D6} the quick brown fox jumps over the lazy dog · 世界 👨‍👩‍👧 · and keeps on running until the line wraps around the edge at least once"));
        _screen = string.Join('\n', text.Split('\n').Skip(50_000).Take(60));
        _long = string.Join('\n', text.Split('\n').Take(2000));
        _wrapped = Middle(new TextAreaState(text));
        _unwrapped = Middle(new TextAreaState(text) { SoftWrap = false });
        _cyrillic = Middle(new TextAreaState(string.Join('\n', Enumerable.Range(0, 100_000)
            .Select(i => $"{i:D6} съешь же ещё этих мягких французских булок да выпей чаю · 世界 · и беги дальше пока строка не перенесётся хотя бы раз"))));
    }

    [Benchmark(Baseline = true, Description = "render, wrapped")]
    public void RenderWrapped() => Render(ref _wrapped);

    [Benchmark(Description = "render, unwrapped")]
    public void RenderUnwrapped() => Render(ref _unwrapped);

    /// <summary>The wrapped render with Cyrillic text: letters outside ASCII, no clusters.</summary>
    [Benchmark(Description = "render, wrapped, cyrillic")]
    public void RenderWrappedCyrillic() => Render(ref _cyrillic);

    /// <summary>For scale: a paragraph of the lines in view, wrapped the same way.</summary>
    [Benchmark(Description = "paragraph, same lines")]
    public void Paragraph()
    {
        _buffer.Clear();
        _buffer.Render(new Paragraph(_screen) { Wrap = TextWrap.Word }, _buffer.Area);
    }

    /// <summary>A 2,000-line paragraph scrolled near its end: without a state every frame wraps the lines above the view.</summary>
    [Benchmark(Description = "paragraph, 2000 lines, scrolled to the end")]
    public void ParagraphScrolled()
    {
        _buffer.Clear();
        _buffer.Render(new Paragraph(_long) { Wrap = TextWrap.Word, Scroll = 3900 }, _buffer.Area);
    }

    /// <summary>The same with a <see cref="ParagraphState"/>: the layout is cached, only the view is wrapped.</summary>
    [Benchmark(Description = "paragraph, 2000 lines, scrolled, with state")]
    public void ParagraphScrolledState()
    {
        _buffer.Clear();
        ParagraphState state = _paragraph;
        _buffer.Render(new Paragraph(_long) { Wrap = TextWrap.Word, Scroll = 3900 }, _buffer.Area, ref state);
    }

    /// <summary>Scrollbar.Auto at the top of the text: the whole text is measured each frame unless a state caches it.</summary>
    [Benchmark(Description = "paragraph, 2000 lines, auto scrollbar, with state")]
    public void ParagraphScrollbarState()
    {
        _buffer.Clear();
        ParagraphState state = _paragraph;
        _buffer.Render(new Paragraph(_long) { Wrap = TextWrap.Word, Scrollbar = ScrollbarMode.Auto }, _buffer.Area, ref state);
    }

    /// <summary>A keystroke and the frame after it, then its undo (so the document stays the same size).</summary>
    [Benchmark(Description = "type + render")]
    public void TypeAndRender()
    {
        _wrapped.Handle(KeyEvent.Char('x'));
        Render(ref _wrapped);
        _wrapped.Undo();
    }

    [Benchmark(Description = "caret down + render")]
    public void Down()
    {
        _wrapped.Handle(new KeyEvent(KeyCode.Down));
        Render(ref _wrapped);
        _wrapped.Handle(new KeyEvent(KeyCode.Up));
    }

    private void Render(ref TextAreaState state)
    {
        _buffer.Clear();
        _buffer.Render(new TextArea { Focused = true, LineNumbers = true }, _buffer.Area, ref state);
    }

    private TextAreaState Middle(TextAreaState state)
    {
        state.MoveCaret(state.LineStart(50_000) + 30);
        Render(ref state);
        return state;
    }
}
