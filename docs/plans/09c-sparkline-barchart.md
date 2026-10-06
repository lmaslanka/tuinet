# 09c · Sparkline and bar chart

**Type:** missing · **Effort:** M · **Priority:** 5 · **Depends on:** 09b (Showcase Stats page)

## Problem
There's no way to draw a value over time or compare values. Dashboards, the Stress sample's frame times and
the Showcase's bytes per frame (a product metric) have no visual form.

## Design
### `Sparkline`
```csharp
public readonly ref struct Sparkline : IWidget
{
    public Sparkline(ReadOnlySpan<double> values, ReadOnlySpan<double> more = default);
    public double Max { get; init; } = double.NaN;   // NaN: the largest visible value
    public Style Style { get; init; }
    public Style MaxStyle { get; init; }            // optional highlight for the peak bar
}
```
- **Eighth blocks** `▁▂▃▄▅▆▇█`. With a height of several rows the levels stack, so h rows give 8h levels.
  0 is blank; NaN and negative values leave a gap.
- **The newest value is on the right.** Given more values than columns, it shows the last `Width` values.
- **Two spans** let an app-owned ring buffer be drawn without a copy (older part, then newer part). Apps
  usually write `new Sparkline(ring.Older, ring.Newer)`. No ring buffer type is added to the library.
- The automatic `Max` is computed over the visible values only, so an old spike scrolls out instead of
  keeping the scale squashed.

### `BarChart`
```csharp
public readonly record struct Bar(double Value, string Label = "", Style Style = default);

public readonly ref struct BarChart : IWidget
{
    public BarChart(ReadOnlySpan<Bar> bars);
    public Direction Direction { get; init; }       // Vertical: bars grow up; Horizontal: bars grow right
    public double Max { get; init; } = double.NaN;
    public int BarWidth { get; init; } = 3;         // vertical only
    public int Gap { get; init; } = 1;
    public Style BarStyle { get; init; }            // under each Bar.Style
    public Style LabelStyle { get; init; }
    public Style ValueStyle { get; init; }
    public string? ValueFormat { get; init; } = "0"; // null hides values; formatted with TryFormat into stackalloc
}
```
- **Vertical:** bars from the bottom with eighth-block tops. The label is under the bar, cut to `BarWidth`.
  The value goes on the row above the bar, or inside its top when the bar is full height.
- **Horizontal:** a label column as wide as the widest label (at most a third of the width), then a bar with
  a left-eighths end (`▏▎▍▌▋▊▉█`), then the value after it.
- Bars that don't fit are dropped from the end, and the chart never squashes them below `BarWidth`.
- The shared eighth-block math moves into one internal `Eighths` helper, used by `ProgressBar`,
  `Scrollbar`, `Sparkline` and `BarChart`.

## Showcase and samples
- **Stats page** (from 09b): a vertical `BarChart` of items per kind colored with `Theme.KindColor`, a
  horizontal one per priority colored with `PriorityColor`, and two `Sparkline`s, bytes per frame and frame
  time, over a 120-entry ring in the app. When you move through the list, the byte cost of each frame shows
  up live.
- **Stress sample:** a one-row frame-time sparkline at the right end of the status bar.

## Files
`Widgets/Sparkline.cs`, `Widgets/BarChart.cs`, `Widgets/Eighths.cs` (internal), `ProgressBar.cs`/`Scrollbar.cs`
(use the shared helper), Showcase, Stress sample, `PublicAPI.Unshipped.txt`, README, CHANGELOG.

## Tests
- Sparkline: levels for known values (0, max, ½, ⅛), multi-row stacking, fixed vs automatic `Max`, NaN and
  negative gaps, more values than columns (shows the tail), two spans give the same result as one joined span,
  all-zero input, width 0.
- BarChart: vertical and horizontal partial bars, labels cut at a wide glyph, `ValueFormat` null/"0"/"F1",
  overflow drops bars, the label column cap, `Max` smaller than a value (clamped).
- `ProgressBar` and `Scrollbar` render the same bytes as before the shared helper (their tests are unchanged).
- AllocationTests for a frame with both charts and value labels: 0 B.

## Non-goals
Braille line or scatter charts, axes, grouped or stacked bars, and negative values (all left for a later plan).
