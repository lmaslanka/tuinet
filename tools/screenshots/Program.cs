// Regenerates the README screenshots: draws each shot's app into a CellBuffer and renders that as a PNG.
//   dotnet run --project tools/screenshots -- [outdir] [--only name,...] [--scale n] [--check] [--sheet]
// outdir defaults to docs/images. --check writes nothing: it renders every shot twice, fails if the two differ (not
// deterministic) or if they differ from the file in outdir (stale). --sheet renders a sheet of everything the renderer
// draws instead of the shots, for checking renderer changes.
using Tuinet;
using Tuinet.Samples.Gallery;
using Tuinet.Samples.Showcase;
using Tuinet.Screenshots;
using static Tuinet.Screenshots.Shot;

const string ShowcaseTitle = "tuinet · showcase";
static IApp Showcase() => new ShowcaseApp();

const string GalleryTitle = "tuinet · gallery";
static IApp Gallery() => new GalleryApp();

// Gallery setup: show page T, then run the rest of the setup.
static Action<IApp> GalleryPage<T>(Action<IApp>? then = null) where T : Page => app =>
{
    ((GalleryApp)app).Show<T>();
    then?.Invoke(app);
};

// Ticks every 100 ms up to ms, as Terminal.Run sends them while a page animates.
static Action<IApp> Ticks(long ms) => app =>
{
    for (long t = 100; t <= ms; t += 100)
    {
        app.Handle(Event.Tick, t);
    }
};

// The shots: name (file name), title bar, size, app, setup, frame time. One line per screenshot.
Shot[] shots =
[
    // The list with row 04 selected.
    new("showcase-main", ShowcaseTitle, 110, 34, Showcase, Keys("jjj")),
    // Row 02's edit form, focus moved to the priority dropdown and opened.
    new("showcase-edit", ShowcaseTitle, 110, 34, Showcase,
        Input(Char('j'), Key(KeyCode.Enter), Key(KeyCode.Tab), Key(KeyCode.Tab), Key(KeyCode.Tab), Key(KeyCode.Tab), Key(KeyCode.Enter))),
    // The progress dialog two thirds of the way through its run.
    new("showcase-progress", ShowcaseTitle, 110, 34, Showcase, Keys("jjp"), NowMs: 4650),
    // Gallery pages: the live widget beside the code that draws it.
    new("gallery-table", GalleryTitle, 110, 34, Gallery, GalleryPage<TablePage>()),
    new("gallery-forms", GalleryTitle, 110, 34, Gallery, GalleryPage<FormsPage>()),
    new("gallery-charts", GalleryTitle, 110, 34, Gallery, GalleryPage<ChartsPage>(Ticks(8000)), NowMs: 8000),
    new("gallery-tree", GalleryTitle, 110, 34, Gallery, GalleryPage<TreePage>()),
];

string? outDir = null;
string[]? only = null;
int scale = 1;
bool check = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            check = true;
            break;
        case "--sheet":
            shots = [TestSheet.Shot];
            break;
        case "--only" when i + 1 < args.Length:
            only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            break;
        case "--scale" when i + 1 < args.Length && int.TryParse(args[i + 1], out int s) && s >= 1:
            scale = s;
            i++;
            break;
        case var arg when !arg.StartsWith("--", StringComparison.Ordinal) && outDir is null:
            outDir = arg;
            break;
        default:
            Console.Error.WriteLine("usage: screenshots [outdir] [--only name,...] [--scale n] [--check] [--sheet]");
            return 2;
    }
}

outDir ??= Path.Combine(RepoRoot(), "docs", "images");
Directory.CreateDirectory(outDir);
TerminalFonts fonts = TerminalFonts.Bundled();
var options = new ScreenshotOptions { Scale = scale };
string suffix = scale == 1 ? "" : $"@{scale}x";
int failures = 0;

foreach (Shot shot in shots)
{
    if (only is not null && !only.Contains(shot.Name))
    {
        continue;
    }

    var renderer = new ScreenshotRenderer(fonts, options);
    byte[] png = renderer.RenderPng(shot.Render(), shot.Title);
    string path = Path.Combine(outDir, shot.Name + suffix + ".png");
    foreach (string text in renderer.Missing)
    {
        Console.Error.WriteLine($"{shot.Name}: no glyph for \"{text}\" (U+{char.ConvertToUtf32(text, 0):X4})");
    }

    if (check)
    {
        // A fresh app and renderer: nothing cached may change the output.
        byte[] again = new ScreenshotRenderer(fonts, options).RenderPng(shot.Render(), shot.Title);
        bool deterministic = png.AsSpan().SequenceEqual(again);
        bool current = File.Exists(path) && png.AsSpan().SequenceEqual(File.ReadAllBytes(path));
        Console.WriteLine($"{shot.Name}: {(deterministic ? "deterministic" : "NOT DETERMINISTIC")}, {(current ? "up to date" : "differs from " + path)}");
        failures += deterministic && current ? 0 : 1;
    }
    else
    {
        File.WriteAllBytes(path, png);
        Console.WriteLine($"{path} ({png.Length:N0} bytes)");
    }
}

return failures == 0 ? 0 : 1;

// The directory holding tuinet.slnx, from the working directory up.
static string RepoRoot()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "tuinet.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run from inside the Tuinet repository, or pass the output directory.");
}
