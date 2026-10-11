#!/usr/bin/env python3
"""Generate src/Tuinet/Internal/UnicodeWidth.g.cs from the Unicode Character Database.

Usage: tools/gen-width/gen.py [UCD_VERSION]   (default 17.0.0; downloads into a temp dir, or reads UCD_DIR)

One two-stage table, 4 bits per code point: the width in the low 2 bits, "never joins" in bit 2.

Width rules (terminal column count, wcwidth-compatible):
  0  controls (Cc), combining marks (Mn, Me), format chars (Cf) except prepended
     concatenation marks and SOFT HYPHEN, line/paragraph separators, Hangul medial/final jamo
  2  East_Asian_Width W or F, or Emoji_Presentation=Yes
  1  everything else (including ambiguous and unassigned non-wide code points)

"Never joins" (Graphemes.Simple): two code points that both have the bit can
never be in one grapheme cluster (UAX #29), so text made of them needs no cluster segmentation and
the renderer need not keep neighbouring cells apart. Derived from GraphemeBreakProperty and
emoji-data:
  not simple  Grapheme_Cluster_Break in Extend, ZWJ, SpacingMark, Prepend, Regional_Indicator,
              L, V, T (Hangul jamo): they extend or start clusters with a neighbour
              surrogates (the lookup is also made with UTF-16 code units)
              the emoji plane, U+1F000 and up (every Extended_Pictographic code point outside the
              BMP is there): the renderer places the cell after a lone code point from it
              explicitly, since terminals disagree on their widths
  simple      everything else: Other, Control, CR, LF, and LV/LVT syllables (they join only with
              jamo, which are not simple). Extended_Pictographic code points in the BMP (©, ★, ⌚ …)
              are simple too: they form clusters only through VS16, ZWJ or a modifier, none of
              which is simple.
"""
import os, sys, tempfile, urllib.request

VERSION = sys.argv[1] if len(sys.argv) > 1 else "17.0.0"
BASE = f"https://www.unicode.org/Public/{VERSION}/ucd/"
FILES = ["EastAsianWidth.txt", "emoji/emoji-data.txt", "extracted/DerivedGeneralCategory.txt",
         "auxiliary/GraphemeBreakProperty.txt"]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "Tuinet", "Internal", "UnicodeWidth.g.cs")
MAX = 0x110000
BLOCK = 256
EMOJI_PLANE = 0x1F000
JOINING = {"Extend", "ZWJ", "SpacingMark", "Prepend", "Regional_Indicator", "L", "V", "T"}

def fetch(name, cache):
    path = os.path.join(cache, os.path.basename(name))
    if not os.path.exists(path):
        urllib.request.urlretrieve(BASE + name, path)
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.split("#", 1)[0].strip()
            if not line:
                continue
            cps, value = [p.strip() for p in line.split(";")[:2]]
            lo, _, hi = cps.partition("..")
            yield int(lo, 16), int(hi or lo, 16), value

def main():
    cache = os.environ.get("UCD_DIR") or tempfile.mkdtemp(prefix="ucd-")
    width = bytearray([1]) * MAX
    simple = bytearray([1]) * MAX

    for lo, hi, eaw in fetch(FILES[0], cache):
        if eaw in ("W", "F"):
            width[lo:hi + 1] = b"\x02" * (hi - lo + 1)
    pictographic = 0
    for lo, hi, prop in fetch(FILES[1], cache):
        if prop == "Emoji_Presentation":
            width[lo:hi + 1] = b"\x02" * (hi - lo + 1)
        if prop == "Extended_Pictographic":
            pictographic += sum(1 for cp in range(lo, hi + 1) if cp >= EMOJI_PLANE)

    keep = {0x00AD, 0x0600, 0x0601, 0x0602, 0x0603, 0x0604, 0x0605, 0x06DD, 0x070F,
            0x0890, 0x0891, 0x08E2, 0x110BD, 0x110CD}
    for lo, hi, gc in fetch(FILES[2], cache):
        if gc in ("Cc", "Mn", "Me", "Cf", "Zl", "Zp", "Cs"):
            for cp in range(lo, hi + 1):
                if cp not in keep:
                    width[cp] = 0
    for lo, hi in ((0x1160, 0x11FF), (0xD7B0, 0xD7FF)):
        width[lo:hi + 1] = b"\x00" * (hi - lo + 1)

    for lo, hi, gcb in fetch(FILES[3], cache):
        if gcb in JOINING:
            simple[lo:hi + 1] = b"\x00" * (hi - lo + 1)
    simple[0xD800:0xE000] = b"\x00" * 0x800
    simple[EMOJI_PLANE:MAX] = b"\x00" * (MAX - EMOJI_PLANE)
    assert pictographic > 0, "no Extended_Pictographic outside the BMP: emoji-data.txt not read"

    # Stage 2 blocks: a nibble per code point, two code points per byte.
    blocks, index = {}, []
    for start in range(0, MAX, BLOCK):
        packed = bytearray(BLOCK // 2)
        for i in range(BLOCK):
            packed[i >> 1] |= (width[start + i] | simple[start + i] << 2) << ((i & 1) * 4)
        index.append(blocks.setdefault(bytes(packed), len(blocks)))
    assert len(blocks) <= 256, len(blocks)
    stage2 = b"".join(sorted(blocks, key=blocks.get))

    def rows(data, per=32):
        return "\n".join("        " + ", ".join(f"0x{b:02X}" for b in data[i:i + per]) + ","
                         for i in range(0, len(data), per))

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"""// <auto-generated>
// Generated by tools/gen-width/gen.py from Unicode {VERSION} UCD. Do not edit.
// </auto-generated>
namespace Tuinet;

internal static partial class UnicodeWidth
{{
    public const string UnicodeVersion = "{VERSION}";

    // Stage 1: one byte per 256-code-point block, indexing a stage-2 block.
    private static ReadOnlySpan<byte> Stage1 => new byte[]
    {{
{rows(bytes(index))}
    }};

    // Stage 2: {len(blocks)} unique blocks, 4 bits per code point (128 bytes per block): width in bits 0-1,
    // "never joins" (see Graphemes.Simple) in bit 2.
    private static ReadOnlySpan<byte> Stage2 => new byte[]
    {{
{rows(stage2)}
    }};
}}
""")
    print(f"wrote {OUT}: {len(blocks)} blocks, {len(index) + len(stage2)} bytes")

if __name__ == "__main__":
    main()
