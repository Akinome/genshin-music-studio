#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Create the optional zero-drop version (song duration will change)."""
import csv
import glob
import json
import os
import sys

import midi_to_genshin as mtg
from process_library import BACKUP_DIR, PLAYABLE_DIR, convert_one, validate_outputs

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(BASE_DIR, "零丢音_时长会变")
SOURCES = [
    ("成熟原琴", PLAYABLE_DIR),
    ("不可播备份", BACKUP_DIR),
]
REPORT_FIELDS = [
    "source_group", "source", "notes", "dropped_range", "dropped_dense", "actual",
    "scale", "low", "high", "seconds", "output", "error",
]


def strict_config():
    cfg = mtg.load_config(os.path.join(BASE_DIR, "config.json"))
    cfg.update({
        "range": {"low": 48, "high": 83},
        "transpose_semitones": 0,
        "chord_mode": "arpeggio",
        "arpeggio_gap_ms": 60,
        "press_ms": 120,
        "tempo_scale": 1.0,
        "min_gap_ms": 60,
        "chord_window_ms": 30,
        "auto_slow": True,
        "quantize_ms": 0,
    })
    return cfg


def write_csv(path, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=REPORT_FIELDS)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def main():
    cfg = strict_config()
    os.makedirs(OUT_DIR, exist_ok=True)
    all_rows = []

    for label, source_dir in SOURCES:
        out_subdir = os.path.join(OUT_DIR, label)
        os.makedirs(out_subdir, exist_ok=True)
        rows = []
        for path in sorted(glob.glob(os.path.join(source_dir, "*.mid"))):
            try:
                row = convert_one(path, cfg, out_subdir, True)
                row["source_group"] = label
                rows.append(row)
            except Exception as exc:
                rows.append({
                    "source_group": label,
                    "source": os.path.basename(path),
                    "error": "%s: %s" % (type(exc).__name__, exc),
                })
        write_csv(os.path.join(OUT_DIR, "%s_转换报告.csv" % label), rows)
        all_rows.extend(rows)
        print("%s: %d/%d" % (label, len([r for r in rows if "error" not in r]), len(rows)))

    write_csv(os.path.join(OUT_DIR, "全部转换报告.csv"), all_rows)

    with open(os.path.join(OUT_DIR, "优化参数.json"), "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)

    with open(os.path.join(OUT_DIR, "优化说明.txt"), "w", encoding="utf-8") as f:
        f.write("原神琴谱零丢音版（时长会变）\n")
        f.write("====================\n")
        f.write("该版本优先保留所有音符，会通过全局放慢避免丢音，因此输出时长会变长。\n")
        f.write("strict mode: 单音序列、C3-B5、最短按键间隔 >= 60ms\n")
        f.write("chord_mode: arpeggio\n")
        f.write("arpeggio_gap_ms: 60\n")
        f.write("min_gap_ms: 60\n")
        f.write("auto_slow: true\n")
        f.write("quantize_ms: 0\n\n")
        f.write("子目录：\n")
        f.write("- 成熟原琴/：来自 GENSHIN_PLAYABLE_DIR\n")
        f.write("- 不可播备份/：来自 GENSHIN_BACKUP_DIR\n")

    print("output:", OUT_DIR)
    validate_outputs(OUT_DIR, cfg)


if __name__ == "__main__":
    main()
