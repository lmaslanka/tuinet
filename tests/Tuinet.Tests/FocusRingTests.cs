namespace Tuinet.Tests;

public class FocusRingTests
{
    [Fact]
    public void Next_and_previous_wrap_around()
    {
        var ring = new FocusRing(3);
        Assert.Equal(0, ring.Current);
        ring.Previous();
        Assert.Equal(2, ring.Current);
        ring.Next();
        Assert.Equal(0, ring.Current);
        ring.Next();
        ring.Next();
        ring.Next();
        Assert.Equal(0, ring.Current);
    }

    [Fact]
    public void Move_wraps_any_distance()
    {
        var ring = new FocusRing(4, current: 1);
        ring.Move(10);
        Assert.Equal(3, ring.Current);
        ring.Move(-7);
        Assert.Equal(0, ring.Current);
        ring.Move(int.MinValue);
        Assert.Equal(0, ring.Current);   // int.MinValue is a multiple of 4
        ring.Move(int.MaxValue);
        Assert.Equal(3, ring.Current);
    }

    [Fact]
    public void Is_marks_only_the_focused_control()
    {
        var ring = new FocusRing(3, current: 1);
        Assert.False(ring.Is(0));
        Assert.True(ring.Is(1));
        Assert.False(ring.Is(2));
        Assert.False(ring.Is(-1));
    }

    [Fact]
    public void Tab_and_shift_tab_move_focus()
    {
        var ring = new FocusRing(3);
        Assert.True(ring.Handle(new KeyEvent(KeyCode.Tab)));
        Assert.Equal(1, ring.Current);
        Assert.True(ring.Handle(new KeyEvent(KeyCode.Tab, modifiers: Modifiers.Shift)));
        Assert.True(ring.Handle(new KeyEvent(KeyCode.Tab, modifiers: Modifiers.Shift)));
        Assert.Equal(2, ring.Current);
    }

    [Fact]
    public void Other_keys_and_releases_leave_focus_alone()
    {
        var ring = new FocusRing(3);
        Assert.False(ring.Handle(new KeyEvent(KeyCode.Enter)));
        Assert.False(ring.Handle(KeyEvent.Char('j')));
        Assert.False(ring.Handle(new KeyEvent(KeyCode.Tab, modifiers: Modifiers.Ctrl)));
        Assert.False(ring.Handle(new KeyEvent(KeyCode.Tab, kind: KeyKind.Release)));   // the press already moved it
        Assert.Equal(0, ring.Current);
    }

    [Fact]
    public void A_click_focuses_the_control_under_it()
    {
        var ring = new FocusRing(3);
        Rect[] areas = [new(0, 0, 10, 1), new(0, 2, 10, 1), new(12, 2, 6, 1)];

        Assert.True(ring.HandleMouse(Click(14, 2), areas));
        Assert.Equal(2, ring.Current);
        Assert.False(ring.HandleMouse(Click(11, 2), areas));   // between controls
        Assert.False(ring.HandleMouse(new MouseEvent(MouseKind.Down, MouseButton.Right, 1, 0, Modifiers.None), areas));
        Assert.False(ring.HandleMouse(new MouseEvent(MouseKind.Move, MouseButton.None, 1, 0, Modifiers.None), areas));
        Assert.Equal(2, ring.Current);
    }

    [Fact]
    public void Areas_past_the_count_are_ignored()
    {
        var ring = new FocusRing(1);
        Rect[] areas = [new(0, 0, 4, 1), new(0, 1, 4, 1)];
        Assert.False(ring.HandleMouse(Click(1, 1), areas));
        Assert.Equal(0, ring.Current);
    }

    [Fact]
    public void Current_must_be_in_range()
    {
        var ring = new FocusRing(3);
        ring.Current = 2;
        Assert.True(ring.Is(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Current = 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Current = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusRing(3, current: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FocusRing(-1));
    }

    [Fact]
    public void An_empty_ring_focuses_nothing()
    {
        FocusRing ring = default;
        Assert.Equal(0, ring.Count);
        Assert.False(ring.Is(0));
        ring.Next();
        ring.Move(5);
        Assert.False(ring.Handle(new KeyEvent(KeyCode.Tab)));   // Tab is the app's
        Assert.Equal(0, ring.Current);
        Assert.False(ring.HandleMouse(Click(0, 0), [new(0, 0, 4, 1)]));
    }

    private static MouseEvent Click(int x, int y) => new(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None);
}
