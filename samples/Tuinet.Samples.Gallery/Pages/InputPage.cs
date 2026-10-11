using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="TextInput"/> (plain with a placeholder, and masked) and <see cref="TextArea"/> with line numbers. Enter, Tab
/// or a click starts editing: every key then goes to the field (Tab moves on, Esc stops), so 'q' and '[' type.
/// Selection, undo/redo, word moves and paste all work.
/// </summary>
public sealed class InputPage() : Page("Input", "TextInput · TextArea", Snippet, Keys)
{
    private const string Snippet = """
        private TextInputState _password = new(mask: '•');
        // …
        buffer.Render(new TextInput
        {
            Focused = Editing(Name),
            Style = Theme.Field,
            Placeholder = "your name",
            PlaceholderStyle = Theme.Faded,
        }, _targets[Name], ref _name);
        buffer.Render(new TextInput
        {
            Focused = Editing(Password),
            Style = Theme.Field,
        }, _targets[Password], ref _password);
        buffer.Render(new TextArea
        {
            Focused = Editing(Notes),
            Style = Theme.Field,
            LineNumbers = true,
            LineNumberStyle = Theme.Faded,
        }, _targets[Notes], ref _notes);
        // …
        case Notes: _notes.Handle(key); break;
        """;

    private static readonly (string, string)[] Keys = [("enter", "edit"), ("tab", "next field")];
    private static readonly (string, string)[] EditingKeys = [("esc", "done"), ("tab", "next field"), ("ctrl+z", "undo")];

    public const int Name = 0;
    public const int Password = 1;
    public const int Notes = 2;

    private readonly Rect[] _targets = new Rect[3];
    private FocusRing _focus = new(3);
    private bool _editing;
    private TextInputState _name = new();
    private TextInputState _password = new(mask: '•');
    private TextAreaState _notes = new("Multi-line text: Enter breaks the line,\nShift+arrows select, Ctrl+Z undoes.");

    public int Focus => _focus.Current;
    public bool IsEditing => _editing;
    public string NameText => _name.Text;
    public string NotesText => _notes.Text;

    public override bool WantsKeys => _editing;

    internal override (string Key, string Action)[] Hints => _editing ? EditingKeys : Keys;

    private bool Editing(int field) => _editing && _focus.Is(field);

    public override bool Handle(Event ev, long nowMs)
    {
        switch (ev.Kind)
        {
            case EventKind.Mouse:
                return HandleMouse(ev.Mouse);
            case EventKind.Paste when _editing:
                FocusedText().Insert(ev.Paste);   // the notes keep line breaks; one-line fields drop them
                return true;
            case EventKind.Key:
                return HandleKey(ev.Key);
            default:
                return false;
        }
    }

    private bool HandleKey(KeyEvent key)
    {
        if (!_editing)
        {
            if (_focus.Handle(key) || key.Is(KeyCode.Enter))
            {
                _editing = true;
                return true;
            }

            return false;
        }

        if (key.Is(KeyCode.Escape))
        {
            _editing = false;
        }
        else if (_focus.Handle(key)) { }   // Tab, Shift+Tab
        else
        {
            switch (_focus.Current)
            {
                case Notes: _notes.Handle(key); break;
                case Name: Type(_name, key); break;
                default: Type(_password, key); break;
            }
        }

        return true;
    }

    /// <summary>A one-row field: Enter moves to the next one.</summary>
    private void Type(TextInputState field, KeyEvent key)
    {
        if (key.Is(KeyCode.Enter))
        {
            _focus.Next();
        }
        else
        {
            field.Handle(key);
        }
    }

    /// <summary>A click on a field focuses it, starts editing and places the caret; a click elsewhere stops editing.</summary>
    private bool HandleMouse(MouseEvent mouse)
    {
        if (_focus.HandleMouse(mouse, _targets))
        {
            _editing = true;
        }
        else if (mouse.IsClick)
        {
            _editing = false;
            return false;
        }

        // Drags and the wheel go to the focused field, wherever the pointer is.
        return _editing && (_focus.Current switch
        {
            Name => _name.HandleMouse(mouse),
            Password => _password.HandleMouse(mouse),
            _ => _notes.HandleMouse(mouse),
        });
    }

    private EditableText FocusedText() => _focus.Current switch
    {
        Name => _name,
        Password => _password,
        _ => _notes,
    };

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // Each field under its label, three rows apart; the notes take what's left above the status line.
        _targets[Name] = Field(buffer, area, Name, "NAME");
        _targets[Password] = Field(buffer, area, Password, "PASSWORD");
        _targets[Notes] = Field(buffer, area, Notes, "NOTES");
        _targets[Notes] = new Rect(area.X, _targets[Notes].Y, area.Width, Math.Max(0, area.Bottom - 2 - _targets[Notes].Y));

        buffer.Render(new TextInput
        {
            Focused = Editing(Name),
            Style = Theme.Field,
            Placeholder = "your name",
            PlaceholderStyle = Theme.Faded,
        }, _targets[Name], ref _name);
        buffer.Render(new TextInput
        {
            Focused = Editing(Password),
            Style = Theme.Field,
        }, _targets[Password], ref _password);
        buffer.Render(new TextArea
        {
            Focused = Editing(Notes),
            Style = Theme.Field,
            LineNumbers = true,
            LineNumberStyle = Theme.Faded,
        }, _targets[Notes], ref _notes);

        // Where the caret is in the notes, and how much they hold.
        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[8]);
        status.Append("line ", Theme.Dim);
        status.Append(_notes.CaretLine + 1, Theme.Strong);
        status.Append(" · col ", Theme.Dim);
        status.Append(_notes.CaretColumn + 1, Theme.Strong);
        status.Append(" · ", Theme.Dim);
        status.Append(_notes.Length, Theme.Strong);
        status.Append(" chars", Theme.Dim);
        if (area.Height > 1)
        {
            buffer.SetText(area.X, area.Bottom - 1, status.Build(), area.Width);
        }
    }

    /// <summary>Field <paramref name="field"/>'s label (a heading while focused); returns the row under it.</summary>
    private Rect Field(CellBuffer buffer, Rect area, int field, ReadOnlySpan<char> label)
    {
        int y = area.Y + 3 * field;
        if (y >= area.Bottom)
        {
            return new Rect(area.X, area.Bottom, 0, 0);
        }

        bool focused = _focus.Is(field);
        buffer.SetString(area.X, y, label, focused ? Theme.Heading(_editing ? Theme.Green : Theme.Blue) : Theme.Dim, area.Width);
        return new Rect(area.X, y + 1, area.Width, 1).Intersect(area);
    }
}
