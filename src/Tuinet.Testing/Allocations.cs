namespace Tuinet.Testing;

/// <summary>
/// The zero-allocation guarantee, for your own app's frames: run a step (handle some input, render into one
/// <see cref="CellBuffer"/>) many times and fail if it keeps allocating. Call the app directly in the step, not through
/// <see cref="TestTerminal"/>, which copies every frame.
/// <code>
/// var buffer = new CellBuffer(80, 24);
/// Allocations.AssertNone(i =>
/// {
///     app.Handle(Event.FromKey(i % 2 == 0 ? KeyCode.Down : KeyCode.Up), 0);
///     app.Render(buffer, 0);
/// });
/// </code>
/// </summary>
public static class Allocations
{
    /// <summary>
    /// After a warm-up of 50 steps, some round of <paramref name="steps"/> steps (the step's argument counts up) must
    /// allocate nothing on this thread. Up to <paramref name="rounds"/> rounds, because a whole app frame runs enough
    /// code that one-time runtime work (tiering on a slow CI runner) can land in the first; a per-frame allocation shows
    /// up in every round, and the <see cref="ScreenAssertionException"/> lists them. Between rounds it pauses briefly:
    /// until the JIT recompiles them, generic formatting paths (an interpolated <c>{value:F1}</c> into a span) box
    /// their argument, and the recompile waits for a quiet moment that a busy loop may never give it.
    /// </summary>
    public static void AssertNone(Action<int> step, int steps = 500, int rounds = 5)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        for (int i = 0; i < 50; i++)
        {
            step(i);
        }

        var allocated = new long[rounds];
        for (int round = 0; round < rounds; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < steps; i++)
            {
                step(i);
            }

            allocated[round] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated[round] == 0)
            {
                return;
            }

            Thread.Sleep(250);
        }

        throw new ScreenAssertionException($"Every round allocated: {string.Join(", ", allocated)} bytes per {steps} steps.");
    }
}
