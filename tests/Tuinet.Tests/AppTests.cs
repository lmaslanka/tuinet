using Tuinet.Testing;

namespace Tuinet.Tests;

/// <summary><see cref="Terminal.Run"/>: one frame per burst of input, sleep when idle, ticks while animating or when due.</summary>
public class AppTests
{
    [Fact]
    public void Run_draws_once_then_sleeps_until_input()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder();
        Thread loop = Start(terminal, app);

        Assert.True(tty.Blocked.Wait(2000));
        Assert.Equal(1, app.Frames);
        Assert.Equal(Timeout.Infinite, tty.ReadTimeouts[^1]);   // no timer: an idle app costs nothing
        Assert.Empty(app.Events);

        tty.Enqueue("q");
        Assert.True(loop.Join(2000), "Run did not return");
        Assert.Equal(1, app.Frames);   // no frame after the app stops
    }

    [Fact]
    public void Run_handles_a_whole_burst_then_draws_once()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder();
        Thread loop = Start(terminal, app);
        Assert.True(tty.Blocked.Wait(2000));

        tty.Blocked.Reset();
        tty.Enqueue("jjjjj");
        Assert.True(tty.Blocked.Wait(2000));
        Assert.Equal(5, app.Events.Count);
        Assert.Equal(2, app.Frames);

        tty.Enqueue("q");
        Assert.True(loop.Join(2000), "Run did not return");
    }

    [Fact]
    public void Run_stops_at_the_event_that_returns_false()
    {
        var tty = new TestTty();
        tty.Enqueue("aqb");
        using var terminal = new Terminal(tty);
        var app = new Recorder();

        terminal.Run(app);

        Assert.Equal(new[] { "Key a", "Key q" }, app.Events.Select(e => e.ToString()));
        Assert.Equal(1, app.Frames);
        Assert.True(terminal.Poll(out Event left, 0));   // the rest of the burst is still queued
        Assert.True(left.Key.IsChar('b'));
    }

    [Fact]
    public void Run_ticks_at_the_frame_rate_while_animating()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder { Animating = true, StopAfterTicks = 4 };

        terminal.Run(app, frameMs: 10);

        Assert.Equal(4, app.Events.Count);
        Assert.All(app.Events, e => Assert.Equal(EventKind.Tick, e.Kind));
        Assert.Equal(4, app.Frames);   // the first frame, then one after each tick but the last
        Assert.All(tty.ReadTimeouts, t => Assert.InRange(t, 0, 10));
        Assert.True(app.Times[^1] >= 30, $"4 ticks 10 ms apart ended at {app.Times[^1]} ms");
    }

    [Fact]
    public void Run_with_a_zero_frame_interval_never_waits_while_animating()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder { Animating = true, StopAfterTicks = 50 };

        terminal.Run(app, frameMs: 0);

        Assert.Equal(50, app.Frames);
        Assert.All(tty.ReadTimeouts, t => Assert.Equal(0, t));
    }

    [Fact]
    public void Run_wakes_an_idle_app_when_its_alarm_is_due()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder { StopAtAlarm = true };
        app.Alarm.At(40);

        terminal.Run(app);

        Assert.All(app.Events, e => Assert.Equal(EventKind.Tick, e.Kind));
        Assert.True(app.Times[^1] >= 40, $"woke at {app.Times[^1]} ms");
        Assert.InRange(tty.ReadTimeouts[0], 1, 40);   // not Infinite: the loop waits only until the alarm
    }

    [Fact]
    public void Input_before_the_alarm_is_handled_without_a_tick()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        var app = new Recorder();
        app.Alarm.At(60_000);
        Thread loop = Start(terminal, app);
        Assert.True(tty.Blocked.Wait(2000));
        Assert.InRange(tty.ReadTimeouts[^1], 50_000, 60_000);

        tty.Enqueue("q");
        Assert.True(loop.Join(2000), "Run did not return");
        Assert.Equal(new[] { "Key q" }, app.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void Run_delivers_resizes_and_draws_at_the_new_size()
    {
        var tty = new TestTty(40, 10);
        using var terminal = new Terminal(tty);
        var app = new Recorder();
        Thread loop = Start(terminal, app);
        Assert.True(tty.Blocked.Wait(2000));

        tty.Blocked.Reset();
        tty.Resize(60, 12);
        Assert.True(tty.Blocked.Wait(2000));
        Assert.Equal(EventKind.Resize, app.Events[^1].Kind);
        Assert.Equal(new Size(60, 12), app.LastFrameSize);

        tty.Enqueue("q");
        Assert.True(loop.Join(2000), "Run did not return");
    }

    [Fact]
    public void Inline_run_draws_the_last_frame_again_after_the_app_stops()
    {
        var tty = new TestTty(20, 10);
        tty.Enqueue("\u001b[3;1R");   // the cursor report inline mode starts with
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(2) });
        tty.Enqueue("aq");
        var app = new Recorder();

        terminal.Run(app);

        Assert.Equal(2, app.Frames);   // the first frame, and the one that stays on screen
        Assert.Equal(2, terminal.Frames);
    }

    [Fact]
    public void Run_rejects_a_negative_frame_interval()
    {
        using var terminal = new Terminal(new TestTty());
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Run(new Recorder(), frameMs: -1));
    }

    [Fact]
    public void Last_frame_time_covers_begin_frame_to_present()
    {
        using var terminal = new Terminal(new TestTty());
        Assert.Equal(TimeSpan.Zero, terminal.LastFrameTime);
        CellBuffer frame = terminal.BeginFrame();
        Thread.Sleep(5);
        frame.SetString(0, 0, "hello", default);
        terminal.Present();
        Assert.True(terminal.LastFrameTime >= TimeSpan.FromMilliseconds(4), terminal.LastFrameTime.ToString());
    }

    [Fact]
    public void Tick_is_its_own_kind_and_poll_never_returns_it()
    {
        Assert.Equal(EventKind.Tick, Event.Tick.Kind);
        Assert.Equal("Tick", Event.Tick.ToString());
        using var terminal = new Terminal(new TestTty());
        Assert.False(terminal.Poll(out _, 0));
    }

    [Fact]
    public void Default_alarm_is_not_set()
    {
        Alarm alarm = default;
        Assert.False(alarm.IsSet);
        Assert.Equal(long.MaxValue, alarm.DueMs);
        Assert.False(alarm.Fire(long.MaxValue));
    }

    [Fact]
    public void Alarm_fires_once_at_its_time()
    {
        Alarm alarm = default;
        alarm.Start(nowMs: 1000, delayMs: 500);
        Assert.Equal(1500, alarm.DueMs);
        Assert.False(alarm.Fire(1499));
        Assert.True(alarm.IsSet);
        Assert.True(alarm.Fire(1700));   // late checks still fire
        Assert.False(alarm.IsSet);
        Assert.False(alarm.Fire(1800));
        Assert.Equal(long.MaxValue, alarm.DueMs);
    }

    [Fact]
    public void Alarm_restarts_and_cancels()
    {
        Alarm alarm = default;
        alarm.At(100);
        alarm.Start(nowMs: 90, delayMs: 50);   // replaces 100
        Assert.False(alarm.Fire(120));
        Assert.True(alarm.Fire(140));

        alarm.At(10);
        alarm.Cancel();
        Assert.False(alarm.Fire(20));
        Assert.Equal(long.MaxValue, alarm.DueMs);
    }

    [Fact]
    public void Run_frames_allocate_nothing()
    {
        using var terminal = new Terminal(new AllocationTests.NullTty(120, 40));
        var app = new CountingApp();

        terminal.Run(app, frameMs: 0);

        Assert.Equal(0, app.Allocated);
    }

    private static Thread Start(Terminal terminal, IApp app)
    {
        var thread = new Thread(() => terminal.Run(app)) { IsBackground = true };
        thread.Start();
        return thread;
    }

    /// <summary>Records what the loop does. Quits on 'q', after <see cref="StopAfterTicks"/> ticks, or when <see cref="Alarm"/> fires.</summary>
    private sealed class Recorder : IApp
    {
        public Alarm Alarm;

        public List<Event> Events { get; } = [];
        public List<long> Times { get; } = [];
        public int Frames { get; private set; }
        public Size LastFrameSize { get; private set; }
        public bool Animating { get; init; }
        public int StopAfterTicks { get; init; } = int.MaxValue;
        public bool StopAtAlarm { get; init; }

        public bool Handle(Event ev, long nowMs)
        {
            Events.Add(ev);
            Times.Add(nowMs);
            if (Alarm.Fire(nowMs) && StopAtAlarm)
            {
                return false;
            }

            return !(ev.Kind == EventKind.Key && ev.Key.IsChar('q')) && Events.Count(e => e.Kind == EventKind.Tick) < StopAfterTicks;
        }

        public void Render(CellBuffer frame, long nowMs)
        {
            Frames++;
            LastFrameSize = frame.Size;
            frame.SetString(0, 0, "frame", default);
        }

        public bool IsAnimating(long nowMs) => Animating;

        public long NextDueMs(long nowMs) => Alarm.DueMs;
    }

    /// <summary>Animates for 2,000 frames and measures what frames 100 to 2,000 allocate, loop included.</summary>
    private sealed class CountingApp : IApp
    {
        private int _frames;
        private long _before;

        public long Allocated { get; private set; } = -1;

        public bool Handle(Event ev, long nowMs) => _frames < 2000;

        public void Render(CellBuffer frame, long nowMs)
        {
            _frames++;
            if (_frames == 100)
            {
                _before = GC.GetAllocatedBytesForCurrentThread();
            }
            else if (_frames == 2000)
            {
                Allocated = GC.GetAllocatedBytesForCurrentThread() - _before;
            }

            frame.SetString(_frames % 10, 0, "tick", default);
        }

        public bool IsAnimating(long nowMs) => true;
    }
}
