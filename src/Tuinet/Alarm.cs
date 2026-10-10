namespace Tuinet;

/// <summary>
/// A one-shot "at this time" check for time-based changes (hide a message after 3 s, debounce a search) without timers
/// or callbacks. Keep it in your state, check it with the loop's time, and return <see cref="DueMs"/> from
/// <see cref="IApp.NextDueMs"/> so <see cref="Terminal.Run"/> wakes for it. <c>default</c> is not set.
/// <code>
/// flashAlarm.Start(nowMs, 3000);                     // when the message is shown
/// if (flashAlarm.Fire(nowMs)) flash = "";            // in Handle: true once, when due
/// public long NextDueMs(long nowMs) => flashAlarm.DueMs;
/// </code>
/// </summary>
public struct Alarm
{
    private long _dueMs;
    private bool _set;

    /// <summary>Whether the alarm is waiting to go off.</summary>
    public readonly bool IsSet => _set;

    /// <summary>When it goes off, or <see cref="long.MaxValue"/> when not set (so <see cref="Math.Min(long, long)"/> combines alarms).</summary>
    public readonly long DueMs => _set ? _dueMs : long.MaxValue;

    /// <summary>Go off <paramref name="delayMs"/> after <paramref name="nowMs"/>, replacing any earlier time.</summary>
    public void Start(long nowMs, long delayMs) => At(nowMs + delayMs);

    /// <summary>Go off at <paramref name="dueMs"/>, replacing any earlier time.</summary>
    public void At(long dueMs)
    {
        _dueMs = dueMs;
        _set = true;
    }

    public void Cancel() => _set = false;

    /// <summary>True once, at the first check at or after <see cref="DueMs"/>; the alarm is then no longer set.</summary>
    public bool Fire(long nowMs)
    {
        if (!_set || nowMs < _dueMs)
        {
            return false;
        }

        _set = false;
        return true;
    }
}
