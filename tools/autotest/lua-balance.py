#!/usr/bin/env python3
"""Block-balance check for autotest Lua scenarios — catches the unclosed `function` / `if` / `for`
that costs a whole run to discover, since the engine only reports it as a Fatal Lua Error at load.

Not a Lua parser. It strips comments and string literals, then counts block openers against `end`
(and `repeat` against `until`). That is enough for the failure this exists to catch: on 2026-08-15
`wip-transport-delivers` shipped missing the single `end` closing its WorldLoaded function, so the
scenario could not compile and a run spent on it measured nothing at all.

Usage: tools/autotest/lua-balance.py <file.lua> [...]   # exit 1 if any file is unbalanced
"""

import re
import sys

OPENERS = ("function", "if", "for", "while", "do")


def strip_noise(src):
    """Blank out comments and string literals, keeping newlines so line numbers survive.

    Must be ONE left-to-right scan rather than a sequence of regex passes. Stripping comments
    before strings corrupts any string containing `--` (of which these scenarios have plenty):
    the comment pass eats the rest of the line, orphaning the opening quote, and the string pass
    then swallows across lines and hides real code. That produced twelve false positives.
    """
    out = []
    i, n = 0, len(src)
    while i < n:
        ch = src[i]
        two = src[i:i + 2]

        if two == "--":
            long_open = re.match(r"--\[(=*)\[", src[i:])
            if long_open:
                close = "]" + long_open.group(1) + "]"
                end = src.find(close, i)
                chunk = src[i:] if end < 0 else src[i:end + len(close)]
            else:
                end = src.find("\n", i)
                chunk = src[i:] if end < 0 else src[i:end]
            out.append(re.sub(r"[^\n]", " ", chunk))
            i += len(chunk)
            continue

        long_open = re.match(r"\[(=*)\[", src[i:])
        if long_open:
            close = "]" + long_open.group(1) + "]"
            end = src.find(close, i)
            chunk = src[i:] if end < 0 else src[i:end + len(close)]
            out.append(re.sub(r"[^\n]", " ", chunk))
            i += len(chunk)
            continue

        if ch in "\"'":
            j = i + 1
            while j < n and src[j] != ch:
                j += 2 if src[j] == "\\" else 1
            chunk = src[i:min(j + 1, n)]
            out.append(re.sub(r"[^\n]", " ", chunk))
            i += len(chunk)
            continue

        out.append(ch)
        i += 1

    return "".join(out)


def check(path):
    with open(path, encoding="utf-8", errors="replace") as handle:
        src = strip_noise(handle.read())

    depth = 0
    repeats = 0
    for line_no, line in enumerate(src.split("\n"), start=1):
        words = re.findall(r"\b\w+\b", line)
        for i, word in enumerate(words):
            if word == "repeat":
                repeats += 1
            elif word == "until":
                repeats -= 1
            elif word == "end":
                depth -= 1
            elif word in OPENERS:
                # `for`/`while` open via their own `do`, so counting both double-counts them.
                if word == "do" and any(w in ("for", "while") for w in words[:i]):
                    continue
                depth += 1

        if depth < 0:
            return f"{path}: unmatched `end` at line {line_no}"

    if depth != 0:
        return f"{path}: {depth} unclosed block(s) — a missing `end` (file ends at depth {depth})"
    if repeats != 0:
        return f"{path}: {repeats} unclosed `repeat` block(s)"
    return None


def main():
    files = sys.argv[1:]

    # Assert it measured something before it reports finding nothing — otherwise a moved directory
    # or a glob that matches no files turns this into a check that passes by scanning zero bytes.
    if not files:
        print("lua-balance: no files given — scanned nothing, which is not a pass", file=sys.stderr)
        return 2

    problems = [p for p in (check(f) for f in files) if p]
    for problem in problems:
        print(problem, file=sys.stderr)

    if not problems:
        print(f"lua-balance: {len(files)} file(s) balanced")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
