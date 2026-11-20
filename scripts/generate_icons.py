#!/usr/bin/env python3
"""Convert the Lucide icons used by the GUI into a WPF geometry dictionary."""

import argparse
import re
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path


VERSION = "1.77.0"
REPOSITORY = f"https://raw.githubusercontent.com/lucide-icons/lucide/{VERSION}"
SOURCE = REPOSITORY + "/icons/{name}.svg"
ICONS = """
activity app-window arrow-left bell cable camera carets-to-axis-dashed-horizontal
carets-to-axis-dashed-vertical chevron-down chevrons-up circle-check circle-dot clipboard-paste copy
cpu crop eye-off film flashlight flashlight-off folder-open gamepad-2 gauge house info keyboard
key-round layers layout-grid link maximize menu monitor monitor-smartphone monitor-up moon
mouse-pointer-click palette pause play power refresh-cw rotate-ccw rotate-cw scan scissors
scroll-text search sliders-horizontal smartphone sparkles square square-terminal sun
text-cursor-input trash unlink usb video volume-1 volume-2 wifi x zap
""".split()

NUMBER = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
ARITY = {"m": 2, "l": 2, "h": 1, "v": 1, "c": 6, "s": 4, "q": 4, "t": 2, "a": 7, "z": 0}


def fmt(value):
    return f"{float(value):.4f}".rstrip("0").rstrip(".") or "0"


def normalise(data):
    """Rewrite SVG path data with explicit separators, including packed arc flags.

    Each SVG element becomes one figure of a shared geometry, so a leading relative
    "m" (absolute in its own element) is made absolute and its implicit line
    segments are spelled out as relative ones.
    """
    segments, pos, command = [], 0, None
    while pos < len(data):
        char = data[pos]
        if char.isspace() or char == ",":
            pos += 1
        elif char.isalpha():
            command, pos = char, pos + 1
            if command in "zZ":
                segments.append(("Z", []))
        else:
            if command is None or command in "zZ":
                raise ValueError(f"Unexpected number at {pos} in {data!r}")
            values = []
            for index in range(ARITY[command.lower()]):
                while data[pos].isspace() or data[pos] == ",":
                    pos += 1
                if command in "aA" and index in (3, 4):
                    values.append(data[pos])
                    pos += 1
                    continue
                match = NUMBER.match(data, pos)
                if not match:
                    raise ValueError(f"Expected a number at {pos} in {data!r}")
                values.append(fmt(match[0]))
                pos = match.end()
            segments.append((command, values))
            # Further coordinate pairs after a move are implicit line segments.
            command = {"M": "L", "m": "l"}.get(command, command)
    if segments and segments[0][0] == "m":
        segments[0] = ("M", segments[0][1])
    return " ".join(" ".join([name, *values]) for name, values in segments)


def rounded_rect(x, y, w, h, rx, ry):
    if rx <= 0 or ry <= 0:
        return f"M {fmt(x)} {fmt(y)} H {fmt(x + w)} V {fmt(y + h)} H {fmt(x)} Z"
    rx, ry = min(rx, w / 2), min(ry, h / 2)
    arc = f"A {fmt(rx)} {fmt(ry)} 0 0 1"
    return (f"M {fmt(x + rx)} {fmt(y)} H {fmt(x + w - rx)} {arc} {fmt(x + w)} {fmt(y + ry)} "
            f"V {fmt(y + h - ry)} {arc} {fmt(x + w - rx)} {fmt(y + h)} "
            f"H {fmt(x + rx)} {arc} {fmt(x)} {fmt(y + h - ry)} "
            f"V {fmt(y + ry)} {arc} {fmt(x + rx)} {fmt(y)} Z")


def shape(element):
    tag = element.tag.rsplit("}", 1)[-1]
    get = lambda name, default=0.0: float(element.get(name, default))
    if tag == "path":
        return normalise(element.get("d"))
    if tag in ("circle", "ellipse"):
        cx, cy = get("cx"), get("cy")
        rx = get("r") if tag == "circle" else get("rx")
        ry = get("r") if tag == "circle" else get("ry")
        arc = f"A {fmt(rx)} {fmt(ry)} 0 1 0"
        return f"M {fmt(cx - rx)} {fmt(cy)} {arc} {fmt(cx + rx)} {fmt(cy)} {arc} {fmt(cx - rx)} {fmt(cy)} Z"
    if tag == "rect":
        rx = element.get("rx") or element.get("ry") or 0
        ry = element.get("ry") or element.get("rx") or 0
        return rounded_rect(get("x"), get("y"), get("width"), get("height"), float(rx), float(ry))
    if tag == "line":
        return f"M {fmt(get('x1'))} {fmt(get('y1'))} L {fmt(get('x2'))} {fmt(get('y2'))}"
    if tag in ("polyline", "polygon"):
        points = [fmt(value) for value in NUMBER.findall(element.get("points"))]
        pairs = [f"{points[i]} {points[i + 1]}" for i in range(0, len(points), 2)]
        return "M " + " L ".join(pairs) + (" Z" if tag == "polygon" else "")
    raise ValueError(f"Unsupported SVG element <{tag}>")


def key(name):
    return "Icon." + "".join(part.capitalize() for part in name.split("-"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, nargs="?",
                        default=Path(__file__).resolve().parents[1] / "app/Theme/Icons.xaml")
    args = parser.parse_args()

    lines = [
        "<!-- Generated by scripts/generate_icons.py from Lucide " + VERSION + " (ISC License). -->",
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
    ]
    for name in sorted(ICONS):
        with urllib.request.urlopen(SOURCE.format(name=name), timeout=30) as response:
            root = ET.fromstring(response.read())
        if root.get("viewBox") != "0 0 24 24":
            raise ValueError(f"{name} does not use the 24 px Lucide grid")
        data = " ".join(shape(child) for child in root)
        lines.append(f'    <Geometry x:Key="{key(name)}">{data}</Geometry>')
    lines.append("</ResourceDictionary>")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    with urllib.request.urlopen(REPOSITORY + "/LICENSE", timeout=30) as response:
        args.output.with_suffix(".LICENSE.txt").write_bytes(response.read())
    print(f"Wrote {len(ICONS)} icons to {args.output}")


if __name__ == "__main__":
    main()
