#!/usr/bin/env python3
"""End-to-end checks of TUI.NET apps in a real terminal implementation: tmux.

Each app runs in a detached tmux session (private server, no user config) inside a small shell wrapper that
records the exit code and the tty modes before and after, with HOME pointed at an empty directory. Checks
read the screen back with `capture-pane`; waits poll the screen, so slow machines don't make them flaky.

  showcase  navigation, edit + save, progress animation, mouse (click, wheel, scrollbar drag, header sort),
            clean exit; at 110x34 and 80x24
  inline    the inline sample under an interactive bash: logs printed above the live band end up in order
            in the history (also on a short terminal, where they scroll), the band leaves one summary line,
            the shell continues right after it, Ctrl+C cancels, terminal modes are restored
  suspend   job control in an interactive bash: Ctrl+Z, kill -TSTP and kill -STOP each return to the
            shell (in cooked mode where the app could restore it), and fg brings the screen back intact
  stress    scrolling bursts: every visible row has the right content, in order, and the terminal
            really received scroll-region (IL/DL) sequences
  clusters  grapheme clusters: the screen TUI.NET produces equals a reference that places every cell
            with an absolute cursor move (cases in clusters.txt)

Usage: e2e.py [--showcase BIN] [--stress BIN] [--clusters BIN --cases FILE]
Exit status 1 if any check fails. Needs Python 3.8+ and tmux.
"""
import argparse
import os
import re
import shlex
import signal
import subprocess
import sys
import tempfile
import time

TIMEOUT = 10.0
failures = 0


def check(ok, label, screen=None):
    global failures
    print(("ok    " if ok else "FAIL  ") + label, flush=True)
    if not ok:
        failures += 1
        if screen is not None:
            print("      screen:\n" + "\n".join("      |" + line for line in screen.splitlines()), flush=True)
    return ok


class Tmux:
    """A private tmux server: -L gives it its own socket, -f /dev/null ignores the user's config."""

    def __init__(self):
        self.socket = f"tuinet-e2e-{os.getpid()}"

    def run(self, *args):
        return subprocess.run(["tmux", "-L", self.socket, "-f", "/dev/null", *args],
                              capture_output=True, text=True).stdout

    def start(self, name, width, height, command):
        self.run("new-session", "-d", "-s", name, "-x", str(width), "-y", str(height), command)
        self.run("set-option", "-t", name, "window-size", "manual")   # resize-window keeps the size it is given

    def screen(self, name):
        return self.run("capture-pane", "-p", "-t", name)

    def keys(self, name, *keys):
        self.run("send-keys", "-t", name, *keys)

    def type(self, name, text):
        self.run("send-keys", "-t", name, "-l", text)

    def mouse(self, name, x, y, button=0):
        """An SGR (1006) mouse press and release at 0-based cell (x, y); button 64/65 is the wheel up/down."""
        seq = f"\x1b[<{button};{x + 1};{y + 1}M"
        if button < 64:
            seq += f"\x1b[<{button};{x + 1};{y + 1}m"
        self.run("send-keys", "-t", name, "-l", seq)

    def drag(self, name, x0, y0, x1, y1):
        """An SGR left-button press at (x0, y0), a drag to (x1, y1) and the release there."""
        seq = f"\x1b[<0;{x0 + 1};{y0 + 1}M\x1b[<32;{x1 + 1};{y1 + 1}M\x1b[<0;{x1 + 1};{y1 + 1}m"
        self.run("send-keys", "-t", name, "-l", seq)

    def find(self, name, text):
        """0-based cell of the first occurrence of text on screen, or None."""
        for y, line in enumerate(self.screen(name).split("\n")):
            x = line.find(text)
            if x >= 0:
                return x, y
        return None

    def log_output(self, name, path):
        self.run("pipe-pane", "-o", "-t", name, f"cat >> {shlex.quote(path)}")

    def wait(self, name, predicate, timeout=TIMEOUT):
        """Poll the screen until predicate(screen) holds; returns (ok, screen)."""
        end = time.time() + timeout
        while True:
            text = self.screen(name)
            if predicate(text):
                return True, text
            if time.time() > end:
                return False, text
            time.sleep(0.05)

    def settle(self, name, quiet=0.3, timeout=TIMEOUT):
        """Wait until the screen stops changing for `quiet` seconds; returns the screen."""
        end = time.time() + timeout
        last, since = self.screen(name), time.time()
        while time.time() < end:
            time.sleep(0.05)
            text = self.screen(name)
            if text != last:
                last, since = text, time.time()
            elif time.time() - since >= quiet:
                break
        return last

    def stop(self):
        self.run("kill-server")


class App:
    """An app in a wrapper that records exit code and tty modes, with an empty HOME."""

    def __init__(self, tmux, name, width, height, argv):
        self.tmux, self.name = tmux, name
        self.dir = tempfile.mkdtemp(prefix=f"tuinet-e2e-{name}-")
        self.home = os.path.join(self.dir, "home")
        os.makedirs(self.home)
        d = shlex.quote(self.dir)
        app = " ".join(shlex.quote(a) for a in argv)
        script = (f"stty -g > {d}/before; HOME={shlex.quote(self.home)} XDG_CONFIG_HOME={shlex.quote(self.home)}/.config "
                  f"{app}; echo $? > {d}/exit.tmp; stty -g > {d}/after; mv {d}/exit.tmp {d}/exit")
        tmux.start(name, width, height, f"sh -c {shlex.quote(script)}")

    def finish(self, label):
        """After the app was told to quit: exit code 0, tty modes restored, nothing written under HOME."""
        exit_file = os.path.join(self.dir, "exit")
        end = time.time() + TIMEOUT
        while not os.path.exists(exit_file) and time.time() < end:
            time.sleep(0.05)
        if not check(os.path.exists(exit_file), f"{label}: quits"):
            return
        code = open(exit_file).read().strip()
        check(code == "0", f"{label}: exit code 0 (got {code})")
        before = open(os.path.join(self.dir, "before")).read()
        after = open(os.path.join(self.dir, "after")).read()
        check(before == after, f"{label}: terminal modes restored")
        written = [os.path.join(r, f) for r, _, fs in os.walk(self.home) for f in fs]
        check(written == [], f"{label}: wrote no files under HOME" + (f" {written}" if written else ""))


def showcase(tmux, binary, width, height):
    label = f"showcase {width}x{height}"
    app = App(tmux, f"showcase{width}", width, height, [binary])
    n = app.name
    ok, s = tmux.wait(n, lambda t: "ITEMS · 20" in t)
    check(ok and "▲ #  name" in s and "───" in s, f"{label}: table with header, sort arrow and rule", s)

    tmux.type(n, "G")
    ok, s = tmux.wait(n, lambda t: "SELECTED · 20" in t and "20  telemetry" in t)
    check(ok, f"{label}: G jumps to the last row and keeps it in view", s)
    tmux.type(n, "g")
    ok, s = tmux.wait(n, lambda t: "SELECTED · 01" in t and "01  parse" in t)
    check(ok, f"{label}: g goes back to the first row", s)

    tmux.type(n, "jj")
    tmux.keys(n, "Enter")
    ok, s = tmux.wait(n, lambda t: "EDIT ITEM · 03" in t and "notify on finish" in t)
    check(ok, f"{label}: enter opens the edit dialog on row 3", s)
    tmux.keys(n, "C-u")
    tmux.type(n, "hello world")
    tmux.keys(n, "C-s")
    ok, s = tmux.wait(n, lambda t: "03  hello world" in t and "saved · hello world" in t)
    check(ok, f"{label}: ctrl+s saves and the table shows the new name", s)

    tmux.type(n, "p")
    ok, s = tmux.wait(n, lambda t: "PIPELINE" in t)
    check(ok, f"{label}: p opens the progress dialog", s)
    first = tmux.screen(n)
    ok, s = tmux.wait(n, lambda t: t != first, timeout=3)
    check(ok, f"{label}: the progress dialog animates without input", s)
    tmux.keys(n, "Escape")
    ok, s = tmux.wait(n, lambda t: "PIPELINE" not in t)
    check(ok, f"{label}: esc closes the progress dialog", s)

    showcase_mouse(tmux, n, label, height)
    tmux.type(n, "q")
    app.finish(label)


def showcase_mouse(tmux, n, label, height):
    """Real SGR mouse reports through tmux: coordinates, the parser and the widgets' hit-testing together."""
    at = tmux.find(n, "05  schema")
    if not check(at is not None, f"{label}: mouse: row 05 is on screen", tmux.screen(n)):
        return
    tmux.mouse(n, *at)
    ok, s = tmux.wait(n, lambda t: "SELECTED · 05" in t)
    check(ok, f"{label}: mouse: a click selects the row", s)

    if height < 30:                                     # only 12 of 20 rows fit: the wheel can scroll
        top = tmux.find(n, "01  parse")
        tmux.mouse(n, *top, button=65)
        ok, s = tmux.wait(n, lambda t: "01  parse" not in t and "04  verify" in t and "SELECTED · 05" in t)
        check(ok, f"{label}: mouse: the wheel scrolls the table and keeps the selection", s)
        tmux.mouse(n, *top, button=64)
        tmux.wait(n, lambda t: "01  parse" in t)

        # The scrollbar runs down the table's rightmost column, just inside the box's padding.
        bar = tmux.screen(n).split("\n")[top[1]].rfind("│") - 2
        last = tmux.find(n, "12  diff")
        tmux.drag(n, bar, top[1], bar, last[1])
        ok, s = tmux.wait(n, lambda t: "01  parse" not in t and "20  telemetry" in t and "SELECTED · 05" in t)
        check(ok, f"{label}: mouse: dragging the scrollbar scrolls to the end and keeps the selection", s)
        tmux.drag(n, bar, last[1], bar, top[1])
        ok, s = tmux.wait(n, lambda t: "01  parse" in t)
        check(ok, f"{label}: mouse: dragging it back scrolls to the top", s)

    header = tmux.find(n, "name  ")
    tmux.mouse(n, *header)
    # Sorted by name, and the view follows the selected item (at 80x24 the first rows scroll off).
    ok, s = tmux.wait(n, lambda t: "name ▲" in t and "SELECTED · 05" in t
                      and -1 < t.find("12  diff") < t.find("06  ephemeral") < t.find("05  schema"))
    check(ok, f"{label}: mouse: a header click sorts by that column", s)
    tmux.mouse(n, *tmux.find(n, "name ▲"))
    ok, s = tmux.wait(n, lambda t: "name ▼" in t)
    check(ok, f"{label}: mouse: a second header click reverses the order", s)


def suspend(tmux, binary):
    """The showcase under an interactive bash, which does job control (Stopped / fg) like a user's shell."""
    label, n = "suspend", "suspend"
    d = tempfile.mkdtemp(prefix="tuinet-e2e-suspend-")
    home = os.path.join(d, "home")
    os.makedirs(home)
    shell = (f"env -i PATH=\"$PATH\" TERM=\"$TERM\" HOME={shlex.quote(home)} PS1='$ ' "
             "bash --norc --noprofile -i")
    tmux.start(n, 80, 24, shell)
    tmux.wait(n, lambda t: "$" in t)
    q = shlex.quote
    # A wrapper script, as in App: bash abandons the rest of a command list once a job stops. It shares the
    # app's process group, so it also checks that the app stops the whole group, as Ctrl+Z would.
    script = f"stty -g > {q(d)}/before; {q(binary)}; echo $? > {q(d)}/exit; stty -g > {q(d)}/after"
    tmux.type(n, f"sh -c {q(script)}")
    tmux.keys(n, "Enter")
    ok, s = tmux.wait(n, lambda t: "ITEMS · 20" in t)
    check(ok, f"{label}: showcase starts under an interactive shell", s)
    tmux.type(n, "jj")
    ok, s = tmux.wait(n, lambda t: "SELECTED · 03" in t)

    def child(pid):
        out = subprocess.run(["pgrep", "-P", str(pid)], capture_output=True, text=True).stdout.split()
        return int(out[0]) if out else None

    wrapper = child(tmux.run("display", "-p", "-t", n, "#{pane_pid}").strip())

    def back_at_shell(how, cooked):
        ok, s = tmux.wait(n, lambda t: "Stopped" in t)
        check(ok, f"{label}: {how} stops the app and the shell reports it", s)
        if cooked:
            check("ITEMS · 20" not in s, f"{label}: {how} leaves the app's screen", s)
            tmux.keys(n, "clear", "Enter")
            tmux.type(n, f"stty -g > {q(d)}/stopped; echo shell-$((6*7))")
            tmux.keys(n, "Enter")
            ok, s = tmux.wait(n, lambda t: "shell-42" in t)
            check(ok, f"{label}: {how}: the shell takes commands", s)
            stopped_file = os.path.join(d, "stopped")
            stopped = open(stopped_file).read() if os.path.exists(stopped_file) else ""
            check(stopped == open(os.path.join(d, "before")).read(), f"{label}: {how}: terminal modes restored while stopped")

    def resumes(how, selected):
        tmux.type(n, "fg")
        tmux.keys(n, "Enter")
        ok, s = tmux.wait(n, lambda t: "ITEMS · 20" in t and f"SELECTED · {selected:02}" in t and "Stopped" not in t)
        check(ok, f"{label}: fg after {how} repaints the whole screen", s)
        tmux.type(n, "j")
        ok, s = tmux.wait(n, lambda t: f"SELECTED · {selected + 1:02}" in t)
        check(ok, f"{label}: keys work after {how} (raw mode again)", s)

    tmux.keys(n, "C-z")
    back_at_shell("ctrl+z", cooked=True)
    resumes("ctrl+z", 3)

    pid = child(wrapper) if wrapper else None
    check(pid is not None, f"{label}: found the app's pid")
    if pid:
        # Only the app gets SIGTSTP; it must leave the screen and then stop its whole group.
        os.kill(pid, signal.SIGTSTP)
        back_at_shell("kill -TSTP", cooked=True)
        resumes("kill -TSTP", 4)
        # SIGSTOP can't be caught: the job stops on the app's screen, and SIGCONT must bring it back.
        os.killpg(os.getpgid(pid), signal.SIGSTOP)
        back_at_shell("kill -STOP", cooked=False)
        resumes("kill -STOP", 5)

    tmux.type(n, "q")
    exit_file, after_file = os.path.join(d, "exit"), os.path.join(d, "after")
    end = time.time() + TIMEOUT
    while not os.path.exists(after_file) and time.time() < end:
        time.sleep(0.05)
    time.sleep(0.1)
    code = open(exit_file).read().strip() if os.path.exists(exit_file) else "none"
    check(code == "0", f"{label}: exit code 0 (got {code})")
    after = open(after_file).read() if os.path.exists(after_file) else ""
    check(after == open(os.path.join(d, "before")).read(), f"{label}: terminal modes restored")
    written = [os.path.join(r, f) for r, _, fs in os.walk(home) for f in fs if not f.startswith(".bash_history")]
    check(written == [], f"{label}: wrote no files under HOME" + (f" {written}" if written else ""))


def inline(tmux, binary, width, height, cancel=False, resize=None):
    """The inline sample run like a CLI tool: everything it printed must read cleanly in the history."""
    label = f"inline {width}x{height}" + (" ctrl+c" if cancel else "") + (f" resized to {resize[0]}x{resize[1]}" if resize else "")
    n = f"inline{width}x{height}{'c' if cancel else ''}{'r%dx%d' % resize if resize else ''}"
    d = tempfile.mkdtemp(prefix=f"tuinet-e2e-{n}-")
    tmux.start(n, width, height, f"env -i PATH=\"$PATH\" TERM=\"$TERM\" PS1='$ ' bash --norc --noprofile -i")
    tmux.wait(n, lambda t: "$" in t)
    q = shlex.quote
    tmux.type(n, f"clear; echo before; stty -g > {q(d)}/before; {q(binary)}; echo after=$?; stty -g > {q(d)}/after")
    tmux.keys(n, "Enter")
    if cancel or resize:
        ok, s = tmux.wait(n, lambda t: "downloading" in t and "3 of 8 done" in t)
        check(ok, f"{label}: the live band shows", s)
    if cancel:
        tmux.keys(n, "C-c")
    if resize:
        # Narrowing is left out: terminals that reflow long lines (tmux among them) move the cursor in ways
        # that can leave a stale copy of the band above it. Taller, shorter and wider must stay clean.
        tmux.run("resize-window", "-t", n, "-x", str(resize[0]), "-y", str(resize[1]))
    ok, s = tmux.wait(n, lambda t: "after=" in t, timeout=20)
    history = tmux.run("capture-pane", "-p", "-S", "-", "-t", n)
    lines = [line.rstrip() for line in history.split("\n") if line.strip()]
    check(ok and "after=0" in lines, f"{label}: exits with code 0", history)
    if not ok:
        return

    start = lines.index("before") + 1
    end = lines.index("after=0")
    output = lines[start:end]
    logs = [line for line in output if line.startswith("✓ ") and " MB in " in line and " files " not in line]
    if cancel:
        check(output[-2:-1] == ["✗ cancelled"] and output[-1].startswith("✗ ") and "of 8 files" in output[-1],
              f"{label}: ctrl+c prints 'cancelled' and leaves a summary line", "\n".join(output))
        check(output[:-2] == logs, f"{label}: only finished files above it", "\n".join(output))
    else:
        check(len(logs) == 8 and len(set(logs)) == 8, f"{label}: all 8 files logged once each", "\n".join(output))
        check(output == logs + [output[-1]] and output[-1].startswith("✓ 8 of 8 files"),
              f"{label}: logs, then one summary line, then the shell (no leftover bars)", "\n".join(output))
    after = open(os.path.join(d, "after")).read() if os.path.exists(os.path.join(d, "after")) else ""
    check(after == open(os.path.join(d, "before")).read(), f"{label}: terminal modes restored")


def stress(tmux, binary):
    label = "stress"
    app = App(tmux, "stress", 100, 30, [binary])
    n = app.name
    log = os.path.join(app.dir, "output.log")
    tmux.log_output(n, log)
    tmux.wait(n, lambda t: "item 000000" in t)

    def rows_are_right(step, screen):
        items, bad = [], []
        for line in screen.splitlines():
            m = re.search(r"item (\d{6})  0x([0-9A-F]+)", line)
            if m:
                index, value = int(m.group(1)), int(m.group(2), 16)
                if value != index * 2654435761:
                    bad.append(line)
                items.append(index)
        lines = screen.splitlines()
        in_order = bool(items) and items == list(range(items[0], items[0] + len(items)))
        one_selected = sum("▶" in line for line in lines) == 1
        borders = bool(lines) and lines[0].lstrip()[:1] in "┌╭" and any(l.lstrip()[:1] in "└╰" for l in lines)
        span = f"rows {items[0]}..{items[-1]}" if items else "no rows"
        check(not bad and in_order and len(items) >= 20 and one_selected and borders,
              f"{label}: {step}: {span}, content and order right", screen)

    rows_are_right("start", tmux.settle(n))
    for step, keys in [("40 j", "j" * 40), ("30 k", "k" * 30), ("page down", None), ("3 j", "jjj"),
                       ("page up", None), ("120 j burst", "j" * 120), ("5 k", "kkkkk")]:
        if keys is None:
            tmux.keys(n, "NPage" if step == "page down" else "PPage")
        else:
            for chunk in range(0, len(keys), 20):   # bursts of 20 keys: several arrive per frame
                tmux.type(n, keys[chunk:chunk + 20])
        rows_are_right(step, tmux.settle(n))

    tmux.type(n, "q")
    app.finish(label)
    data = open(log, "rb").read() if os.path.exists(log) else b""
    moves = len(re.findall(rb"\x1b\[\d*[LM]", data))
    check(moves > 0, f"{label}: scrolling used insert/delete line ({moves} times)")


def read_cases(path):
    rows = []
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if line and not line.startswith("#"):
            rows.append([(piece.rsplit(" ", 1)[0], int(piece.rsplit(" ", 1)[1])) for piece in line.split("\t")])
    return rows


def reference_screen(cases_path):
    """Draw the cluster cases with an absolute cursor move before every cell, then wait to be killed."""
    out = ["\x1b[?1049h\x1b[?25l\x1b[?7l\x1b[?2027h\x1b[2J"]
    for y, pieces in enumerate(read_cases(cases_path)):
        x = 2
        out.append(f"\x1b[{y + 1};{x + 1}Ha")
        x += 1
        for text, width in pieces:
            out.append(f"\x1b[{y + 1};{x + 1}H{text}")
            x += width
        out.append(f"\x1b[{y + 1};{x + 1}Hb\x1b[{y + 1};13HEND")
    sys.stdout.write("".join(out))
    sys.stdout.flush()
    time.sleep(3600)


def clusters(tmux, binary, cases_path):
    label = "clusters"
    count = len(read_cases(cases_path))
    reference = f"python3 {shlex.quote(os.path.abspath(__file__))} --reference {shlex.quote(cases_path)}"
    tmux.start("reference", 40, count + 3, reference)
    ok, expected = tmux.wait("reference", lambda t: t.count("END") == count)
    check(ok, f"{label}: reference screen drawn", expected)

    app = App(tmux, "clusters", 40, count + 3, [binary, cases_path])
    ok, first = tmux.wait(app.name, lambda t: t.count("END") == count)
    first = tmux.settle(app.name)
    check(first == expected, f"{label}: {count} rows match the reference", diff(expected, first))
    tmux.type(app.name, "r")
    time.sleep(0.2)
    repaint = tmux.settle(app.name)
    check(repaint == expected, f"{label}: still match after a full repaint", diff(expected, repaint))
    tmux.type(app.name, "q")
    app.finish(label)


def diff(expected, actual):
    lines = []
    for i, (e, a) in enumerate(zip(expected.splitlines(), actual.splitlines())):
        if e != a:
            lines.append(f"row {i}: expected {e!r}\n        actual   {a!r}")
    return "\n".join(lines) or None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--showcase", help="Tuinet.Samples.Showcase binary")
    parser.add_argument("--stress", help="Tuinet.Samples.Stress binary")
    parser.add_argument("--clusters", help="ClusterScreen binary (tools/e2e/ClusterScreen)")
    parser.add_argument("--inline", help="Tuinet.Samples.Inline binary")
    parser.add_argument("--cases", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "clusters.txt"))
    parser.add_argument("--reference", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.reference:
        reference_screen(args.reference)
        return

    print(f"tmux: {subprocess.run(['tmux', '-V'], capture_output=True, text=True).stdout.strip()}", flush=True)
    tmux = Tmux()
    try:
        if args.showcase:
            showcase(tmux, os.path.abspath(args.showcase), 110, 34)
            showcase(tmux, os.path.abspath(args.showcase), 80, 24)
            suspend(tmux, os.path.abspath(args.showcase))
        if args.inline:
            inline(tmux, os.path.abspath(args.inline), 90, 20)
            inline(tmux, os.path.abspath(args.inline), 80, 8)      # 8 log lines + band don't fit: they scroll
            inline(tmux, os.path.abspath(args.inline), 90, 20, cancel=True)
            for size in [(90, 9), (130, 20), (90, 30)]:
                inline(tmux, os.path.abspath(args.inline), 90, 20, resize=size)
        if args.stress:
            stress(tmux, os.path.abspath(args.stress))
        if args.clusters:
            clusters(tmux, os.path.abspath(args.clusters), os.path.abspath(args.cases))
    finally:
        tmux.stop()

    print("ALL OK" if failures == 0 else f"{failures} FAILED", flush=True)
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
