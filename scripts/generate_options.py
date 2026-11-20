#!/usr/bin/env python3
"""Generate the GUI catalogue and fail when an upstream option is uncovered."""

import argparse
import json
import re
from pathlib import Path


STRING = r'"(?:[^"\\]|\\.)*"'


def strings(source):
    return [json.loads(value) for value in re.findall(STRING, source)]


def member(source, name):
    match = re.search(rf"\.{name}\s*=\s*({STRING})", source)
    return json.loads(match[1]) if match else None


def parse_options(source):
    table = source.split("static const struct sc_option options[] = {", 1)[1].split("\n};", 1)[0]
    options = {}
    for record in re.findall(r"^    \{\n(.*?)^    \},", table, re.M | re.S):
        name = member(record, "longopt")
        short = re.search(r"\.shortopt = '(.)'", record)
        alias = f"-{short[1]}" if short else None
        value = member(record, "argdesc")
        body = record.split(".text =", 1)[-1]
        if "#ifndef NDEBUG" in body:
            debug_start, release = body.split("#ifndef NDEBUG", 1)
            body = debug_start + release.split("#else", 1)[1].split("#endif", 1)[0]
        body = body.replace("STR(DEFAULT_LOCAL_PORT_RANGE_FIRST)", '"27183"')
        body = body.replace("STR(DEFAULT_LOCAL_PORT_RANGE_LAST)", '"27199"')
        key = name or alias
        if key is None or key in options:
            raise ValueError(f"Invalid or duplicate source record {record!r}")
        options[key] = {
            "key": key,
            "flag": f"--{name}" if name else alias,
            "alias": alias if name else None,
            "argument": value,
            "optional": ".optional_arg = true" in record,
            "description": "".join(strings(body)),
            "environment": False,
            "platform": "Linux" if key.startswith("v4l2-") else None,
        }

    env_table = source.split("static const struct sc_envvar envvars[] = {", 1)[1].split("\n};", 1)[0]
    for record in re.findall(r"^    \{\n(.*?)^    \},", env_table, re.M | re.S):
        name = member(record, "name")
        options[name] = {
            "key": name,
            "flag": name,
            "argument": "value",
            "description": "".join(strings(record.split(".text =", 1)[1])),
            "environment": True,
        }
    return options


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    source = (args.source / "app/src/cli.c").read_text(encoding="utf-8")
    metadata = Path(__file__).resolve().parents[1] / "data/options.json"
    groups = json.loads(metadata.read_text(encoding="utf-8"))
    options = parse_options(source)
    catalogue = []
    for group, entries in groups.items():
        for entry in entries:
            key, title, *extra = entry
            if key not in options:
                raise ValueError(f"Unknown or duplicate GUI option {key}")
            option = options.pop(key)
            option.update(group=group, title=title, choices=extra[0] if extra else [],
                          placeholder=extra[1] if len(extra) > 1 else None)
            catalogue.append(option)

    if options:
        raise ValueError(f"Missing GUI options {', '.join(options)}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(catalogue, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    cli_count = sum(not item["environment"] for item in catalogue)
    alias_count = sum(bool(item.get("alias")) for item in catalogue)
    print(f"Coverage: {cli_count} CLI entries, {alias_count} aliases, "
          f"{len(catalogue) - cli_count} environment variables")


if __name__ == "__main__":
    main()
