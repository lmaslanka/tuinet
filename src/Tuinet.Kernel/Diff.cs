using System.Buffers;
using System.Text;

namespace Tuinet;

public static class Diff
{
    public static void Write(CellBuffer current, CellBuffer previous, IBufferWriter<byte> output)
    {
        if (current.Width != previous.Width || current.Height != previous.Height)
        {
            previous = new CellBuffer(current.Width, current.Height);
        }

        bool dirty = false;
        Style lastStyle = default;
        bool haveStyle = false;

        for (int y = 0; y < current.Height; y++)
        {
            int start = -1;
            int end = -1;
            for (int x = 0; x < current.Width; x++)
            {
                if (current[x, y] != previous[x, y])
                {
                    if (start < 0)
                    {
                        start = x;
                    }
                    end = x;
                }
            }

            if (start < 0)
            {
                continue;
            }

            if (start > 0 && current[start, y].IsContinuation)
            {
                start--;
            }

            if (!dirty)
            {
                Utf8Write.Bytes(output, Vt.SyncStart);
                dirty = true;
            }

            MoveTo(output, start, y);

            for (int x = start; x <= end; x++)
            {
                Cell cell = current[x, y];
                if (cell.IsContinuation)
                {
                    continue;
                }

                if (!haveStyle || !cell.Style.Equals(lastStyle))
                {
                    EmitStyle(output, cell.Style);
                    lastStyle = cell.Style;
                    haveStyle = true;
                }

                EmitRune(output, cell.Rune);
            }
        }

        if (dirty)
        {
            Utf8Write.Bytes(output, Vt.SyncEnd);
        }
    }

    private static void MoveTo(IBufferWriter<byte> output, int x, int y)
    {
        Utf8Write.Bytes(output, "\u001b["u8);
        Utf8Write.Int(output, y + 1);
        Utf8Write.Bytes(output, ";"u8);
        Utf8Write.Int(output, x + 1);
        Utf8Write.Bytes(output, "H"u8);
    }

    private static void EmitStyle(IBufferWriter<byte> output, Style style)
    {
        Utf8Write.Bytes(output, Vt.ResetStyle);
        if (!style.Foreground.IsDefault)
        {
            EmitRgb(output, 38, style.Foreground);
        }

        if (!style.Background.IsDefault)
        {
            EmitRgb(output, 48, style.Background);
        }
    }

    private static void EmitRgb(IBufferWriter<byte> output, int kind, Color color)
    {
        Utf8Write.Bytes(output, "\u001b["u8);
        Utf8Write.Int(output, kind);
        Utf8Write.Bytes(output, ";2;"u8);
        Utf8Write.Int(output, color.R);
        Utf8Write.Bytes(output, ";"u8);
        Utf8Write.Int(output, color.G);
        Utf8Write.Bytes(output, ";"u8);
        Utf8Write.Int(output, color.B);
        Utf8Write.Bytes(output, "m"u8);
    }

    private static void EmitRune(IBufferWriter<byte> output, Rune rune)
    {
        Span<byte> utf8 = stackalloc byte[4];
        int n = rune.EncodeToUtf8(utf8);
        Utf8Write.Bytes(output, utf8[..n]);
    }
}
