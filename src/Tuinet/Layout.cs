namespace Tuinet;

public enum Direction : byte
{
    Vertical,
    Horizontal,
}

public enum ConstraintKind : byte
{
    Length,
    Percent,
    Min,
    Max,
    Fill,
}

/// <summary>How much of the split axis one segment gets. See <see cref="Layout.Split"/>.</summary>
public readonly record struct Constraint(ConstraintKind Kind, int Value)
{
    /// <summary>Exactly <paramref name="cells"/>.</summary>
    public static Constraint Length(int cells) => new(ConstraintKind.Length, Math.Max(0, cells));

    /// <summary><paramref name="percent"/>% of the available space.</summary>
    public static Constraint Percent(int percent) => new(ConstraintKind.Percent, Math.Clamp(percent, 0, 100));

    /// <summary>At least <paramref name="cells"/>; grows when no <see cref="Fill"/> segment takes the space.</summary>
    public static Constraint Min(int cells) => new(ConstraintKind.Min, Math.Max(0, cells));

    /// <summary>Up to <paramref name="cells"/>; grows into leftover space when no <see cref="Fill"/> segment takes it.</summary>
    public static Constraint Max(int cells) => new(ConstraintKind.Max, Math.Max(0, cells));

    /// <summary>A share of the leftover space proportional to <paramref name="weight"/>.</summary>
    public static Constraint Fill(int weight = 1) => new(ConstraintKind.Fill, Math.Max(1, weight));
}

/// <summary>
/// Splits a rect along one axis. Allocation-free: constraints come in as a span (a collection
/// expression stays on the stack) and results go into a caller-provided span.
/// <code>
/// Span&lt;Rect&gt; rows = stackalloc Rect[3];
/// Layout.Vertical(area, [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows);
/// </code>
/// </summary>
public static class Layout
{
    public static void Vertical(Rect area, ReadOnlySpan<Constraint> constraints, Span<Rect> result, int spacing = 0) =>
        Split(area, Direction.Vertical, constraints, result, spacing);

    public static void Horizontal(Rect area, ReadOnlySpan<Constraint> constraints, Span<Rect> result, int spacing = 0) =>
        Split(area, Direction.Horizontal, constraints, result, spacing);

    /// <summary>
    /// Fixed sizes (Length, Percent, Min's minimum) are assigned first, shrinking from the end on
    /// overflow; the remainder goes to Fill segments by weight, or, if there are none, to Min/Max
    /// segments equally (Max capped). Unclaimed space is left after the last segment.
    /// </summary>
    public static void Split(Rect area, Direction direction, ReadOnlySpan<Constraint> constraints, Span<Rect> result, int spacing = 0)
    {
        int n = constraints.Length;
        if (result.Length < n)
        {
            throw new ArgumentException("Result span is shorter than the constraint list.", nameof(result));
        }

        if (n == 0)
        {
            return;
        }

        spacing = Math.Max(0, spacing);
        int total = direction == Direction.Vertical ? area.Height : area.Width;
        int available = Math.Max(0, total - spacing * (n - 1));
        Span<int> size = n <= 64 ? stackalloc int[n] : new int[n];

        int used = 0;
        bool anyFill = false;
        for (int i = 0; i < n; i++)
        {
            Constraint c = constraints[i];
            size[i] = c.Kind switch
            {
                ConstraintKind.Length => c.Value,
                ConstraintKind.Percent => available * c.Value / 100,
                ConstraintKind.Min => c.Value,
                _ => 0,
            };
            used += size[i];
            anyFill |= c.Kind == ConstraintKind.Fill;
        }

        for (int i = n - 1; i >= 0 && used > available; i--)
        {
            int take = Math.Min(size[i], used - available);
            size[i] -= take;
            used -= take;
        }

        Distribute(constraints, size, available - used, anyFill);

        int offset = direction == Direction.Vertical ? area.Y : area.X;
        int limit = offset + total;
        for (int i = 0; i < n; i++)
        {
            int length = Math.Max(0, Math.Min(size[i], limit - offset));
            result[i] = direction == Direction.Vertical
                ? new Rect(area.X, offset, area.Width, length)
                : new Rect(offset, area.Y, length, area.Height);
            offset += length + spacing;
        }
    }

    private static void Distribute(ReadOnlySpan<Constraint> constraints, Span<int> size, int remaining, bool fillOnly)
    {
        for (int round = 0; round <= constraints.Length && remaining > 0; round++)
        {
            int weights = 0;
            for (int i = 0; i < constraints.Length; i++)
            {
                weights += Weight(constraints[i], size[i], fillOnly);
            }

            if (weights == 0)
            {
                return;
            }

            int given = 0;
            for (int i = 0; i < constraints.Length; i++)
            {
                int weight = Weight(constraints[i], size[i], fillOnly);
                if (weight == 0)
                {
                    continue;
                }

                int share = remaining * weight / weights;
                if (constraints[i].Kind == ConstraintKind.Max)
                {
                    share = Math.Min(share, constraints[i].Value - size[i]);
                }

                size[i] += share;
                given += share;
            }

            remaining -= given;
            if (given == 0)
            {
                // Rounding left a few cells: hand them out one at a time in order.
                for (int i = 0; i < constraints.Length && remaining > 0; i++)
                {
                    if (Weight(constraints[i], size[i], fillOnly) > 0)
                    {
                        size[i]++;
                        remaining--;
                    }
                }
            }
        }
    }

    private static int Weight(Constraint c, int current, bool fillOnly) => c.Kind switch
    {
        ConstraintKind.Fill => c.Value,
        ConstraintKind.Min when !fillOnly => 1,
        ConstraintKind.Max when !fillOnly && current < c.Value => 1,
        _ => 0,
    };
}
