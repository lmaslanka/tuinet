using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>
/// The items grouped by kind, then by priority (highest first), for the Groups tab. Node ids: an item is its
/// <see cref="Item.Number"/> - 1 (stable while the list is re-sorted), a kind group is <see cref="KindGroup"/> + kind,
/// and a priority group inside it is <see cref="PriorityGroup"/> + kind·10 + priority. Empty groups are left out;
/// items keep the list's current order. Nothing is cached: groups are counted from the 20 items as they're drawn.
/// </summary>
public readonly struct ItemTree(Item[] items) : ITreeSource
{
    public const int KindGroup = 1000;
    public const int PriorityGroup = 2000;

    public static bool IsItem(int node) => (uint)node < KindGroup;

    public static int NodeOf(Item item) => item.Number - 1;

    public int ChildCount(int node)
    {
        int count = 0;
        if (node == TreeState.Root)
        {
            for (int kind = 0; kind < Item.Kinds.Length; kind++)
            {
                count += Count(kind, -1) > 0 ? 1 : 0;
            }
        }
        else if (node >= PriorityGroup)
        {
            count = Count((node - PriorityGroup) / 10, (node - PriorityGroup) % 10);
        }
        else if (node >= KindGroup)
        {
            for (int priority = 0; priority < Item.Priorities.Length; priority++)
            {
                count += Count(node - KindGroup, priority) > 0 ? 1 : 0;
            }
        }

        return count;
    }

    public int Child(int node, int index)
    {
        if (node == TreeState.Root)
        {
            for (int kind = 0; kind < Item.Kinds.Length; kind++)
            {
                if (Count(kind, -1) > 0 && index-- == 0)
                {
                    return KindGroup + kind;
                }
            }
        }
        else if (node >= PriorityGroup)
        {
            int kind = (node - PriorityGroup) / 10;
            int priority = (node - PriorityGroup) % 10;
            foreach (Item item in items)
            {
                if (item.Kind == kind && item.Priority == priority && index-- == 0)
                {
                    return NodeOf(item);
                }
            }
        }
        else if (node >= KindGroup)
        {
            int kind = node - KindGroup;
            for (int priority = Item.Priorities.Length - 1; priority >= 0; priority--)
            {
                if (Count(kind, priority) > 0 && index-- == 0)
                {
                    return PriorityGroup + kind * 10 + priority;
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public bool HasChildren(int node) => node >= KindGroup;

    public int Parent(int node)
    {
        if (node >= PriorityGroup)
        {
            return KindGroup + (node - PriorityGroup) / 10;
        }

        if (node >= KindGroup)
        {
            return TreeState.Root;
        }

        Item item = Find(node);
        return PriorityGroup + item.Kind * 10 + item.Priority;
    }

    public void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected)
    {
        int x = area.X;
        int right = area.Right;
        if (node >= KindGroup)
        {
            bool kindGroup = node < PriorityGroup;
            int kind = kindGroup ? node - KindGroup : (node - PriorityGroup) / 10;
            int priority = kindGroup ? -1 : (node - PriorityGroup) % 10;
            Style style = kindGroup
                ? Theme.Heading(Theme.KindColor(kind))
                : Theme.Accent(Theme.PriorityColor(priority));
            x = buffer.SetString(x, area.Y, kindGroup ? Item.Kinds[kind] : Item.Priorities[priority], style, right - x);
            Span<char> count = stackalloc char[8];
            count.TryWrite($" ({Count(kind, priority)})", out int length);
            buffer.SetString(x, area.Y, count[..length], Theme.Faded, right - x);
            return;
        }

        Item item = Find(node);
        Span<char> number = stackalloc char[4];
        number.TryWrite($"{item.Number:D2} ", out int digits);
        x = buffer.SetString(x, area.Y, number[..digits], Theme.Faded, right - x);
        int ownerX = right - item.Owner.Length;
        int nameEnd = buffer.SetString(x, area.Y, item.Name, item.Enabled ? Theme.Body : Theme.Dim, right - x, Overflow.Ellipsis);
        if (ownerX > nameEnd + 1)
        {
            buffer.SetString(ownerX, area.Y, item.Owner, Theme.Faded, right - ownerX);
        }
    }

    /// <summary>Items of <paramref name="kind"/> with <paramref name="priority"/> (-1: any priority).</summary>
    private int Count(int kind, int priority)
    {
        int count = 0;
        foreach (Item item in items)
        {
            count += item.Kind == kind && (priority < 0 || item.Priority == priority) ? 1 : 0;
        }

        return count;
    }

    private Item Find(int node)
    {
        foreach (Item item in items)
        {
            if (NodeOf(item) == node)
            {
                return item;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(node));
    }
}
