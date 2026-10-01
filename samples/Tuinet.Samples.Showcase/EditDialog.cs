using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

public enum DialogResult
{
    Open,
    Saved,
    Closed,
}

/// <summary>Edits one <see cref="Item"/>: three text boxes, two dropdowns, two checkboxes, Save / Close.</summary>
public sealed class EditDialog
{
    public const int Name = 0;
    public const int Owner = 1;
    public const int Description = 2;
    public const int Kind = 3;
    public const int Priority = 4;
    public const int Enabled = 5;
    public const int Notify = 6;
    public const int Save = 7;
    public const int Close = 8;
    private const int FocusCount = 9;

    private readonly Item _item;
    private readonly int _number;
    private TextInputState _name;
    private TextInputState _owner;
    private TextInputState _description;
    private DropdownState _kind;
    private DropdownState _priority;
    private bool _enabled;
    private bool _notify;
    private string _status = "";
    private bool _nameMissing;

    public EditDialog(Item item, int number)
    {
        _item = item;
        _number = number;
        _name = new TextInputState(item.Name);
        _owner = new TextInputState(item.Owner);
        _description = new TextInputState(item.Description);
        _kind = new DropdownState(item.Kind);
        _priority = new DropdownState(item.Priority);
        _enabled = item.Enabled;
        _notify = item.Notify;
    }

    public int Focus { get; private set; }

    public DialogResult Handle(Event ev)
    {
        if (ev.Kind == EventKind.Paste && FocusedText() is { } pasteTarget)
        {
            pasteTarget.Insert(ev.Paste);
            return DialogResult.Open;
        }

        if (ev.Kind != EventKind.Key)
        {
            return DialogResult.Open;
        }

        KeyEvent key = ev.Key;
        if (key.IsCtrl('s'))
        {
            return TrySave();
        }

        // An open dropdown owns the keyboard until it closes.
        if (Focus == Kind && _kind.IsOpen)
        {
            _kind.Handle(key, Item.Kinds.Length);
            return DialogResult.Open;
        }

        if (Focus == Priority && _priority.IsOpen)
        {
            _priority.Handle(key, Item.Priorities.Length);
            return DialogResult.Open;
        }

        if (key.Is(KeyCode.Escape))
        {
            return DialogResult.Closed;
        }

        if (key.Code == KeyCode.Tab)
        {
            Move((key.Modifiers & Modifiers.Shift) != 0 ? -1 : 1);
            return DialogResult.Open;
        }

        if (FocusedText() is { } text)
        {
            if (key.Is(KeyCode.Enter) || key.Is(KeyCode.Down))
            {
                Move(1);
            }
            else if (key.Is(KeyCode.Up))
            {
                Move(-1);
            }
            else
            {
                text.Handle(key);
                if (Focus == Name && !text.IsEmpty)
                {
                    _nameMissing = false;
                }
            }

            return DialogResult.Open;
        }

        bool activate = key.Is(KeyCode.Enter) || key.IsChar(' ');
        switch (Focus)
        {
            case Kind:
                _kind.Handle(key, Item.Kinds.Length);
                break;
            case Priority:
                _priority.Handle(key, Item.Priorities.Length);
                break;
            case Enabled when activate:
                _enabled = !_enabled;
                break;
            case Notify when activate:
                _notify = !_notify;
                break;
            case Save when activate:
                return TrySave();
            case Close when activate:
                return DialogResult.Closed;
            default:
                if (key.Is(KeyCode.Up) || key.Is(KeyCode.Left))
                {
                    Move(-1);
                }
                else if (key.Is(KeyCode.Down) || key.Is(KeyCode.Right))
                {
                    Move(1);
                }

                break;
        }

        return DialogResult.Open;
    }

    public void Render(CellBuffer buffer)
    {
        Rect dialog = buffer.Area.Centered(64, 20);
        buffer.Render(new Clear(Theme.Dialog), dialog);

        Span<char> title = stackalloc char[24];
        title.TryWrite($" EDIT ITEM · {_number:D2} ", out int titleLength);
        var frame = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Amber),
            Title = title[..titleLength],
            TitleStyle = Theme.Heading(Theme.Amber),
            Footer = " tab next · ctrl+s save · esc close ",
            FooterAlignment = Alignment.Right,
            Style = Theme.Dialog,
        };
        buffer.Render(frame, dialog);
        Rect inner = frame.Inner(dialog).Inset(2, 1);

        Span<Rect> rows = stackalloc Rect[8];
        Layout.Vertical(inner,
        [
            Constraint.Length(3), Constraint.Length(3), Constraint.Length(3), Constraint.Length(3),
            Constraint.Length(1), Constraint.Length(1), Constraint.Length(1), Constraint.Length(1),
        ], rows);

        RenderText(buffer, rows[0], " name ", ref _name, Name, _nameMissing ? "required" : null);
        RenderText(buffer, rows[1], " owner ", ref _owner, Owner, null);
        RenderText(buffer, rows[2], " description ", ref _description, Description, null);

        Span<Rect> pickers = stackalloc Rect[2];
        Layout.Horizontal(rows[3], [Constraint.Fill(), Constraint.Fill()], pickers, spacing: 2);
        Rect kindBox = RenderPickerBox(buffer, pickers[0], " kind ", Kind);
        Rect priorityBox = RenderPickerBox(buffer, pickers[1], " priority ", Priority);
        Dropdown<Options> kind = Picker(Item.Kinds, kindColors: true);
        Dropdown<Options> priority = Picker(Item.Priorities, kindColors: false);
        kind.Render(kindBox, buffer, ref _kind);
        priority.Render(priorityBox, buffer, ref _priority);

        Span<Rect> checks = stackalloc Rect[2];
        Layout.Horizontal(rows[5], [Constraint.Fill(), Constraint.Fill()], checks, spacing: 2);
        RenderCheckbox(buffer, checks[0], "enabled", _enabled, Enabled);
        RenderCheckbox(buffer, checks[1], "notify on finish", _notify, Notify);

        int x = rows[7].X;
        Style idle = new(Theme.Text, Theme.Raised);
        buffer.Render(new Button("Save") { Style = idle, FocusedStyle = new Style(Theme.Bg, Theme.Green, Attr.Bold), Focused = Focus == Save }, new Rect(x, rows[7].Y, 8, 1));
        buffer.Render(new Button("Close") { Style = idle, FocusedStyle = new Style(Theme.Bg, Theme.Coral, Attr.Bold), Focused = Focus == Close }, new Rect(x + 10, rows[7].Y, 9, 1));
        if (_status.Length > 0)
        {
            buffer.SetString(x + 21, rows[7].Y, _status, Theme.Accent(Theme.Coral), rows[7].Right - x - 21, Overflow.Ellipsis);
        }

        // Popups last, so they sit on top of everything else in the dialog.
        kind.RenderPopup(new Rect(pickers[0].X, pickers[0].Bottom - 1, pickers[0].Width, 1), buffer, ref _kind);
        priority.RenderPopup(new Rect(pickers[1].X, pickers[1].Bottom - 1, pickers[1].Width, 1), buffer, ref _priority);
    }

    private TextInputState? FocusedText() => Focus switch
    {
        Name => _name,
        Owner => _owner,
        Description => _description,
        _ => null,
    };

    private void Move(int delta)
    {
        _kind.Close();
        _priority.Close();
        Focus = (Focus + delta + FocusCount) % FocusCount;
    }

    private DialogResult TrySave()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _nameMissing = true;
            _status = "name is required";
            Focus = Name;
            return DialogResult.Open;
        }

        _item.Name = name;
        _item.Owner = _owner.Text.Trim();
        _item.Description = _description.Text.Trim();
        _item.Kind = _kind.Selected;
        _item.Priority = _priority.Selected;
        _item.Enabled = _enabled;
        _item.Notify = _notify;
        return DialogResult.Saved;
    }

    private Block Outline(ReadOnlySpan<char> label, int field, ReadOnlySpan<char> error)
    {
        bool focused = Focus == field;
        Color border = !error.IsEmpty ? Theme.Coral : focused ? Theme.Blue : Theme.Faint;
        return new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(border),
            Title = label,
            TitleStyle = focused ? Theme.Heading(Theme.Blue) : Theme.Dim,
            Footer = error,
            FooterAlignment = Alignment.Right,
        };
    }

    private void RenderText(CellBuffer buffer, Rect box, ReadOnlySpan<char> label, ref TextInputState state, int field, string? error)
    {
        Block outline = Outline(label, field, error);
        buffer.Render(outline, box);
        buffer.Render(new TextInput { Focused = Focus == field, Style = Theme.Body, Placeholder = "…", PlaceholderStyle = Theme.Faded }, outline.Inner(box).Inset(1, 0), ref state);
    }

    private Rect RenderPickerBox(CellBuffer buffer, Rect box, ReadOnlySpan<char> label, int field)
    {
        Block outline = Outline(label, field, default);
        buffer.Render(outline, box);
        return outline.Inner(box).Inset(1, 0);
    }

    private void RenderCheckbox(CellBuffer buffer, Rect area, ReadOnlySpan<char> label, bool value, int field) =>
        // ▐█▌ is a filled square: 2 cells wide, 1 row tall (cells are twice as tall as wide).
        // Checked fills it green; unchecked leaves a dim slot.
        buffer.Render(new Checkbox(label, value)
        {
            Focused = Focus == field,
            Style = value ? Theme.Body : Theme.Dim,
            FocusedStyle = Theme.Heading(Theme.Blue),
            CheckedSymbol = "▐█▌",
            UncheckedSymbol = "▐█▌",
            CheckedStyle = new Style(Theme.Green, default, Attr.None),
            UncheckedStyle = new Style(Theme.Faint, default, Attr.None),
        }, area);

    private static Dropdown<Options> Picker(string[] options, bool kindColors) => new(new Options(options, kindColors))
    {
        PopupStyle = new Style(Theme.Text, Theme.Raised),
        PopupBordered = true,
        PopupBorder = BorderType.Rounded,
        PopupBorderStyle = new Style(Theme.Blue, Theme.Raised),
        SelectedStyle = new Style(Theme.Bg, Theme.Blue, Attr.Bold),
    };

    /// <summary>Dropdown options, each with its accent dot.</summary>
    private readonly struct Options(string[] names, bool kindColors) : IListSource
    {
        public int Count => names.Length;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
        {
            Color color = kindColors ? Theme.KindColor(index) : Theme.PriorityColor(index);
            int x = buffer.SetString(area.X, area.Y, "● ", Theme.Accent(color), area.Width);
            buffer.SetString(x, area.Y, names[index], Theme.Body, area.Right - x, Overflow.Ellipsis);
        }
    }
}
