namespace Tuinet.Samples.Gallery;

/// <summary>
/// One gallery page: a live widget with its state, and the snippet that draws it. The page owns its state (as an app
/// would); <see cref="GalleryApp"/> draws the frame around it, the code panel and the key hints.
/// </summary>
public abstract class Page
{
    private readonly (string Key, string Action)[] _hints;

    /// <param name="title">The tab's title.</param>
    /// <param name="heading">What the page shows, as the live panel's title (the widget and its state type).</param>
    /// <param name="code">
    /// An excerpt of the page's own render (and input) code, shown beside the widget. A test checks that it appears in
    /// the page's source file, whitespace aside; a line "// …" separates parts taken from different places in it.
    /// </param>
    /// <param name="hints">The page's keys, shown at the bottom.</param>
    protected Page(string title, string heading, string code, (string Key, string Action)[] hints)
    {
        Title = title;
        Heading = $" {heading} ";
        Code = code;
        _hints = hints;
        FileName = $"{GetType().Name}.cs";
        FileLabel = $" {FileName} ";
        CodeLines = CodePanel.LineCount(code);
        CodeWidth = CodePanel.Width(code);
    }

    public string Title { get; }

    /// <summary>The live panel's title, padded for a border.</summary>
    public string Heading { get; }

    public string Code { get; }

    /// <summary>The source file the code comes from (Pages/<c>FileName</c>).</summary>
    public string FileName { get; }

    /// <summary>The code panel's footer, padded for a border.</summary>
    internal string FileLabel { get; }

    public int CodeLines { get; }

    /// <summary>Columns of the widest code line.</summary>
    public int CodeWidth { get; }

    /// <summary>The page's keys for the hints row; a page whose keys change with its state overrides it.</summary>
    internal virtual (string Key, string Action)[] Hints => _hints;

    /// <summary>
    /// The page takes every key before the gallery does: a text field is being edited, or a dropdown or menu is open.
    /// Only Ctrl+C and Alt+1…9 stay global, so typing 'q' or '[' types it.
    /// </summary>
    public virtual bool WantsKeys => false;

    /// <summary>Draw the live widget into <paramref name="area"/>, the inside of the page's panel.</summary>
    public abstract void Render(CellBuffer buffer, Rect area, long nowMs);

    /// <summary>Input, a tick or a paste; returns whether the page used it.</summary>
    public virtual bool Handle(Event ev, long nowMs) => false;

    /// <summary>True while the page animates on its own, so the loop draws frames without input.</summary>
    public virtual bool IsAnimating(long nowMs) => false;
}
