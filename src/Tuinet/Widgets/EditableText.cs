using System.Buffers;
using System.Text;

namespace Tuinet.Widgets;

/// <summary>
/// The editing core of <see cref="TextInputState"/> and <see cref="TextAreaState"/>: the text, a caret with an
/// optional selection, movement by grapheme cluster and by word, and undo/redo. Offsets count UTF-16 chars and
/// fall on grapheme cluster boundaries. The text lives in a gap buffer, so typing in the middle costs about what
/// typing at the end does. Allocates only when the text or the undo history outgrows its buffers, and when
/// <see cref="Text"/> is read after an edit.
/// </summary>
public abstract class EditableText
{
    private readonly bool _multiline;
    private readonly bool _caretAtEndOnSet;

    // Gap buffer: the text is _chars[.._gapStart] followed by _chars[_gapEnd..].
    private char[] _chars = new char[16];
    private int _gapStart;
    private int _gapEnd = 16;

    // Line index: line i starts at _lineStarts[i], plus _shift from line _shiftFrom on. An edit within a line
    // only moves _shiftFrom and _shift, so typing doesn't touch the starts of the lines after it.
    private int[] _lineStarts = new int[1];
    private int _lineCount = 1;
    private int _shiftFrom = 1;
    private int _shift;

    private EditHistory? _history;
    private bool _sealed = true;   // the next edit starts a new undo step
    private string? _text;

    private protected EditableText(bool multiline, bool caretAtEndOnSet, string? text)
    {
        _multiline = multiline;
        _caretAtEndOnSet = caretAtEndOnSet;
        if (text is not null)
        {
            Set(text);
        }
    }

    /// <summary>Length of the text, in UTF-16 chars.</summary>
    public int Length => _chars.Length - (_gapEnd - _gapStart);

    public bool IsEmpty => Length == 0;

    /// <summary>Caret offset, in chars (always at a grapheme cluster boundary).</summary>
    public int Caret { get; private set; }

    /// <summary>The other end of the selection; equal to <see cref="Caret"/> when nothing is selected.</summary>
    public int Anchor { get; private set; }

    public bool HasSelection => Anchor != Caret;
    public int SelectionStart => Math.Min(Anchor, Caret);
    public int SelectionEnd => Math.Max(Anchor, Caret);

    /// <summary>
    /// The selected text, e.g. for <see cref="Terminal.CopyToClipboard"/>. Empty when nothing is selected. Valid
    /// until the next edit.
    /// </summary>
    public ReadOnlySpan<char> Selection => Slice(SelectionStart, SelectionEnd);

    /// <summary>The whole text. Allocates the first time it is read after an edit.</summary>
    public string Text => _text ??= string.Concat(_chars.AsSpan(0, _gapStart), _chars.AsSpan(_gapEnd));

    public bool CanUndo => _history?.CanUndo == true;
    public bool CanRedo => _history?.CanRedo == true;

    /// <summary>Counts edits, so a widget can tell whether the layout it last rendered still holds.</summary>
    internal int Version { get; private set; }

    /// <summary>Set by edits and caret moves, cleared by the widget once it has scrolled the caret into view.</summary>
    internal bool CaretMoved { get; set; } = true;

    /// <summary>Column that up/down movement aims for, or -1 to take it from the caret.</summary>
    private protected int DesiredX { get; set; } = -1;

    /// <summary>Replace the text. Clears the selection and the undo history.</summary>
    public void Set(ReadOnlySpan<char> text)
    {
        _gapStart = 0;
        _gapEnd = _chars.Length;
        _lineCount = 1;
        _shiftFrom = 1;
        _shift = 0;
        _history?.Clear();
        Caret = Anchor = 0;
        InsertClean(text, EditKind.Other, record: false);
        Caret = Anchor = _caretAtEndOnSet ? Length : 0;
        _sealed = true;
        DesiredX = -1;
        CaretMoved = true;
        OnReset();
    }

    /// <summary>Remove all text. Clears the undo history.</summary>
    public void Clear() => Set(default);

    /// <summary>
    /// Insert text at the caret in place of the selection (e.g. a paste), as one undo step. Control characters are
    /// dropped; a <see cref="TextAreaState"/> keeps line breaks (as '\n') and turns tabs into four spaces.
    /// </summary>
    public void Insert(ReadOnlySpan<char> text)
    {
        _sealed = true;
        InsertClean(text, EditKind.Other, record: true);
        _sealed = true;
    }

    /// <summary>Type one character in place of the selection. Control characters are ignored; marks and joiners are kept.</summary>
    public void Insert(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return;
        }

        Span<char> chars = stackalloc char[2];
        int n = rune.EncodeToUtf16(chars);
        Type(chars[..n]);
    }

    /// <summary>Move the caret to <paramref name="offset"/> (snapped to a cluster boundary); <paramref name="extend"/> keeps the anchor, selecting.</summary>
    public void MoveCaret(int offset, bool extend = false) => MoveTo(Snap(offset), extend);

    /// <summary>Select from <paramref name="anchor"/> to <paramref name="caret"/> (snapped to cluster boundaries).</summary>
    public void Select(int anchor, int caret)
    {
        MoveTo(Snap(anchor));
        MoveTo(Snap(caret), extend: true);
    }

    public void SelectAll() => Select(0, Length);

    /// <summary>Delete the selected text. Returns false when nothing is selected.</summary>
    public bool DeleteSelection()
    {
        if (!HasSelection)
        {
            return false;
        }

        Edit(SelectionStart, SelectionEnd, default, EditKind.Other);
        return true;
    }

    /// <summary>
    /// Undo the last edit, restoring the selection it replaced. Runs of typing, of Backspace and of Delete undo
    /// together, a word at a time. The history keeps the last 1000 steps.
    /// </summary>
    public bool Undo()
    {
        if (_history is null || !_history.Undo(this, out int caret, out int anchor))
        {
            return false;
        }

        Caret = caret;
        Anchor = anchor;
        AfterHistory();
        return true;
    }

    public bool Redo()
    {
        if (_history is null || !_history.Redo(this, out int caret))
        {
            return false;
        }

        Caret = Anchor = caret;
        AfterHistory();
        return true;
    }

    private void AfterHistory()
    {
        _sealed = true;
        DesiredX = -1;
        CaretMoved = true;
    }

    /// <summary>Called by <see cref="Set"/> and <see cref="Clear"/>, for the widget's scroll state.</summary>
    private protected virtual void OnReset()
    {
    }

    /// <summary>
    /// Keys every editor shares: typing, Backspace/Delete (by word with Ctrl or Alt), ←/→ (by word with Ctrl or Alt,
    /// selecting with Shift), Ctrl+Z (or Ctrl+/, Ctrl+_) undo and Ctrl+Y (or Ctrl+Shift+Z) redo.
    /// </summary>
    private protected bool HandleCommon(KeyEvent key)
    {
        Modifiers mods = key.Modifiers;
        bool word = (mods & (Modifiers.Ctrl | Modifiers.Alt)) != 0;
        bool extend = (mods & Modifiers.Shift) != 0;
        switch (key.Code)
        {
            case KeyCode.Char when mods is Modifiers.None or Modifiers.Shift:
                Insert(key.Rune);
                return true;
            case KeyCode.Char when mods == Modifiers.Ctrl && key.Rune.Value is 'z' or '/' or '_':
                Undo();
                return true;
            case KeyCode.Char when mods == Modifiers.Ctrl && key.Rune.Value == 'y'
                || mods == (Modifiers.Ctrl | Modifiers.Shift) && key.Rune.Value is 'z' or 'Z':
                Redo();
                return true;
            case KeyCode.Backspace:
                if (!DeleteSelection())
                {
                    Edit(word ? WordStart(Caret) : PrevCluster(Caret), Caret, default, word ? EditKind.Other : EditKind.Backspace);
                }

                return true;
            case KeyCode.Delete:
                if (!DeleteSelection())
                {
                    Edit(Caret, word ? WordEnd(Caret) : NextCluster(Caret), default, word ? EditKind.Other : EditKind.Delete);
                }

                return true;
            case KeyCode.Left:
                // Without Shift, ← first collapses a selection to its start (→ to its end), as in GUI editors.
                MoveTo(word ? WordStart(Caret) : HasSelection && !extend ? SelectionStart : PrevCluster(Caret), extend);
                return true;
            case KeyCode.Right:
                MoveTo(word ? WordEnd(Caret) : HasSelection && !extend ? SelectionEnd : NextCluster(Caret), extend);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Move the caret (to a boundary); without <paramref name="extend"/> the selection collapses.</summary>
    private protected void MoveTo(int offset, bool extend = false, bool keepColumn = false)
    {
        Caret = Math.Clamp(offset, 0, Length);
        if (!extend)
        {
            Anchor = Caret;
        }

        _sealed = true;
        CaretMoved = true;
        if (!keepColumn)
        {
            DesiredX = -1;
        }
    }

    /// <summary>Typed text (clean) in place of the selection; runs of typing undo together.</summary>
    private protected void Type(ReadOnlySpan<char> text) => Edit(SelectionStart, SelectionEnd, text, EditKind.Type);

    /// <summary>Delete <paramref name="start"/>..<paramref name="end"/>, as one undo step.</summary>
    private protected void Delete(int start, int end) => Edit(start, end, default, EditKind.Other);

    /// <summary>Replace <paramref name="start"/>..<paramref name="end"/> with clean text; the caret ends after it.</summary>
    private void Edit(int start, int end, ReadOnlySpan<char> text, EditKind kind, bool record = true)
    {
        if (start == end && text.IsEmpty)
        {
            return;
        }

        if (record)
        {
            _history ??= new EditHistory();
            _history.Record(this, start, end, text, Caret, Anchor, kind, _sealed);
            _sealed = false;
        }

        Splice(start, end, text);
        Caret = Anchor = start + text.Length;
        DesiredX = -1;
        CaretMoved = true;
    }

    /// <summary>Drop what an editor of this kind can't hold, then insert it in place of the selection.</summary>
    private void InsertClean(ReadOnlySpan<char> text, EditKind kind, bool record)
    {
        if (IsClean(text))
        {
            Edit(SelectionStart, SelectionEnd, text, kind, record);
            return;
        }

        int length = Clean(text, default);
        char[] rented = ArrayPool<char>.Shared.Rent(Math.Max(1, length));
        try
        {
            Clean(text, rented);
            Edit(SelectionStart, SelectionEnd, rented.AsSpan(0, length), kind, record);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>No controls (C0, DEL, C1) and no surrogates to validate.</summary>
    private static bool IsClean(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (c < 0x20 || (uint)(c - 0x7F) <= 0x9F - 0x7F || char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the cleaned text into <paramref name="output"/> (or only measures it when that is empty) and returns
    /// its length: controls dropped, invalid UTF-16 replaced, and in multi-line text CR LF / CR made '\n' and tabs
    /// four spaces.
    /// </summary>
    private int Clean(ReadOnlySpan<char> text, Span<char> output)
    {
        bool write = !output.IsEmpty;
        int n = 0;
        bool afterCr = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            bool wasCr = afterCr;
            afterCr = false;
            if (_multiline && rune.Value is '\n' or '\r')
            {
                afterCr = rune.Value == '\r';
                if (rune.Value == '\n' && wasCr)
                {
                    continue;
                }

                if (write)
                {
                    output[n] = '\n';
                }

                n++;
            }
            else if (_multiline && rune.Value == '\t')
            {
                if (write)
                {
                    output.Slice(n, 4).Fill(' ');
                }

                n += 4;
            }
            else if (!Rune.IsControl(rune))
            {
                n += write ? rune.EncodeToUtf16(output[n..]) : rune.Utf16SequenceLength;
            }
        }

        return n;
    }

    // ---- Text access -------------------------------------------------------------------------------------

    private char At(int index) => index < _gapStart ? _chars[index] : _chars[index + _gapEnd - _gapStart];

    /// <summary>
    /// The text from <paramref name="start"/> to <paramref name="end"/> as one span, valid until the next edit or
    /// slice: the gap moves out of the way if it splits the range (by whichever end is closer).
    /// </summary>
    internal ReadOnlySpan<char> Slice(int start, int end)
    {
        if (start < _gapStart && end > _gapStart)
        {
            MoveGap(_gapStart - start <= end - _gapStart ? start : end);
        }

        int physical = start < _gapStart ? start : start + _gapEnd - _gapStart;
        return _chars.AsSpan(physical, end - start);
    }

    /// <summary>Copy the text from <paramref name="start"/> into <paramref name="destination"/>; returns the chars copied.</summary>
    internal int CopyTo(int start, Span<char> destination)
    {
        int n = Math.Min(destination.Length, Length - start);
        int front = Math.Clamp(_gapStart - start, 0, n);
        if (front > 0)
        {
            _chars.AsSpan(start, front).CopyTo(destination);
        }

        if (n > front)
        {
            _chars.AsSpan(start + front + _gapEnd - _gapStart, n - front).CopyTo(destination[front..]);
        }

        return n;
    }

    /// <summary>Replace <paramref name="start"/>..<paramref name="end"/> with <paramref name="text"/>, keeping the line index in step.</summary>
    internal void Splice(int start, int end, ReadOnlySpan<char> text)
    {
        int startLine = LineAt(start);
        int endLine = end == start ? startLine : LineAt(end);
        int newlines = _multiline ? text.Count('\n') : 0;

        MoveGap(end);
        _gapStart = start;
        if (_gapEnd - _gapStart < text.Length)
        {
            Grow(text.Length);
        }

        text.CopyTo(_chars.AsSpan(_gapStart));
        _gapStart += text.Length;
        _text = null;
        Version++;

        // Lines startLine+1..endLine were deleted; the inserted text adds one per newline. The lines after move by
        // the length change, applied lazily from where they now begin.
        ShiftFrom(endLine + 1);
        int removed = endLine - startLine;
        int count = _lineCount - removed + newlines;
        if (removed != newlines)
        {
            if (count > _lineStarts.Length)
            {
                Array.Resize(ref _lineStarts, Math.Max(count, _lineStarts.Length * 2));
            }

            Array.Copy(_lineStarts, endLine + 1, _lineStarts, startLine + 1 + newlines, _lineCount - endLine - 1);
        }

        for (int line = startLine + 1, from = 0; line <= startLine + newlines; line++)
        {
            from += text[from..].IndexOf('\n') + 1;
            _lineStarts[line] = start + from;
        }

        _lineCount = count;
        _shiftFrom = startLine + 1 + newlines;
        _shift += text.Length - (end - start);
        if (_shiftFrom >= _lineCount)
        {
            _shiftFrom = _lineCount;
            _shift = 0;
        }
    }

    /// <summary>Make <paramref name="line"/> the first line the pending shift applies to.</summary>
    private void ShiftFrom(int line)
    {
        if (_shift != 0)
        {
            for (int i = _shiftFrom; i < line; i++)
            {
                _lineStarts[i] += _shift;
            }

            for (int i = line; i < _shiftFrom; i++)
            {
                _lineStarts[i] -= _shift;
            }
        }

        _shiftFrom = line;
    }

    private void MoveGap(int offset)
    {
        if (offset < _gapStart)
        {
            int n = _gapStart - offset;
            _chars.AsSpan(offset, n).CopyTo(_chars.AsSpan(_gapEnd - n));
            _gapStart = offset;
            _gapEnd -= n;
        }
        else if (offset > _gapStart)
        {
            int n = offset - _gapStart;
            _chars.AsSpan(_gapEnd, n).CopyTo(_chars.AsSpan(_gapStart));
            _gapStart = offset;
            _gapEnd += n;
        }
    }

    private void Grow(int needed)
    {
        int length = Length;
        int size = Math.Max(_chars.Length * 2, length + needed + 16);
        var chars = new char[size];
        int tail = _chars.Length - _gapEnd;
        _chars.AsSpan(0, _gapStart).CopyTo(chars);
        _chars.AsSpan(_gapEnd).CopyTo(chars.AsSpan(size - tail));
        _chars = chars;
        _gapEnd = size - tail;
    }

    // ---- Lines -------------------------------------------------------------------------------------------

    internal int Lines => _lineCount;

    internal int LineStartAt(int line) => _lineStarts[line] + (line >= _shiftFrom ? _shift : 0);

    /// <summary>Where <paramref name="line"/> ends, before its '\n'.</summary>
    internal int LineEndAt(int line) => line + 1 < _lineCount ? LineStartAt(line + 1) - 1 : Length;

    /// <summary>The line holding <paramref name="offset"/> (an offset right after a '\n' is on the next line).</summary>
    internal int LineAt(int offset)
    {
        int lo = 0;
        int hi = _lineCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (LineStartAt(mid) <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    internal ReadOnlySpan<char> LineSpan(int line) => Slice(LineStartAt(line), LineEndAt(line));

    // ---- Clusters and words ------------------------------------------------------------------------------

    /// <summary>Length in chars of the grapheme cluster at the start of <paramref name="text"/> (non-empty).</summary>
    internal static int ClusterLength(ReadOnlySpan<char> text) =>
        text[0] < 0x80 && (text.Length == 1 || text[1] < 0x300) ? 1 : Graphemes.Length(text);

    /// <summary>Offset after the grapheme cluster at <paramref name="offset"/>.</summary>
    internal int NextCluster(int offset)
    {
        int length = Length;
        if (offset >= length)
        {
            return length;
        }

        if (At(offset) < 0x80 && (offset + 1 == length || At(offset + 1) < 0x300))
        {
            return offset + 1;
        }

        Span<char> window = stackalloc char[2 * Graphemes.MaxChars];
        int n = CopyTo(offset, window);
        return offset + Graphemes.Length(window[..n]);
    }

    /// <summary>Offset where the grapheme cluster before <paramref name="offset"/> (a boundary) starts.</summary>
    internal int PrevCluster(int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        int start = BoundaryBefore(offset);
        while (true)
        {
            int next = NextCluster(start);
            if (next >= offset)
            {
                return start;
            }

            start = next;
        }
    }

    /// <summary>The start of the cluster holding <paramref name="offset"/>, which is clamped to the text.</summary>
    internal int Snap(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        if (offset == 0 || offset == Length)
        {
            return offset;
        }

        int start = BoundaryBefore(offset + 1);
        while (start < offset)
        {
            int next = NextCluster(start);
            if (next > offset)
            {
                return start;
            }

            start = next;
        }

        return start;
    }

    /// <summary>
    /// A cluster boundary before <paramref name="offset"/>, found by scanning back to a pair that can't join (two
    /// Latin or CJK chars, or a line break). Clusters are then walked forward from it.
    /// </summary>
    private int BoundaryBefore(int offset)
    {
        for (int i = offset - 1; i > 0; i--)
        {
            char c = At(i);
            char before = At(i - 1);
            if (c == '\n' || before == '\n' || Graphemes.Simple(c) && Graphemes.Simple(before))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Start of the word before <paramref name="from"/> (skipping whitespace first; line breaks are whitespace).</summary>
    internal int WordStart(int from)
    {
        int i = from;
        while (i > 0 && char.IsWhiteSpace(At(i - 1))) i--;
        while (i > 0 && !char.IsWhiteSpace(At(i - 1))) i--;
        return Snap(i);
    }

    /// <summary>End of the word after <paramref name="from"/>.</summary>
    internal int WordEnd(int from)
    {
        int length = Length;
        int i = from;
        while (i < length && char.IsWhiteSpace(At(i))) i++;
        while (i < length && !char.IsWhiteSpace(At(i))) i++;
        return Snap(i);
    }
}

internal enum EditKind : byte
{
    Other,
    Type,
    Backspace,
    Delete,
}

/// <summary>
/// Undo/redo steps for an <see cref="EditableText"/>. Each step's removed and inserted text sit back to back in one
/// shared char log, so recording an edit allocates nothing once the log has grown. Consecutive typing, Backspace or
/// Delete merge into one step (typing a space after a word starts a new one). Bounded: past
/// <see cref="MaxSteps"/> steps or <see cref="MaxChars"/> chars the oldest quarter is dropped.
/// </summary>
internal sealed class EditHistory
{
    private const int MaxSteps = 1000;
    private const int MaxChars = 1 << 20;

    private Step[] _steps = new Step[16];
    private int _count;     // recorded steps; the ones past _applied were undone and can be redone
    private int _applied;
    private char[] _log = new char[64];
    private int _logLength;

    public bool CanUndo => _applied > 0;
    public bool CanRedo => _applied < _count;

    public void Clear()
    {
        _count = 0;
        _applied = 0;
        _logLength = 0;
    }

    public void Record(EditableText text, int start, int end, ReadOnlySpan<char> inserted, int caret, int anchor, EditKind kind, bool sealedStep)
    {
        // A new edit drops what could be redone.
        _count = _applied;
        _logLength = _count > 0 ? _steps[_count - 1].End : 0;
        int removed = end - start;

        if (!sealedStep && _count > 0 && Merge(ref _steps[_count - 1], text, start, end, inserted, kind))
        {
            return;
        }

        Reserve(removed + inserted.Length);
        text.CopyTo(start, _log.AsSpan(_logLength, removed));
        inserted.CopyTo(_log.AsSpan(_logLength + removed));
        if (_count == _steps.Length)
        {
            Array.Resize(ref _steps, _steps.Length * 2);
        }

        _steps[_count++] = new Step
        {
            Offset = start,
            At = _logLength,
            Removed = removed,
            Inserted = inserted.Length,
            Caret = caret,
            Anchor = anchor,
            Kind = kind,
        };
        _logLength += removed + inserted.Length;
        _applied = _count;
        Trim();
    }

    /// <summary>Fold the edit into the last step when it continues it. That step's text ends the log.</summary>
    private bool Merge(ref Step last, EditableText text, int start, int end, ReadOnlySpan<char> inserted, EditKind kind)
    {
        int removed = end - start;
        if (kind != last.Kind)
        {
            return false;
        }

        switch (kind)
        {
            case EditKind.Type when removed == 0 && start == last.Offset + last.Inserted:
                bool space = inserted.Length > 0 && char.IsWhiteSpace(inserted[0]);
                bool afterWord = last.Inserted > 0 && !char.IsWhiteSpace(_log[last.End - 1]);
                if (space && afterWord)
                {
                    return false;
                }

                Reserve(inserted.Length);
                inserted.CopyTo(_log.AsSpan(_logLength));
                last.Inserted += inserted.Length;
                _logLength += inserted.Length;
                return true;

            case EditKind.Backspace when inserted.IsEmpty && last.Inserted == 0 && end == last.Offset:
                // The newly removed text goes in front of what this step removed so far.
                Reserve(removed);
                _log.AsSpan(last.At, last.Removed).CopyTo(_log.AsSpan(last.At + removed));
                text.CopyTo(start, _log.AsSpan(last.At, removed));
                last.Offset = start;
                last.Removed += removed;
                _logLength += removed;
                return true;

            case EditKind.Delete when inserted.IsEmpty && last.Inserted == 0 && start == last.Offset:
                Reserve(removed);
                text.CopyTo(start, _log.AsSpan(_logLength, removed));
                last.Removed += removed;
                _logLength += removed;
                return true;

            default:
                return false;
        }
    }

    public bool Undo(EditableText text, out int caret, out int anchor)
    {
        caret = anchor = 0;
        if (_applied == 0)
        {
            return false;
        }

        Step step = _steps[--_applied];
        text.Splice(step.Offset, step.Offset + step.Inserted, _log.AsSpan(step.At, step.Removed));
        caret = step.Caret;
        anchor = step.Anchor;
        return true;
    }

    public bool Redo(EditableText text, out int caret)
    {
        caret = 0;
        if (_applied == _count)
        {
            return false;
        }

        Step step = _steps[_applied++];
        text.Splice(step.Offset, step.Offset + step.Removed, _log.AsSpan(step.At + step.Removed, step.Inserted));
        caret = step.Offset + step.Inserted;
        return true;
    }

    private void Reserve(int chars)
    {
        if (_logLength + chars > _log.Length)
        {
            Array.Resize(ref _log, Math.Max(_log.Length * 2, _logLength + chars));
        }
    }

    /// <summary>Over a bound, drop the oldest steps until a quarter of the room is free again (always keeping the newest).</summary>
    private void Trim()
    {
        if (_count <= MaxSteps && _logLength <= MaxChars)
        {
            return;
        }

        int drop = 0;
        while (drop < _count - 1
            && (_count - drop > MaxSteps * 3 / 4 || _logLength - _steps[drop].At > MaxChars * 3 / 4))
        {
            drop++;
        }

        drop = Math.Max(drop, 1);
        int shift = _steps[drop].At;
        _log.AsSpan(shift, _logLength - shift).CopyTo(_log);
        _logLength -= shift;
        Array.Copy(_steps, drop, _steps, 0, _count - drop);
        _count -= drop;
        _applied -= drop;
        for (int i = 0; i < _count; i++)
        {
            _steps[i].At -= shift;
        }
    }

    private struct Step
    {
        public int Offset;
        public int At;         // removed text, then inserted text, in the log
        public int Removed;
        public int Inserted;
        public int Caret;      // before the edit, with the anchor: undo restores the selection
        public int Anchor;
        public EditKind Kind;

        public readonly int End => At + Removed + Inserted;
    }
}
