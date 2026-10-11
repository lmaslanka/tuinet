using Tuinet;
using Tuinet.Samples.Gallery;
using Tuinet.Testing;

namespace Tuinet.Samples.Gallery.Tests;

/// <summary>
/// Visual regression check: every gallery page, colors included, against __snapshots__/page-NN-title.txt. A change to a
/// widget's drawing shows up here as a diff; after a deliberate change rerun with TUINET_ACCEPT_SNAPSHOTS=1 and review.
/// </summary>
public class GallerySnapshotTests
{
    [Fact]
    public void Every_page_matches_its_snapshot()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        var failures = new List<string>();
        for (int page = 0; page < app.Pages.Count; page++)
        {
            app.Show(page);
            term.Render();
            try
            {
                Snapshot.Verify(term.Screen, $"page-{page + 1:D2}-{app.Current.Title.Trim().ToLowerInvariant()}");
            }
            catch (ScreenAssertionException e)
            {
                failures.Add(e.Message);   // check every page, then report them all
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void Narrow_screen_stacks_the_code_under_the_widget()
    {
        using var term = new TestTerminal(new GalleryApp(), 80, 24);
        Snapshot.Verify(term.Screen, "narrow-layout", styled: false);
    }
}
