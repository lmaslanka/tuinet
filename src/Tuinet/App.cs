using System.Diagnostics;

namespace Tuinet;

/// <summary>
/// An app for <see cref="Terminal.Run"/>: it handles events and draws frames, and the loop decides when. Times are
/// milliseconds since <see cref="Terminal.Run"/> started.
/// </summary>
public interface IApp
{
    /// <summary>
    /// Update state for one event: input, a resize, a posted message, or <see cref="EventKind.Tick"/> when an animation
    /// frame or <see cref="NextDueMs"/> comes due. Return false to stop the loop.
    /// </summary>
    bool Handle(Event ev, long nowMs);

    /// <summary>Draw the whole UI into <paramref name="frame"/>, the cleared back buffer.</summary>
    void Render(CellBuffer frame, long nowMs);

    /// <summary>
    /// True while something moves on its own (a spinner, a progress bar): the loop then draws at the frame rate,
    /// with a <see cref="EventKind.Tick"/> before each frame that no input caused. False sleeps until input.
    /// </summary>
    bool IsAnimating(long nowMs) => false;

    /// <summary>
    /// When the app next needs a frame with no input (e.g. an <see cref="Alarm"/>'s <see cref="Alarm.DueMs"/> to
    /// hide a message), or <see cref="long.MaxValue"/> for never. The loop wakes then with a
    /// <see cref="EventKind.Tick"/>; a time that stays in the past wakes it on every pass.
    /// </summary>
    long NextDueMs(long nowMs) => long.MaxValue;
}

public sealed partial class Terminal
{
    /// <summary>Default frame interval for <see cref="Run"/> while animating: about 30 frames a second.</summary>
    public const int DefaultFrameMs = 33;

    /// <summary>
    /// Run <paramref name="app"/> until its <see cref="IApp.Handle"/> returns false: draw a frame, sleep until input
    /// (or the next animation frame, or <see cref="IApp.NextDueMs"/>), handle every event already waiting, draw once.
    /// A burst of key repeats costs one frame, and an idle app costs nothing. In inline mode, the last frame is drawn
    /// again after the app stops, since it stays on screen. Allocates nothing per frame; apps that need something
    /// else can keep writing their own loop with <see cref="Poll"/>, <see cref="BeginFrame"/> and <see cref="Present"/>.
    /// <code>using var term = Terminal.Open();
    /// term.Run(new MyApp());</code>
    /// </summary>
    /// <param name="app">Handles events and draws frames.</param>
    /// <param name="frameMs">Time between frames while <see cref="IApp.IsAnimating"/>; 0 draws as fast as the terminal takes them.</param>
    public void Run(IApp app, int frameMs = DefaultFrameMs)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfNegative(frameMs);
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            long frameAt = Elapsed(start);
            app.Render(BeginFrame(), frameAt);
            Present();

            long now = Elapsed(start);
            long wakeAt = app.NextDueMs(now);
            if (app.IsAnimating(now))
            {
                wakeAt = Math.Min(wakeAt, frameAt + frameMs);
            }

            int timeout = wakeAt == long.MaxValue ? Timeout.Infinite : (int)Math.Clamp(wakeAt - now, 0, int.MaxValue);
            bool running;
            if (Poll(out Event ev, timeout))
            {
                do
                {
                    running = app.Handle(ev, Elapsed(start));
                }
                while (running && Poll(out ev, 0));   // drain the burst, then draw once
            }
            else
            {
                running = app.Handle(Event.Tick, Elapsed(start));
            }

            if (!running)
            {
                if (_inline is not null)
                {
                    app.Render(BeginFrame(), Elapsed(start));
                    Present();
                }

                return;
            }
        }
    }

    private static long Elapsed(long start) => (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
