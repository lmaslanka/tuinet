namespace Tuinet;

internal static class Theme
{
    public static Style Screen { get; } = new(Color.FromRgb(200, 210, 215), Color.FromRgb(20, 24, 28));
    public static Style Label { get; } = new(Color.FromRgb(140, 160, 170), Color.FromRgb(20, 24, 28));
    public static Style Title { get; } = new(Color.FromRgb(220, 220, 220), Color.FromRgb(20, 24, 28));
    public static Style Status { get; } = new(Color.FromRgb(140, 160, 170), Color.FromRgb(20, 24, 28));
    public static Style FieldFocus { get; } = new(Color.FromRgb(16, 16, 16), Color.FromRgb(0, 200, 200));
    public static Style FieldIdle { get; } = new(Color.FromRgb(200, 220, 220), Color.FromRgb(40, 70, 80));
    public static Style OutlineFocus { get; } = new(Color.FromRgb(0, 200, 200), Color.FromRgb(20, 24, 28));
    public static Style Caret { get; } = new(Color.FromRgb(0, 200, 200), Color.FromRgb(16, 16, 16));
    public static Style ListCursor { get; } = new(Color.FromRgb(16, 16, 16), Color.FromRgb(200, 200, 200));
}
