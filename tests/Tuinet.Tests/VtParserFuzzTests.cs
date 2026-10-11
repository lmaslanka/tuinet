using System.Runtime.InteropServices;
using System.Text;

namespace Tuinet.Tests;

/// <summary>
/// Property tests for the input parser, in the style of <see cref="RendererFuzzTests"/>. Whatever the terminal
/// sends (random bytes; valid sequences cut and glued at arbitrary points; arbitrary read boundaries) the parser
/// never throws, never delivers a control character as a key, never reports a cursor or mouse position outside
/// the clamps, keeps its memory bounded, and gets back to Ground within a bounded number of bytes.
/// Deterministic: fixed seeds.
/// </summary>
public class VtParserFuzzTests
{
    private const int Rounds = 300;

    /// <summary>Valid input, one sequence each: keys, modifiers, kitty, mouse, UTF-8, paste, focus and terminal replies.</summary>
    private static readonly byte[][] Sequences =
    [
        "j"u8.ToArray(), "\r"u8.ToArray(), "\t"u8.ToArray(), [0x7F], [0x03], [0x00], [0x1C],
        "\u001b[A"u8.ToArray(), "\u001bOA"u8.ToArray(), "\u001b[1;5A"u8.ToArray(), "\u001b[1;4C"u8.ToArray(),
        "\u001b[3~"u8.ToArray(), "\u001b[3;5~"u8.ToArray(), "\u001b[15~"u8.ToArray(), "\u001b[24~"u8.ToArray(),
        "\u001b[Z"u8.ToArray(), "\u001bx"u8.ToArray(), "\u001b\u007f"u8.ToArray(), "\u001b"u8.ToArray(),
        "\u001b[97;5u"u8.ToArray(), "\u001b[97;1:3u"u8.ToArray(), "\u001b[57399u"u8.ToArray(), "\u001b[133u"u8.ToArray(),
        "\u001b[<0;10;5M"u8.ToArray(), "\u001b[<0;10;5m"u8.ToArray(), "\u001b[<64;1;1M"u8.ToArray(), "\u001b[<35;3;4M"u8.ToArray(),
        "é"u8.ToArray(), "你"u8.ToArray(), "👍"u8.ToArray(), "\u001bé"u8.ToArray(), [0xC2, 0x85], [0xE4, (byte)'a'], [0xFF],
        "\u001b[I"u8.ToArray(), "\u001b[O"u8.ToArray(),
        "\u001b[200~hello\r\nworld\u001b[201~"u8.ToArray(), "\u001b[200~\u001b[A\u0003\u001b[201~"u8.ToArray(), "\u001b[201~"u8.ToArray(),
        "\u001bP>|kitty(0.36.4)\u001b\\"u8.ToArray(), "\u001bP>|foot(1.18.1)\u001b\\"u8.ToArray(), "\u001bPx"u8.ToArray(),
        "\u001b[?69;2$y"u8.ToArray(), "\u001b[?1u"u8.ToArray(), "\u001b[12;40R"u8.ToArray(), "\u001b[1;2R"u8.ToArray(),
        "\u001b[?1;2c"u8.ToArray(), "\u001b[99999999999999999999A"u8.ToArray(), "\u001b[1;2;3;4;5;6;7;8;9~"u8.ToArray(),
    ];

    [Fact]
    public void Random_bytes_never_throw_and_the_parser_recovers()
    {
        var random = new Random(0x5EED);
        var parser = new VtParser { BracketedPaste = true };
        byte[] buffer = new byte[4096];
        long total = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < Rounds; round++)
        {
            int length = random.Next(1, buffer.Length + 1);
            Span<byte> data = buffer.AsSpan(0, length);
            random.NextBytes(data);
            total += length;
            Round(parser, random, data);
        }

        // Bounded memory: the paste buffer, the event queue and the paste strings are all proportional to the input.
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 32 * total + (1 << 20), $"{allocated} bytes allocated for {total} bytes of input");
    }

    [Fact]
    public void Cut_and_glued_valid_sequences_never_throw_and_the_parser_recovers()
    {
        var random = new Random(0x5EED + 1);
        var parser = new VtParser { BracketedPaste = true };
        var stream = new List<byte>();
        for (int round = 0; round < Rounds; round++)
        {
            stream.Clear();
            int count = random.Next(1, 60);
            for (int i = 0; i < count; i++)
            {
                byte[] sequence = Sequences[random.Next(Sequences.Length)];
                int cut = random.Next(3) == 0 ? random.Next(1, sequence.Length + 1) : sequence.Length;
                stream.AddRange(sequence.AsSpan(0, cut));
            }

            Round(parser, random, CollectionsMarshal.AsSpan(stream));
        }
    }

    [Fact]
    public void Parser_without_bracketed_paste_never_enters_paste_mode()
    {
        var random = new Random(0x5EED + 2);
        var parser = new VtParser();
        byte[] buffer = new byte[2048];
        for (int round = 0; round < Rounds; round++)
        {
            random.NextBytes(buffer);
            parser.Feed(buffer);
            Assert.False(parser.InPaste);
            Drain(parser, buffer.Length);
            parser.FlushIncomplete();
        }
    }

    [Fact]
    public void Ground_is_reached_within_a_bounded_number_of_bytes_without_a_flush()
    {
        // From any state: a paste-end marker ends a paste; a second one is an ignored CSI from every other state
        // (an ESC aborts a CSI, a UTF-8 sequence or a DCS, and an SS3 or a lone Alt swallows at most one byte),
        // so the letter after them is always a plain key, and the last event.
        var random = new Random(0x5EED + 3);
        var parser = new VtParser { BracketedPaste = true };
        byte[] buffer = new byte[1024];
        for (int round = 0; round < Rounds; round++)
        {
            random.NextBytes(buffer);
            parser.Feed(buffer);
            parser.Feed("\u001b[201~\u001b[201~z"u8);
            Assert.False(parser.IsIncomplete);
            Assert.False(parser.InPaste);
            Event last = default;
            int taken = 0;
            while (parser.TryTake(out Event ev))
            {
                Check(ev);
                last = ev;
                taken++;
            }

            Assert.True(taken > 0);
            Assert.Equal(KeyEvent.Char('z'), last.Key);
        }
    }

    /// <summary>Feed <paramref name="data"/> in random read-sized chunks, check every event, then recover.</summary>
    private static void Round(VtParser parser, Random random, ReadOnlySpan<byte> data)
    {
        parser.ExpectCursorReport = random.Next(2) == 0;
        int length = data.Length;
        while (!data.IsEmpty)
        {
            int n = Math.Min(data.Length, random.Next(1, 64));
            parser.Feed(data[..n]);
            data = data[n..];
        }

        Drain(parser, length);

        // Quiet input resolves whatever is buffered; afterwards a letter is a plain key.
        parser.FlushIncomplete();
        Assert.False(parser.IsIncomplete);
        Assert.False(parser.InPaste);
        Drain(parser, 128);
        parser.ExpectCursorReport = false;
        parser.Feed("z"u8);
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(KeyEvent.Char('z'), ev.Key);
        Assert.False(parser.TryTake(out _));
    }

    /// <summary>Take and check every pending event; there can't be more than one per input byte plus one.</summary>
    private static void Drain(VtParser parser, int bytesFed)
    {
        int taken = 0;
        while (parser.TryTake(out Event ev))
        {
            Check(ev);
            taken++;
        }

        if (taken > bytesFed + 2)
        {
            Assert.Fail($"{taken} events from {bytesFed} bytes");
        }

        if (parser.TryTakeCursorReport(out int row, out int column))
        {
            Assert.InRange(row, 0, Terminal.MaxHeight - 1);
            Assert.InRange(column, 0, Terminal.MaxWidth - 1);
        }
    }

    private static void Check(Event ev)
    {
        switch (ev.Kind)
        {
            case EventKind.Key when ev.Key.Code == KeyCode.Char:
                // Conditional messages: an eager $"" per event would dominate the allocation bound.
                if (!Rune.IsValid(ev.Key.Rune.Value) || Rune.IsControl(ev.Key.Rune) || ev.Key.Rune.Value < 0x20)
                {
                    Assert.Fail($"control U+{ev.Key.Rune.Value:X4} delivered as a key");
                }

                break;
            case EventKind.Mouse:
                Assert.True(ev.Mouse.X >= 0 && ev.Mouse.Y >= 0);
                break;
            case EventKind.Paste:
                Assert.NotNull(ev.Paste);
                Assert.True(ev.Paste.Length <= Terminal.MaxPasteBytes);
                Assert.DoesNotContain("\r", ev.Paste);
                break;
        }
    }
}
