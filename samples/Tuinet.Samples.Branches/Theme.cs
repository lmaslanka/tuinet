namespace Tuinet.Samples.Branches;

internal static class Theme
{
    private static readonly Color Background = Color.Rgb(20, 24, 28);

    public static readonly Style Screen = new(Color.Rgb(200, 210, 215), Background);
    public static readonly Style Label = new(Color.Rgb(140, 160, 170), Background);
    public static readonly Style Title = new(Color.Rgb(220, 220, 220), Background);
    public static readonly Style Status = new(Color.Rgb(140, 160, 170), Background);
    public static readonly Style FieldFocus = new(Color.Rgb(16, 16, 16), Color.Rgb(0, 200, 200));
    public static readonly Style FieldIdle = new(Color.Rgb(200, 220, 220), Color.Rgb(40, 70, 80));
    public static readonly Style OutlineFocus = new(Color.Rgb(0, 200, 200), Background);
    public static readonly Style ListCursor = new(Color.Rgb(16, 16, 16), Color.Rgb(200, 200, 200));
}
