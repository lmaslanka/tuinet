# screenshots

Regenerates the README images in `docs/images/`: each *shot* runs an app with some input, draws one frame into a
`CellBuffer`, and renders that buffer as a PNG of a terminal window (rounded frame, traffic-light dots, title).

```sh
dotnet run --project tools/screenshots                  # writes docs/images/showcase-*.png and gallery-*.png
dotnet run --project tools/screenshots -- out/          # somewhere else
dotnet run --project tools/screenshots -- --only showcase-edit
dotnet run --project tools/screenshots -- --scale 2     # high-density images, named name@2x.png
dotnet run --project tools/screenshots -- --check       # writes nothing; exit code 1 if not deterministic or stale
dotnet run --project tools/screenshots -- --sheet       # renderer-sheet.png: everything the renderer draws
```

`--check` renders every shot twice from scratch and compares the bytes with each other and with the file in the
output directory, so it catches both nondeterminism and images that no longer match the app.

## Adding a shot

The shots are a table at the top of `Program.cs`, one line each:

```csharp
new("showcase-edit", ShowcaseTitle, 110, 34, Showcase, Input(Char('j'), Key(KeyCode.Enter))),
//   file name         title bar      size     app      setup                                  (, NowMs: 4650)
```

- **app**: a `Func<IApp>` that makes a fresh app. Apps that don't implement `IApp` wrap their two methods in a
  `DelegateApp`, as `TestSheet` does.
- **setup**: an `Action<IApp>` run before the frame. `Keys("jjp")` types characters, `Input(...)` sends any events
  (`Key(KeyCode.Tab)`, `Char('s', Modifiers.Ctrl)`, `Event.FromPaste(...)`, mouse events) at time 0; or write a lambda.
- **NowMs**: the time the frame is drawn at, for animations (the progress dialog is shown 4.65 s in).

To add another sample, reference its project from `Screenshots.csproj` and add its lines to the table.

## How it renders

`ScreenshotRenderer` takes a `CellBuffer`, a title and `ScreenshotOptions` (cell size, font size, padding, default
colors, palette, line width, `Scale`, `Window: false` for just the grid):

- Colors: RGB as is; indexed 0-15 from the options' ANSI palette (GitHub Dark by default), 16-255 from the xterm cube
  and grey ramp; default colors from the options. Reverse swaps, Dim blends toward the background.
- Bold, italic and bold italic use their own font faces; underline and strikethrough are drawn lines; hidden cells
  draw only their background.
- Wide glyphs are centered across both cells; grapheme clusters (`Cell.Text`) go to the font as one string.
- Box drawing (U+2500-257F, including rounded corners, heavy, double and dashed lines), block elements (U+2580-259F,
  eighths, quadrants and shades) and braille (U+2800-28FF) are drawn as shapes, as terminals do, so borders and bars
  join without gaps.
- The cursor, if visible, is drawn in its shape: bar, underline, or a block (inverted cell).

Glyphs it has no font for are listed on stderr and drawn as the font's missing-glyph box.

## Determinism

The same input gives byte-identical PNGs on every machine (checked here with hardware intrinsics on and off):

- Fonts come from `fonts/`, never from the system.
- Glyph outlines (from SixLabors.Fonts, hinted) are rasterized by our own coverage rasterizer (`Rasterizer.cs`):
  scalar float math, points snapped to 1/64 px, no trigonometry. Blending is integer.
- The PNG encoder (`Png.cs`, `Deflate.cs`) is ours too: no time or metadata chunks, and its own deflate, since zlib
  builds may choose different matches on different CPUs.

## Fonts and licenses

- `fonts/JetBrainsMono-*.ttf`: [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) 2.304, SIL Open Font
  License 1.1 (`fonts/OFL.txt`). It has no CJK or emoji: any other `.ttf`/`.otf` dropped into `fonts/` is used as a
  fallback, in file name order (e.g. Noto Emoji, OFL, for monochrome emoji).
- [SixLabors.Fonts](https://github.com/SixLabors/Fonts) 2.1.3 reads the fonts: Six Labors Split License, which grants
  Apache-2.0 for use in open-source software such as Tuinet. It is pinned to 2.x on purpose: later versions (and
  ImageSharp 4) check for a license key at build time, and ImageSharp 3.x has open security advisories, so the
  drawing and PNG encoding here don't use ImageSharp at all.
