#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build a melody-only, duration-preserving Genshin MIDI library."""
import csv
import glob
import json
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND_DIR = os.path.join(REPO_ROOT, "backend")
for extra_dir in (TOOLS_DIR, BACKEND_DIR):
    if extra_dir not in sys.path:
        sys.path.insert(0, extra_dir)

import melody_extract as me
import midi_to_genshin as mtg
from process_library import BACKUP_DIR, PLAYABLE_DIR, validate_outputs

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = REPO_ROOT
STAGE_DIR = os.path.join(BASE_DIR, "优化完成_纯旋律")
SOURCES = [
    ("成熟原琴", PLAYABLE_DIR),
    ("不可播备份", BACKUP_DIR),
]
FIELDS = [
    "source_group", "source", "track_count", "selected_track", "method",
    "notes", "dropped_range", "dropped_dense", "actual", "scale", "low", "high",
    "source_seconds", "seconds", "duration_ratio", "output", "error",
]


def melody_config():
    cfg = mtg.load_config(os.path.join(BASE_DIR, "config.json"))
    cfg.update({
        "range": {"low": 48, "high": 83},
        "transpose_semitones": 0,
        "chord_mode": "top",
        "arpeggio_gap_ms": 60,
        "press_ms": 120,
        "tempo_scale": 1.0,
        "min_gap_ms": 60,
        "chord_window_ms": 30,
        "auto_slow": False,
        "quantize_ms": 0,
        "strict_gap": True,
        "preserve_duration": True,
        "collapse_window_ms": 10,
    })
    return cfg


def write_csv(path, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=FIELDS)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def main():
    cfg = melody_config()
    os.makedirs(STAGE_DIR, exist_ok=True)
    all_rows = []

    for label, source_dir in SOURCES:
        out_subdir = os.path.join(STAGE_DIR, label)
        os.makedirs(out_subdir, exist_ok=True)
        rows = []
        for path in sorted(glob.glob(os.path.join(source_dir, "*.mid"))):
            try:
                row = me.convert_file(path, cfg, out_subdir, write=True)
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
        f.write("原神琴谱纯旋律版\n")
        f.write("===============\n")
        f.write("只保留主旋律，不保留和弦与伴奏。\n")
        f.write("多轨 MIDI 自动选择主旋律轨；单轨复音 MIDI 使用 Viterbi 旋律选音。\n")
        f.write("不改变全局速度，输出时长与源 MIDI 保持一致。\n")
        f.write("音域：C3-B5（MIDI 48-83）\n")
        f.write("最短按键间隔：60ms\n")
        f.write("自动放慢：关闭\n")

    ok_rows = [r for r in all_rows if "error" not in r]
    if ok_rows:
        print("total melody notes:", sum(r["notes"] for r in ok_rows))
        print("total kept notes:", sum(r["actual"] for r in ok_rows))
        print("total simplified:", sum(r["dropped_dense"] for r in ok_rows))
        print("duration ratio min/max: %.6f / %.6f" % (
            min(r["duration_ratio"] for r in ok_rows), max(r["duration_ratio"] for r in ok_rows)))
    print("output:", STAGE_DIR)
    validate_outputs(STAGE_DIR, cfg)


if __name__ == "__main__":
    main()
