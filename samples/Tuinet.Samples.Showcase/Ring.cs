namespace Tuinet.Samples.Showcase;

/// <summary>
/// The last values added, up to a fixed capacity. <see cref="Older"/> then <see cref="Newer"/> are the values oldest
/// first, so <c>new Sparkline(ring.Older, ring.Newer)</c> draws them without a copy.
/// </summary>
internal sealed class Ring(int capacity)
{
    private readonly double[] _values = new double[capacity];
    private int _next;
    private int _count;

    public int Count => _count;

    /// <summary>The newest value; NaN while empty.</summary>
    public double Last => _count == 0 ? double.NaN : _values[(_next + _values.Length - 1) % _values.Length];

    public ReadOnlySpan<double> Older => _count < _values.Length ? default : _values.AsSpan(_next);
    public ReadOnlySpan<double> Newer => _values.AsSpan(0, _count < _values.Length ? _count : _next);

    public void Add(double value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        _count = Math.Min(_count + 1, _values.Length);
    }
}
