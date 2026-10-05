using System.Text;

namespace Tuinet;

/// <summary>
/// Incremental VT input decoder: bytes in, <see cref="Event"/>s out. Handles UTF-8, Ctrl/Alt
/// combinations, CSI/SS3 keys with xterm modifier parameters, kitty <c>CSI u</c> keys,
/// SGR (1006) mouse, focus reports and bracketed paste. Allocation-free except for paste text.
/// </summary>
internal sealed class VtParser
{
    private enum State : byte
    {
        Ground,
        Esc,
        Csi,
        Ss3,
        Utf8,
        Paste,
    }

    private const int MaxParams = 4;
    private static ReadOnlySpan<byte> PasteEnd => "\u001b[201~"u8;

    private readonly Queue<Event> _events = new(64);
    private readonly byte[] _csi = new byte[32];
    private readonly byte[] _utf8 = new byte[4];
    private byte[] _paste = [];
    private State _state;
    private int _csiLen;
    private bool _csiOverflow;
    private int _utf8Len;
    private int _utf8Needed;
    private int _pasteLen;
    private bool _alt;
    private int _reportRow = -1;
    private int _reportColumn;

    /// <summary>True while a partial escape or UTF-8 sequence is buffered (a paste in progress is not "incomplete").</summary>
    public bool IsIncomplete => _state is State.Esc or State.Csi or State.Ss3 or State.Utf8;

    public int Pending => _events.Count;

    /// <summary>
    /// Set after sending a cursor position query (<c>CSI 6n</c>): the next <c>CSI row;col R</c> is the reply,
    /// not F3 with modifiers (xterm sends those as <c>CSI 1;mod R</c>). Cleared by the reply.
    /// </summary>
    public bool ExpectCursorReport { get; set; }

    /// <summary>The reply to a cursor position query, 0-based, once it has arrived.</summary>
    public bool TryTakeCursorReport(out int row, out int column)
    {
        row = _reportRow;
        column = _reportColumn;
        _reportRow = -1;
        return row >= 0;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            switch (_state)
            {
                case State.Ground:
                    Ground(b);
                    break;
                case State.Esc:
                    Esc(b);
                    break;
                case State.Csi:
                    Csi(b);
                    break;
                case State.Ss3:
                    Ss3(b);
                    break;
                case State.Utf8:
                    Utf8(b);
                    break;
                case State.Paste:
                    i += PasteChunk(data[i..]) - 1;
                    break;
            }
        }
    }

    public bool TryTake(out Event ev) => _events.TryDequeue(out ev);

    /// <summary>The input went quiet mid-sequence: resolve what is buffered (a lone ESC is the Escape key).</summary>
    public void FlushIncomplete()
    {
        switch (_state)
        {
            case State.Esc:
                Key(KeyCode.Escape);
                break;
            case State.Csi when _csiLen == 0:
                _alt = true;
                Key(KeyCode.Char, '[');
                break;
            case State.Ss3:
                _alt = true;
                Key(KeyCode.Char, 'O');
                break;
        }

        _alt = false;
        _state = State.Ground;
    }

    private void Ground(byte b)
    {
        switch (b)
        {
            case 0x1B:
                _state = State.Esc;
                return;
            case 0x0D:
            case 0x0A:
                Key(KeyCode.Enter);
                return;
            case 0x09:
                Key(KeyCode.Tab);
                return;
            case 0x7F:
            case 0x08:
                Key(KeyCode.Backspace);
                return;
            case 0x00:
                Key(KeyCode.Char, ' ', Modifiers.Ctrl);
                return;
        }

        if (b < 0x20)
        {
            int c = b <= 26 ? 'a' + b - 1 : b + 0x40;
            Key(KeyCode.Char, c, Modifiers.Ctrl);
            return;
        }

        if (b < 0x80)
        {
            Key(KeyCode.Char, b);
            return;
        }

        _utf8Needed = b switch
        {
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 0,
        };

        if (_utf8Needed == 0)
        {
            _alt = false;
            return;
        }

        _utf8[0] = b;
        _utf8Len = 1;
        _state = State.Utf8;
    }

    private void Esc(byte b)
    {
        switch (b)
        {
            case (byte)'[':
                _csiLen = 0;
                _csiOverflow = false;
                _state = State.Csi;
                return;
            case (byte)'O':
                _state = State.Ss3;
                return;
            case 0x1B:
                Key(KeyCode.Escape);
                return;
            default:
                _alt = true;
                _state = State.Ground;
                Ground(b);
                return;
        }
    }

    private void Csi(byte b)
    {
        if (b is >= 0x30 and <= 0x3F)
        {
            if (_csiLen < _csi.Length)
            {
                _csi[_csiLen++] = b;
            }
            else
            {
                _csiOverflow = true;
            }

            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            return;
        }

        _state = State.Ground;
        if (b is >= 0x40 and <= 0x7E)
        {
            if (!_csiOverflow)
            {
                DispatchCsi(b);
            }

            _alt = false;
            return;
        }

        // A control byte aborts the sequence and is processed on its own.
        _alt = false;
        Ground(b);
    }

    private void DispatchCsi(byte final)
    {
        ReadOnlySpan<byte> raw = _csi.AsSpan(0, _csiLen);
        byte prefix = raw.Length > 0 && raw[0] is (byte)'<' or (byte)'?' or (byte)'>' or (byte)'=' ? raw[0] : (byte)0;
        if (prefix != 0)
        {
            raw = raw[1..];
        }

        Span<int> p = stackalloc int[MaxParams];
        int count = ParseParams(raw, p);

        if (prefix == '<')
        {
            if ((final == 'M' || final == 'm') && count >= 3)
            {
                Mouse(p[0], p[1] - 1, p[2] - 1, release: final == 'm');
            }

            return;
        }

        if (prefix != 0)
        {
            return;
        }

        Modifiers mods = count >= 2 ? DecodeModifiers(p[1]) : Modifiers.None;
        switch (final)
        {
            case (byte)'A': Key(KeyCode.Up, 0, mods); return;
            case (byte)'B': Key(KeyCode.Down, 0, mods); return;
            case (byte)'C': Key(KeyCode.Right, 0, mods); return;
            case (byte)'D': Key(KeyCode.Left, 0, mods); return;
            case (byte)'H': Key(KeyCode.Home, 0, mods); return;
            case (byte)'F': Key(KeyCode.End, 0, mods); return;
            case (byte)'P': Key(KeyCode.F1, 0, mods); return;
            case (byte)'Q': Key(KeyCode.F2, 0, mods); return;
            case (byte)'R' when ExpectCursorReport && count == 2:
                ExpectCursorReport = false;
                _reportRow = Math.Max(0, p[0] - 1);
                _reportColumn = Math.Max(0, p[1] - 1);
                return;
            case (byte)'R': Key(KeyCode.F3, 0, mods); return;
            case (byte)'S': Key(KeyCode.F4, 0, mods); return;
            case (byte)'Z': Key(KeyCode.Tab, 0, mods | Modifiers.Shift); return;
            case (byte)'I' when count == 0: _events.Enqueue(Event.Focus(gained: true)); return;
            case (byte)'O' when count == 0: _events.Enqueue(Event.Focus(gained: false)); return;
            case (byte)'u' when count >= 1: KittyKey(p[0], mods); return;
            case (byte)'~' when count >= 1: Tilde(p[0], mods); return;
        }
    }

    private void Tilde(int code, Modifiers mods)
    {
        KeyCode key;
        switch (code)
        {
            case 1 or 7: key = KeyCode.Home; break;
            case 2: key = KeyCode.Insert; break;
            case 3: key = KeyCode.Delete; break;
            case 4 or 8: key = KeyCode.End; break;
            case 5: key = KeyCode.PageUp; break;
            case 6: key = KeyCode.PageDown; break;
            case >= 11 and <= 15: key = (KeyCode)((int)KeyCode.F1 + code - 11); break;
            case >= 17 and <= 21: key = (KeyCode)((int)KeyCode.F6 + code - 17); break;
            case 23: key = KeyCode.F11; break;
            case 24: key = KeyCode.F12; break;
            case 200:
                _pasteLen = 0;
                _state = State.Paste;
                return;
            default:
                return;
        }

        Key(key, 0, mods);
    }

    private void KittyKey(int codePoint, Modifiers mods)
    {
        switch (codePoint)
        {
            case 13: Key(KeyCode.Enter, 0, mods); return;
            case 27: Key(KeyCode.Escape, 0, mods); return;
            case 9: Key(KeyCode.Tab, 0, mods); return;
            case 127: Key(KeyCode.Backspace, 0, mods); return;
        }

        if (Rune.IsValid(codePoint) && codePoint >= 0x20)
        {
            Key(KeyCode.Char, codePoint, mods);
        }
    }

    private void Mouse(int cb, int x, int y, bool release)
    {
        Modifiers mods = Modifiers.None;
        if ((cb & 4) != 0) mods |= Modifiers.Shift;
        if ((cb & 8) != 0) mods |= Modifiers.Alt;
        if ((cb & 16) != 0) mods |= Modifiers.Ctrl;

        int low = cb & 3;
        MouseButton button = low switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.None,
        };

        MouseKind kind;
        if ((cb & 64) != 0)
        {
            kind = (MouseKind)((int)MouseKind.ScrollUp + low);
            button = MouseButton.None;
        }
        else if ((cb & 32) != 0)
        {
            kind = button == MouseButton.None ? MouseKind.Move : MouseKind.Drag;
        }
        else
        {
            kind = release ? MouseKind.Up : MouseKind.Down;
        }

        _events.Enqueue(Event.FromMouse(new MouseEvent(kind, button, Math.Max(0, x), Math.Max(0, y), mods)));
    }

    private void Ss3(byte b)
    {
        _state = State.Ground;
        KeyCode key;
        switch (b)
        {
            case (byte)'A': key = KeyCode.Up; break;
            case (byte)'B': key = KeyCode.Down; break;
            case (byte)'C': key = KeyCode.Right; break;
            case (byte)'D': key = KeyCode.Left; break;
            case (byte)'H': key = KeyCode.Home; break;
            case (byte)'F': key = KeyCode.End; break;
            case (byte)'P': key = KeyCode.F1; break;
            case (byte)'Q': key = KeyCode.F2; break;
            case (byte)'R': key = KeyCode.F3; break;
            case (byte)'S': key = KeyCode.F4; break;
            default:
                _alt = false;
                return;
        }

        Key(key);
    }

    private void Utf8(byte b)
    {
        if ((b & 0xC0) != 0x80)
        {
            _state = State.Ground;
            _alt = false;
            Ground(b);
            return;
        }

        _utf8[_utf8Len++] = b;
        if (_utf8Len < _utf8Needed)
        {
            return;
        }

        _state = State.Ground;
        if (Rune.DecodeFromUtf8(_utf8.AsSpan(0, _utf8Len), out Rune rune, out _) == System.Buffers.OperationStatus.Done)
        {
            Key(KeyCode.Char, rune.Value);
        }
        else
        {
            _alt = false;
        }
    }

    /// <summary>Consume paste bytes up to and including the end marker. Returns bytes consumed (at least 1).</summary>
    private int PasteChunk(ReadOnlySpan<byte> data)
    {
        int n = 0;
        while (n < data.Length)
        {
            if (_pasteLen == _paste.Length)
            {
                Array.Resize(ref _paste, Math.Max(256, _paste.Length * 2));
            }

            _paste[_pasteLen++] = data[n++];
            if (_pasteLen >= PasteEnd.Length && _paste.AsSpan(_pasteLen - PasteEnd.Length, PasteEnd.Length).SequenceEqual(PasteEnd))
            {
                string text = Encoding.UTF8.GetString(_paste, 0, _pasteLen - PasteEnd.Length);
                _events.Enqueue(Event.FromPaste(text.ReplaceLineEndings("\n")));
                _pasteLen = 0;
                _state = State.Ground;
                break;
            }
        }

        return n;
    }

    private void Key(KeyCode code, int rune = 0, Modifiers mods = Modifiers.None)
    {
        if (_alt)
        {
            mods |= Modifiers.Alt;
            _alt = false;
        }

        _events.Enqueue(Event.FromKey(new KeyEvent(code, rune == 0 ? default : new Rune(rune), mods)));
    }

    private static Modifiers DecodeModifiers(int param)
    {
        int m = param - 1;
        if (m <= 0)
        {
            return Modifiers.None;
        }

        Modifiers mods = Modifiers.None;
        if ((m & 1) != 0) mods |= Modifiers.Shift;
        if ((m & (2 | 8)) != 0) mods |= Modifiers.Alt;
        if ((m & 4) != 0) mods |= Modifiers.Ctrl;
        return mods;
    }

    /// <summary>Parse ';'-separated decimal parameters (':' sub-parameters are skipped). Missing values are 1.</summary>
    private static int ParseParams(ReadOnlySpan<byte> raw, Span<int> dest)
    {
        if (raw.IsEmpty)
        {
            return 0;
        }

        int count = 0;
        int value = 0;
        bool any = false;
        bool sub = false;
        foreach (byte b in raw)
        {
            if (b == ';')
            {
                if (count < dest.Length)
                {
                    dest[count++] = any ? value : 1;
                }

                value = 0;
                any = false;
                sub = false;
            }
            else if (b == ':')
            {
                sub = true;
            }
            else if (!sub && b is >= (byte)'0' and <= (byte)'9')
            {
                value = Math.Min(value * 10 + (b - '0'), 1_000_000);
                any = true;
            }
        }

        if (count < dest.Length)
        {
            dest[count++] = any ? value : 1;
        }

        return count;
    }
}
