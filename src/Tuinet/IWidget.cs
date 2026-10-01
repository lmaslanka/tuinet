namespace Tuinet;

/// <summary>
/// An immediate-mode widget: a value describing what to draw, rendered into an area of a buffer.
/// Widgets hold no state between frames; implement them as <c>readonly ref struct</c>s so they can
/// carry spans (e.g. <c>stackalloc</c>-formatted text) at zero allocation cost.
/// </summary>
public interface IWidget
{
    void Render(Rect area, CellBuffer buffer);
}

/// <summary>A widget whose render reads and updates app-owned state (selection, scroll, caret).</summary>
public interface IStatefulWidget<TState>
    where TState : allows ref struct
{
    void Render(Rect area, CellBuffer buffer, ref TState state);
}

public static class WidgetExtensions
{
    public static void Render<TWidget>(this CellBuffer buffer, TWidget widget, Rect area)
        where TWidget : IWidget, allows ref struct =>
        widget.Render(area, buffer);

    public static void Render<TWidget, TState>(this CellBuffer buffer, TWidget widget, Rect area, ref TState state)
        where TWidget : IStatefulWidget<TState>, allows ref struct
        where TState : allows ref struct =>
        widget.Render(area, buffer, ref state);
}
