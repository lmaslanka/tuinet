using System.Buffers;
using System.Text;

namespace Tuinet;

public sealed class VtParser
{
    private enum State
    {
        Ground,
        Esc,
        Csi,
        Ss3,
        Utf8,
    }

    private State _state;
    private readonly byte[] _param = new byte[16];
    private int _paramLen;
    private readonly byte[] _utf8 = new byte[4];
    private int _utf8Len;
    private int _utf8Needed;
    private readonly Queue<KeyEvent> _keys = new();

    public bool IsIncomplete => _state is State.Esc or State.Csi or State.Ss3 or State.Utf8;

    public void Feed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
            Consume(data[i]);
    }

    public bool TryTake(out KeyEvent key) => _keys.TryDequeue(out key!);

    public void FlushIncomplete()
    {
        if (_state == State.Esc)
        {
            _keys.Enqueue(new KeyEvent(KeyCode.Escape));
        }
        Reset();
    }

    private void Consume(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                ConsumeGround(b);
                break;
            case State.Esc:
                ConsumeEsc(b);
                break;
            case State.Csi:
                ConsumeCsi(b);
                break;
            case State.Ss3:
                ConsumeSs3(b);
                break;
            case State.Utf8:
                ConsumeUtf8(b);
                break;
        }
    }

    private void ConsumeGround(byte b)
    {
        switch (b)
        {
            case 0x1B:
                _state = State.Esc;
                return;
            case 0x09:
                _keys.Enqueue(new KeyEvent(KeyCode.Tab));
                return;
            case 0x0D:
            case 0x0A:
                _keys.Enqueue(new KeyEvent(KeyCode.Enter));
                return;
            case 0x7F:
            case 0x08:
                _keys.Enqueue(new KeyEvent(KeyCode.Backspace));
                return;
        }

        if (b < 0x20)
        {
            _keys.Enqueue(new KeyEvent(KeyCode.Char, new Rune((char)('a' + b - 1)), Modifiers.Ctrl));
            return;
        }

        if (b < 0x80)
        {
            _keys.Enqueue(new KeyEvent(KeyCode.Char, new Rune(b)));
            return;
        }

        _utf8Len = 0;
        _utf8[_utf8Len++] = b;
        _utf8Needed = b switch
        {
            < 0xE0 => 2,
            < 0xF0 => 3,
            _ => 4,
        };
        _state = State.Utf8;
    }

    private void ConsumeEsc(byte b)
    {
        switch (b)
        {
            case (byte)'[':
                _paramLen = 0;
                _state = State.Csi;
                return;
            case (byte)'O':
                _state = State.Ss3;
                return;
            default:
                _keys.Enqueue(new KeyEvent(KeyCode.Escape));
                Reset();
                ConsumeGround(b);
                return;
        }
    }

    private void ConsumeCsi(byte b)
    {
        if (b is >= 0x30 and <= 0x3F)
        {
            if (_paramLen < _param.Length)
            {
                _param[_paramLen++] = b;
            }
            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            return;
        }

        KeyEvent? key = b switch
        {
            (byte)'A' => new KeyEvent(KeyCode.Up),
            (byte)'B' => new KeyEvent(KeyCode.Down),
            (byte)'C' => new KeyEvent(KeyCode.Right),
            (byte)'D' => new KeyEvent(KeyCode.Left),
            (byte)'H' => new KeyEvent(KeyCode.Home),
            (byte)'F' => new KeyEvent(KeyCode.End),
            (byte)'Z' => new KeyEvent(KeyCode.Tab, default, Modifiers.Shift),
            (byte)'~' => ParseTilde(),
            _ => null,
        };

        if (key is { } k)
        {
            _keys.Enqueue(k);
        }
        Reset();
    }

    private void ConsumeSs3(byte b)
    {
        KeyEvent? key = b switch
        {
            (byte)'A' => new KeyEvent(KeyCode.Up),
            (byte)'B' => new KeyEvent(KeyCode.Down),
            (byte)'C' => new KeyEvent(KeyCode.Right),
            (byte)'D' => new KeyEvent(KeyCode.Left),
            (byte)'H' => new KeyEvent(KeyCode.Home),
            (byte)'F' => new KeyEvent(KeyCode.End),
            _ => null,
        };
        if (key is { } k)
        {
            _keys.Enqueue(k);
        }
        Reset();
    }

    private void ConsumeUtf8(byte b)
    {
        _utf8[_utf8Len++] = b;
        if (_utf8Len < _utf8Needed)
        {
            return;
        }

        if (Rune.DecodeFromUtf8(_utf8.AsSpan(0, _utf8Len), out Rune rune, out _) == OperationStatus.Done)
        {
            _keys.Enqueue(new KeyEvent(KeyCode.Char, rune));
        }
        Reset();
    }

    private KeyEvent? ParseTilde()
    {
        int n = 0;
        for (int i = 0; i < _paramLen; i++)
        {
            byte b = _param[i];
            if (b is >= (byte)'0' and <= (byte)'9')
            {
                n = n * 10 + (b - '0');
            }
            else
            {
                break;
            }
        }

        return n switch
        {
            1 or 7 => new KeyEvent(KeyCode.Home),
            3 => new KeyEvent(KeyCode.Delete),
            4 or 8 => new KeyEvent(KeyCode.End),
            _ => null,
        };
    }

    private void Reset()
    {
        _state = State.Ground;
        _paramLen = 0;
        _utf8Len = 0;
        _utf8Needed = 0;
    }
}
