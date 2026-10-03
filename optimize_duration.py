#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build a duration-preserving, playable Genshin MIDI library."""
import csv
import glob
import json
import os
import sys

import duration_preserving as dp
import midi_to_genshin as mtg
from process_library import BACKUP_DIR, PLAYABLE_DIR, validate_outputs

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
STAGE_DIR = os.path.join(BASE_DIR, "优化完成_时长匹配")
SOURCES = [
    ("成熟原琴", PLAYABLE_DIR),
    ("不可播备份", BACKUP_DIR),
]
FIELDS = [
    "source_group", "source", "notes", "dropped_range", "dropped_dense", "actual",
    "scale", "low", "high", "source_seconds", "seconds", "duration_ratio", "output", "error",
]


def strict_duration_config():
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
        "auto_slow": False,
        "quantize_ms": 0,
        "strict_gap": True,
        "preserve_duration": True,
    })
    return cfg


def write_csv(path, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=FIELDS)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def main():
    cfg = strict_duration_config()
    os.makedirs(STAGE_DIR, exist_ok=True)
    all_rows = []

    for label, source_dir in SOURCES:
        out_subdir = os.path.join(STAGE_DIR, label)
        os.makedirs(out_subdir, exist_ok=True)
        rows = []
        for path in sorted(glob.glob(os.path.join(source_dir, "*.mid"))):
            try:
                row = dp.convert_file(path, cfg, out_subdir, write=True)
                row["source_group"] = label
                rows.append(row)
            except Exception as exc:
                rows.append({
                    "source_group": label,
                    "source": os.path.basename(path),
                    "error": "%s: %s" % (type(exc).__name__, exc),
                })
        write_csv(os.path.join(STAGE_DIR, "%s_转换报告.csv" % label), rows)
        all_rows.extend(rows)
        ok = [r for r in rows if "error" not in r]
        print("%s: %d/%d" % (label, len(ok), len(rows)))

    write_csv(os.path.join(STAGE_DIR, "全部转换报告.csv"), all_rows)

    with open(os.path.join(STAGE_DIR, "优化参数.json"), "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)

    with open(os.path.join(STAGE_DIR, "优化说明.txt"), "w", encoding="utf-8") as f:
        f.write("原神琴谱时长匹配优化版\n")
        f.write("========================\n")
        f.write("不改变全局速度，输出时长与源 MIDI 保持一致。\n")
        f.write("同一时间或过密冲突的音符会按优先级简化，避免全局放慢。\n")
        f.write("音域：C3-B5（MIDI 48-83）\n")
        f.write("最短按键间隔：60ms\n")
        f.write("琶音间隔：60ms\n")
        f.write("自动放慢：关闭\n")
        f.write("时间量化：关闭\n")

    ok_rows = [r for r in all_rows if "error" not in r]
    if ok_rows:
        print("total source notes:", sum(r["notes"] for r in ok_rows))
        print("total kept notes:", sum(r["actual"] for r in ok_rows))
        print("total simplified:", sum(r["dropped_dense"] for r in ok_rows))
        print("duration ratio min/max: %.6f / %.6f" % (
            min(r["duration_ratio"] for r in ok_rows), max(r["duration_ratio"] for r in ok_rows)))
    print("output:", STAGE_DIR)
    validate_outputs(STAGE_DIR, cfg)


if __name__ == "__main__":
    main()
