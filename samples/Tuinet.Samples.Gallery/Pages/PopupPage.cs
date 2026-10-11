using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Popup"/>: a confirm dialog centered on its content with <see cref="Popup.Outer"/>, over text that its
/// shadow darkens; and a small popup that a click places by the pointer with <see cref="Rect.PlaceNear"/>. Enter opens
/// (or confirms) the dialog, Esc or a click outside it closes it; s toggles the shadow, p the padding.
/// </summary>
public sealed class PopupPage : Page
{
    private const string Snippet = """
        private Popup Dialog() => new()
        {
            Block = new Block
            {
                BorderType = BorderType.Rounded,
                BorderStyle = Theme.Accent(Theme.Coral),
                Title = " confirm ",
                TitleStyle = Theme.Heading(Theme.Coral),
                Style = Theme.Dialog,
            },
            Shadow = _shadow,
            ShadowStyle = Theme.Shadow,
            Padding = _padding,
        };
        // …
        _dialog = area.Centered(Dialog().Outer(22, 2));
        buffer.Render(Dialog(), _dialog);
        // …
        if (m.IsClick && !m.IsIn(Dialog().Frame(_dialog)))
            _open = false;   // a click outside closes it
        """;

    private static readonly (string, string)[] Keys = [("enter", "dialog"), ("click", "popup here"), ("s", "shadow"), ("p", "padding")];
    private static readonly (string, string)[] OpenKeys = [("enter", "confirm"), ("esc, click outside", "close"), ("s", "shadow"), ("p", "padding")];

    private const string Background =
        "A popup draws over whatever is already on screen: a fill, a border and an optional drop shadow that darkens "
        + "the cells below and to the right of the box while keeping their glyphs, so this text stays readable "
        + "under it. The area you give it includes the shadow; Outer turns a content size into that area, so "
        + "centering on the content works, and Frame gives the box alone, for click-outside tests. PlaceNear puts a "
        + "popup next to an anchor: below it if it fits, else above, else on the side with more room, moved left to "
        + "stay on screen. A 0×0 anchor is a point, such as the mouse pointer. Click anywhere on this side to try it.";

    private bool _open;
    private bool _shadow = true;
    private int _padding = 1;
    private Rect _dialog;
    private Rect _area;
    private Rect? _point;   // where the last click placed the small popup
    private int _confirmed;

    public PopupPage() : base("Popup", "Popup · Rect.PlaceNear", Snippet, Keys)
    {
    }

    public bool IsOpen => _open;
    public bool HasPointPopup => _point is not null;
    public int Confirmed => _confirmed;

    internal override (string Key, string Action)[] Hints => _open ? OpenKeys : Keys;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Mouse)
        {
            return HandleMouse(ev.Mouse);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.Is(KeyCode.Enter))
        {
            _confirmed += _open ? 1 : 0;
            _open = !_open;
        }
        else if (key.Is(KeyCode.Escape) && (_open || _point is not null))
        {
            (_open, _point) = (false, null);
        }
        else if (key.IsChar('s')) _shadow = !_shadow;
        else if (key.IsChar('p')) _padding = (_padding + 1) % 3;
        else return false;

        return true;
    }

    private bool HandleMouse(MouseEvent m)
    {
        if (_open)
        {
            if (m.IsClick && !m.IsIn(Dialog().Frame(_dialog)))
                _open = false;   // a click outside closes it

            return m.IsClick;
        }

        if (!m.IsClickIn(_area))
        {
            return false;
        }

        _point = new Rect(m.X, m.Y, 0, 0);
        return true;
    }

    /// <summary>The dialog; render and clicks use the same one.</summary>
    private Popup Dialog() => new()
    {
        Block = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Coral),
            Title = " confirm ",
            TitleStyle = Theme.Heading(Theme.Coral),
            Style = Theme.Dialog,
        },
        Shadow = _shadow,
        ShadowStyle = Theme.Shadow,
        Padding = _padding,
    };

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        _area = area;
        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[6]);
        status.Append("shadow ", Theme.Dim);
        status.Append(_shadow ? "on" : "off", Theme.Strong);
        status.Append(" · padding ", Theme.Dim);
        status.Append(_padding, Theme.Strong);
        status.Append(" · confirmed ", Theme.Dim);
        status.Append(_confirmed, Theme.Strong);
        buffer.SetText(area.X, area.Y, status.Build(), area.Width);
        buffer.Render(new Paragraph(Background, Theme.Dim) { Wrap = TextWrap.Word },
            new Rect(area.X, area.Y + 2, area.Width, area.Height - 2));

        if (_point is Rect point)
        {
            // Sized for its text, by the pointer: below and right of it when there's room.
            var popup = new Popup
            {
                Block = new Block { BorderType = BorderType.Rounded, BorderStyle = Theme.Accent(Theme.Blue), Style = Theme.Dialog },
                Shadow = true,
                ShadowStyle = Theme.Shadow,
            };
            Span<char> text = stackalloc char[24];
            int length = new SpanText(text).Append("clicked ").Append(point.X - area.X).Append(',').Append(point.Y - area.Y).Text.Length;
            Rect box = area.PlaceNear(point, popup.Outer(length, 1));
            buffer.Render(popup, box);
            Rect inner = popup.Inner(box);
            buffer.SetString(inner.X, inner.Y, text[..length], Theme.Accent(Theme.Blue), inner.Width);
        }

        if (!_open)
        {
            return;
        }

        _dialog = area.Centered(Dialog().Outer(22, 2));
        buffer.Render(Dialog(), _dialog);
        Rect content = Dialog().Inner(_dialog);
        buffer.SetString(content.X, content.Y, "Delete 3 files?", Theme.Strong, content.Width);
        buffer.SetString(content.X, content.Y + 1, "enter yes · esc no", Theme.Dim, content.Width);
    }
}
