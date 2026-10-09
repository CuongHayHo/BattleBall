#!/usr/bin/env python3
"""Export static game tables from SourceData/tables/nro1.sql (MySQL dump) to versioned JSON.

Output: SourceData/tables/json/<table>.json with
  {"table", "version", "source", "columns", "json_columns", "row_count", "rows": [ {col: value, ...}, ... ]}

Only an allowlist of static (non-user) tables is exported; account, player, payment, clan, chat tables are
deliberately skipped. Columns whose text is JSON are parsed into real JSON; the NRO "skills" column
(JSON array of JSON-in-strings with broken quoting) is repaired and validated.

Usage: python export_sql_to_json.py [--version N]
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TABLES_DIR = os.path.normpath(os.path.join(HERE, "..", "tables"))
SQL_PATH = os.path.join(TABLES_DIR, "nro1.sql")
OUT_DIR = os.path.join(TABLES_DIR, "json")

ALLOWLIST = [
    "part", "array_head_2_frames", "head_avatar", "img_by_name", "skill_template",
    "item_template", "item_option_template", "map_template", "mob_template", "npc_template",
    "bg_item_template", "intrinsic", "flag_bag", "radar", "shop", "tab_shop", "item_shop",
    "item_shop_option",
]

ESCAPES = {"n": "\n", "r": "\r", "t": "\t", "0": "\0", "b": "\b", "Z": "\x1a",
           "\\": "\\", "'": "'", '"': '"'}


def parse_values(text, pos):
    """Parse '(..),(..),...;' starting at pos (just after VALUES). Return (rows, new_pos)."""
    rows = []
    n = len(text)
    while True:
        while pos < n and text[pos] in " \r\n\t,":
            pos += 1
        if pos >= n or text[pos] == ";":
            return rows, pos + 1
        if text[pos] != "(":
            raise ValueError("expected '(' at %d: %r" % (pos, text[pos:pos + 40]))
        pos += 1
        row = []
        while True:
            while text[pos] in " \r\n\t":
                pos += 1
            ch = text[pos]
            if ch == "'":
                pos += 1
                buf = []
                while True:
                    c = text[pos]
                    if c == "\\":
                        buf.append(ESCAPES.get(text[pos + 1], text[pos + 1]))
                        pos += 2
                    elif c == "'":
                        if text[pos + 1] == "'":
                            buf.append("'")
                            pos += 2
                        else:
                            pos += 1
                            break
                    else:
                        buf.append(c)
                        pos += 1
                row.append("".join(buf))
            else:
                m = re.compile(r"[^,)\s]+").match(text, pos)
                tok = m.group(0)
                pos = m.end()
                if tok.upper() == "NULL":
                    row.append(None)
                else:
                    try:
                        row.append(int(tok))
                    except ValueError:
                        try:
                            row.append(float(tok))
                        except ValueError:
                            row.append(tok)  # e.g. hex/bit literal, kept as text
            while text[pos] in " \r\n\t":
                pos += 1
            if text[pos] == ",":
                pos += 1
                continue
            if text[pos] == ")":
                pos += 1
                break
            raise ValueError("bad row at %d: %r" % (pos, text[pos:pos + 40]))
        rows.append(row)


def _unwrap(parsed):
    """['[1,2]','[3,4]'] (array of JSON-array strings) -> [[1,2],[3,4]]."""
    if isinstance(parsed, list) and parsed and all(
            isinstance(x, str) and x[:1] == "[" and x[-1:] == "]" for x in parsed):
        try:
            return [json.loads(x) for x in parsed]
        except ValueError:
            return parsed
    return parsed


def try_json(value):
    """Return (ok, parsed). Repairs known NRO quirks in JSON-like text columns."""
    if not isinstance(value, str) or not value or value[0] not in "[{":
        return False, value
    try:
        return True, _unwrap(json.loads(value))
    except ValueError:
        pass
    fixed = value
    # array of JSON-in-strings with unescaped inner quotes: ["{...}","{...}"] / ["[...]","[...]"]
    for a, b in (('["{', "[{"), ('}","{', "},{"), ('}"]', "}]"),
                 ('["[', "[["), (']","[', "],["), (']"]', "]]")):
        fixed = fixed.replace(a, b)
    # trailing comma before a closing bracket: [1,2,]
    fixed = re.sub(r",\s*\]", "]", fixed)
    try:
        return True, _unwrap(json.loads(fixed))
    except ValueError:
        return False, value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", type=int, default=1)
    args = ap.parse_args()

    with open(SQL_PATH, "r", encoding="utf-8") as f:
        text = f.read()

    insert_re = re.compile(r"INSERT INTO `([^`]+)` \(([^)]*)\) VALUES", re.M)
    tables = {}
    for m in insert_re.finditer(text):
        name = m.group(1)
        if name not in ALLOWLIST:
            continue
        cols = [c.strip().strip("`") for c in m.group(2).split(",")]
        rows, _ = parse_values(text, m.end())
        t = tables.setdefault(name, {"columns": cols, "rows": []})
        if t["columns"] != cols:
            raise SystemExit("column list changed inside table " + name)
        t["rows"].extend(rows)

    os.makedirs(OUT_DIR, exist_ok=True)
    report = []
    for name in ALLOWLIST:
        t = tables.get(name)
        if not t:
            report.append((name, "NOT FOUND", 0, 0, 0))
            continue
        cols = t["columns"]
        json_cols = {}
        out_rows = []
        bad = 0
        for r in t["rows"]:
            if len(r) != len(cols):
                raise SystemExit("row width mismatch in %s: %d != %d" % (name, len(r), len(cols)))
            obj = {}
            for c, v in zip(cols, r):
                ok, parsed = try_json(v)
                if ok:
                    json_cols[c] = json_cols.get(c, 0) + 1
                    obj[c] = parsed
                else:
                    if isinstance(v, str) and v and v[0] in "[{":
                        bad += 1
                    obj[c] = v
            out_rows.append(obj)
        doc = {
            "table": name,
            "version": args.version,
            "source": "Teamobi2026/SRC/sql/nro1.sql",
            "columns": cols,
            "json_columns": sorted(json_cols),
            "row_count": len(out_rows),
            "rows": out_rows,
        }
        with open(os.path.join(OUT_DIR, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
            f.write("\n")
        report.append((name, "ok" if not bad else "UNPARSED_JSON", len(out_rows), len(cols), bad))

    print("%-22s %-14s %7s %5s %s" % ("table", "status", "rows", "cols", "unparsed-json-cells"))
    for name, st, nrows, ncols, bad in report:
        print("%-22s %-14s %7d %5d %d" % (name, st, nrows, ncols, bad))


if __name__ == "__main__":
    sys.exit(main())
