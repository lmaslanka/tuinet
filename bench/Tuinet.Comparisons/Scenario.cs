namespace Tuinet.Comparisons;

/// <summary>
/// The one scenario every library draws: a 200×60 screen with a rounded, titled border around a
/// 5,000-row list and a status line under it. Each frame moves the selection down one row; once the
/// selection is past the viewport, every frame also scrolls the list by one row.
/// </summary>
internal static class Scenario
{
    public const int Width = 200;
    public const int Height = 60;
    public const string Title = "items";
    public const string Status = "j/k move  q quit";

    /// <summary>List rows inside the border: the screen less the status line and two border rows.</summary>
    public const int Viewport = Height - 1 - 2;

    /// <summary>First selection of a measured run: far enough down that every frame scrolls.</summary>
    public const int FirstSelected = Viewport + 40;

    // True-color RGB, the same in every library. Text and border use the terminal's default background.
    public static readonly (byte R, byte G, byte B) Text = (204, 204, 204);
    public static readonly (byte R, byte G, byte B) Border = (110, 110, 130);
    public static readonly (byte R, byte G, byte B) SelectedFg = (16, 16, 16);
    public static readonly (byte R, byte G, byte B) SelectedBg = (97, 175, 239);
    public static readonly (byte R, byte G, byte B) StatusFg = (140, 140, 140);

    private static readonly string[] Names = ["alpha-service", "billing", "catalog-api", "deploy-bot", "edge-proxy", "feature-flags", "gateway"];
    private static readonly string[] Kinds = ["service", "job", "cron", "worker"];
    private static readonly string[] Owners = ["platform", "payments", "search", "infra", "growth"];

    /// <summary>5,000 rows such as <c>row 00042  alpha-service   service   platform</c>, built once and shared.</summary>
    public static readonly string[] Items =
        [.. Enumerable.Range(0, 5000).Select(i => $"row {i:D5}  {Names[i % Names.Length],-16}  {Kinds[i % Kinds.Length],-8}  {Owners[i % Owners.Length],-10}")];

    /// <summary>Selection for frame <paramref name="frame"/>: one row further each frame, wrapping at the end.</summary>
    public static int Selected(long frame) => (int)((FirstSelected + frame) % Items.Length);
}
