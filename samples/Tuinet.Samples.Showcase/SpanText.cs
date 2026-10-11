using System.Globalization;

namespace Tuinet.Samples.Showcase;

/// <summary>
/// A short label written into a caller's buffer: <c>new SpanText(stackalloc char[24]).Append("ITEMS · ").Append(n).Text</c>.
/// Not <c>span.TryWrite($"…")</c>: its handler's unconstrained <c>AppendFormatted&lt;T&gt;</c> boxes every number and
/// char until the JIT has fully optimized the caller, and a slow runner can still be running that code when an
/// allocation test measures. A constrained <see cref="ISpanFormattable"/> call never boxes. What doesn't fit is cut.
/// </summary>
internal ref struct SpanText(Span<char> buffer)
{
    private readonly Span<char> _buffer = buffer;
    private int _length;

    public readonly ReadOnlySpan<char> Text => _buffer[.._length];

    public SpanText Append(scoped ReadOnlySpan<char> text)
    {
        text = text[..Math.Min(text.Length, _buffer.Length - _length)];
        text.CopyTo(_buffer[_length..]);
        _length += text.Length;
        return this;
    }

    public SpanText Append(char c)
    {
        if (_length < _buffer.Length)
        {
            _buffer[_length++] = c;
        }

        return this;
    }

    /// <summary>Format <paramref name="value"/>, right-aligned in <paramref name="width"/> columns like <c>{value,width}</c>.</summary>
    public SpanText Append<T>(T value, ReadOnlySpan<char> format = default, int width = 0)
        where T : ISpanFormattable
    {
        Span<char> rest = _buffer[_length..];
        if (!value.TryFormat(rest, out int written, format, CultureInfo.InvariantCulture))
        {
            return this;
        }

        int pad = Math.Min(Math.Max(0, width - written), rest.Length - written);
        if (pad > 0)
        {
            rest[..written].CopyTo(rest[pad..]);
            rest[..pad].Fill(' ');
        }

        _length += pad + written;
        return this;
    }
}
