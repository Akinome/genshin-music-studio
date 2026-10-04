#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Batch-convert the unplayable backup library and pair songs by title."""
import csv
import difflib
import glob
import io
import mido
import os
import re
import sys
from contextlib import redirect_stdout

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BACKEND_DIR = os.path.join(REPO_ROOT, "backend")
if BACKEND_DIR not in sys.path:
    sys.path.insert(0, BACKEND_DIR)

import midi_to_genshin as mtg

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = REPO_ROOT
PLAYABLE_DIR = os.environ.get("GENSHIN_PLAYABLE_DIR", os.path.join(BASE_DIR, "示例谱库", "成熟的原琴"))
BACKUP_DIR = os.environ.get("GENSHIN_BACKUP_DIR", os.path.join(BASE_DIR, "示例谱库", "不可播备份"))
OUT_DIR = os.path.join(BASE_DIR, "converted_backup")

STOPWORDS = [
    "原琴", "原神", "演奏", "附谱", "圆号", "诗琴", "风琴", "风物", "钢琴",
    "教学", "演示", "带谱版", "动态谱", "吉他", "合奏", "双谱", "完整版",
    "重制版", "纯音乐", "原版", "翻奏", "翻弹", "cover", "piano", "version",
    "karaoke", "伴奏", "试听", "无损版", "动态曲谱", "指法", "曲谱",
    "入门级", "豪华典藏", "男女合唱", "高音质", "主题曲", "片头曲", "片尾曲",
    "插曲", "OST", "bgm", "BGM", "BMG", "G调", "笛子", "简谱", "唱弹谱",
]


def strip_author(name):
    base = re.sub(r"\.mid$", "", name, flags=re.IGNORECASE)
    parts = re.split(r"\s*-\s*", base, maxsplit=1)
    return parts[-1] if len(parts) > 1 else base


def normalize_title(name):
    text = strip_author(name)
    text = re.sub(r"[\[\]【】「」『』()（）\"'《》,，。!！?？:：;；~～+_/]", " ", text)
    text = re.sub(r"\s+", " ", text).lower()
    for word in STOPWORDS:
        text = text.replace(word.lower(), " ")
    text = re.sub(r"\s+", " ", text).strip()
    text = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", "", text)
    return text


def title_score(a, b):
    if not a or not b:
        return 0.0
    seq = difflib.SequenceMatcher(None, a, b).ratio()
    if a in b or b in a:
        seq += 0.15
    return min(seq, 1.0)


def convert_one(path, cfg, out_dir, convert):
    notes, tpb, first_tempo, tempo_set = mtg.load_notes(path, None)
    strikes, dropped_range = mtg.normalize(notes, cfg)
    events, smallest_inter = mtg.plan(strikes, cfg, tpb, first_tempo)
    scale = mtg.compute_scale(cfg, smallest_inter)
    min_gap_ticks = max(1, round(cfg["min_gap_ms"] / 1000.0 * tpb * 1e6 / (first_tempo * scale)))
    final_presses, dropped_dense = mtg.dense_filter(events, min_gap_ticks)

    low = min((n for _, _, n, _, _ in strikes), default=0)
    high = max((n for _, _, n, _, _ in strikes), default=0)
    total_sec = (final_presses[-1][0] if final_presses else 0) * first_tempo * scale / (tpb * 1e6)
    out_name = os.path.splitext(os.path.basename(path))[0] + "_genshin.mid"

    if convert:
        out_path = os.path.join(out_dir, out_name)
        with redirect_stdout(io.StringIO()):
            mtg.write_output(final_presses, out_path, tpb, first_tempo, scale, cfg, tempo_set)
    else:
        out_name = ""

    return {
        "source": os.path.basename(path),
        "notes": len(notes),
        "dropped_range": dropped_range,
        "dropped_dense": dropped_dense,
        "actual": len(final_presses),
        "scale": round(scale, 3),
        "low": mtg.note_name(low),
        "high": mtg.note_name(high),
        "seconds": round(total_sec, 2),
        "output": out_name,
    }


def build_pairs():
    play_names = [os.path.basename(p) for p in glob.glob(os.path.join(PLAYABLE_DIR, "*.mid"))]
    backup_names = [os.path.basename(p) for p in glob.glob(os.path.join(BACKUP_DIR, "*.mid"))]
    play_norm = {name: normalize_title(name) for name in play_names}
    backup_norm = {name: normalize_title(name) for name in backup_names}
    rows = []
    for backup in backup_names:
        nb = backup_norm[backup]
        scored = sorted(
            ((title_score(nb, pn), play) for play, pn in play_norm.items() if pn),
            key=lambda x: x[0],
            reverse=True,
        )
        best_score, best_play = scored[0] if scored else (0.0, "")
        rows.append({
            "backup": backup,
            "backup_norm": nb,
            "playable": best_play,
            "playable_norm": play_norm.get(best_play, ""),
            "score": round(best_score, 3),
        })
    return sorted(rows, key=lambda r: (-r["score"], r["backup"]))


def validate_outputs(out_dir=None, cfg=None):
    out_dir = out_dir or OUT_DIR
    cfg = cfg or mtg.load_config(os.path.join(BASE_DIR, "config.json"))
    low = cfg["range"]["low"]
    high = cfg["range"]["high"]
    min_gap_ms = cfg["min_gap_ms"]
    paths = glob.glob(os.path.join(out_dir, "**", "*.mid"), recursive=True)
    bad = []
    total = 0
    max_sim = 0
    min_note = 127
    max_note = 0
    min_all_gap_ms = None
    for path in paths:
        try:
            mid = mido.MidiFile(path)
            merged = mido.merge_tracks(mid.tracks)
            tempo = 500000
            sim = 0
            prev_sec = None
            abs_sec = 0.0
            file_total = 0
            file_max_sim = 0
            file_min_gap_ms = None
            file_min_note = 127
            file_max_note = 0
            for msg in merged:
                if msg.is_meta:
                    if msg.type == "set_tempo":
                        tempo = msg.tempo
                    continue
                abs_sec += mido.tick2second(msg.time, mid.ticks_per_beat, tempo)
                sec = abs_sec
                if msg.type == "note_on" and msg.velocity > 0:
                    file_total += 1
                    sim += 1
                    file_max_sim = max(file_max_sim, sim)
                    file_min_note = min(file_min_note, msg.note)
                    file_max_note = max(file_max_note, msg.note)
                    if prev_sec is not None:
                        gap_ms = (sec - prev_sec) * 1000
                        if file_min_gap_ms is None or gap_ms < file_min_gap_ms:
                            file_min_gap_ms = gap_ms
                    prev_sec = sec
                elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
                    if sim > 0:
                        sim -= 1
            total += file_total
            max_sim = max(max_sim, file_max_sim)
            min_note = min(min_note, file_min_note)
            max_note = max(max_note, file_max_note)
            if min_all_gap_ms is None or (file_min_gap_ms is not None and file_min_gap_ms < min_all_gap_ms):
                min_all_gap_ms = file_min_gap_ms
            if file_total and (file_max_sim > 1 or file_min_note < low or file_max_note > high):
                bad.append(os.path.basename(path))
        except Exception as exc:
            bad.append("%s: %s" % (os.path.basename(path), exc))
    print("validated outputs:", len(paths))
    print("total notes:", total)
    print("max simultaneous:", max_sim)
    print("pitch range:", min_note, "-", max_note, "(game", low, "-", high, ")")
    print("min gap ms:", round(min_all_gap_ms, 3) if min_all_gap_ms is not None else "n/a")
    print("hard violations:", len(bad))
    for item in bad[:20]:
        print("  ", item)


def main():
    if "--validate" in sys.argv:
        validate_outputs()
        return
    convert = "--preview" not in sys.argv
    cfg = mtg.load_config(os.path.join(BASE_DIR, "config.json"))
    os.makedirs(OUT_DIR, exist_ok=True)

    rows = []
    for path in sorted(glob.glob(os.path.join(BACKUP_DIR, "*.mid"))):
        try:
            rows.append(convert_one(path, cfg, OUT_DIR, convert))
        except Exception as exc:
            rows.append({
                "source": os.path.basename(path),
                "error": "%s: %s" % (type(exc).__name__, exc),
            })

    report_path = os.path.join(OUT_DIR, "conversion_report.csv")
    with open(report_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "source", "notes", "dropped_range", "dropped_dense", "actual",
            "scale", "low", "high", "seconds", "output", "error",
        ])
        writer.writeheader()
        for row in rows:
            writer.writerow(row)

    pairs = build_pairs()
    pair_path = os.path.join(OUT_DIR, "pair_matches.csv")
    with open(pair_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "backup", "backup_norm", "playable", "playable_norm", "score",
        ])
        writer.writeheader()
        for row in pairs:
            writer.writerow(row)

    ok = [r for r in rows if "error" not in r]
    print("processed:", len(ok), "/", len(rows))
    print("files written:", OUT_DIR)
    print("report:", report_path)
    print("pairs:", pair_path)
    if ok:
        print("total original notes:", sum(r["notes"] for r in ok))
        print("total dropped range:", sum(r["dropped_range"] for r in ok))
        print("total dropped dense:", sum(r["dropped_dense"] for r in ok))
        print("total actual:", sum(r["actual"] for r in ok))
        print("slowest scale:", max(r["scale"] for r in ok))
    strong = [r for r in pairs if r["score"] >= 0.6]
    print("same-title candidates:", len(strong))
    for r in strong[:12]:
        print("  %.2f  %s  =>  %s" % (r["score"], r["backup"], r["playable"]))


if __name__ == "__main__":
    main()
