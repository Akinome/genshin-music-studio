# -*- coding: utf-8 -*-
"""Compare two score libraries configured by environment variables."""
import glob
import os
import statistics
import sys

import mido

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIRS = [
    os.environ.get("GENSHIN_PLAYABLE_DIR", os.path.join(BASE_DIR, "示例谱库", "成熟的原琴")),
    os.environ.get("GENSHIN_BACKUP_DIR", os.path.join(BASE_DIR, "示例谱库", "不可播备份")),
]
RANGE_LO, RANGE_HI = 48, 83
MIN_GAP_MS = 60


def analyze(path):
    mid = mido.MidiFile(path)
    merged = mido.merge_tracks(mid.tracks)
    tempo = 500000
    active = {}
    sim = 0
    max_sim = 0
    channels = set()
    min_note = 127
    max_note = 0
    note_count = 0
    total_sec = 0.0
    last_sec = None
    min_gap_sec = None
    overlapping = 0
    for msg in merged:
        if msg.is_meta:
            if msg.type == "set_tempo":
                tempo = msg.tempo
            continue
        sec = mido.tick2second(msg.time, mid.ticks_per_beat, tempo) if mid.ticks_per_beat else 0
        if msg.type == "note_on" and msg.velocity > 0:
            note_count += 1
            channels.add(msg.channel)
            min_note = min(min_note, msg.note)
            max_note = max(max_note, msg.note)
            if active.get(msg.note, 0) > 0:
                overlapping += 1
            active[msg.note] = active.get(msg.note, 0) + 1
            sim += 1
            max_sim = max(max_sim, sim)
            if last_sec is not None:
                gap = sec - last_sec
                if min_gap_sec is None or gap < min_gap_sec:
                    min_gap_sec = gap
            last_sec = sec
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            if active.get(msg.note, 0) > 0:
                active[msg.note] -= 1
                sim -= 1
                if active[msg.note] == 0:
                    del active[msg.note]
    return {
        "path": path,
        "midi_type": mid.type,
        "tracks": len(mid.tracks),
        "notes": note_count,
        "max_sim": max_sim,
        "min_note": min_note if note_count else None,
        "max_note": max_note if note_count else None,
        "channels": len(channels),
        "min_gap_ms": min_gap_sec * 1000 if min_gap_sec is not None else None,
        "overlapping": overlapping,
    }


def main():
    for d in DIRS:
        rows = []
        for p in glob.glob(os.path.join(d, "*.mid")):
            try:
                rows.append(analyze(p))
            except Exception as e:
                rows.append({"path": p, "error": str(e)})
        good = [r for r in rows if "error" not in r]
        print("=" * 70)
        print("DIR:", d)
        print("files:", len(rows), "ok:", len(good))
        if not good:
            continue
        for key, label in [("notes", "note count"), ("max_sim", "max simultaneous"), ("tracks", "tracks")]:
            vals = [r[key] for r in good if r[key] is not None]
            if vals:
                print("%-18s avg=%.1f min=%d max=%d" % (label, statistics.mean(vals), min(vals), max(vals)))
        mins = [r["min_note"] for r in good if r["min_note"] is not None]
        maxs = [r["max_note"] for r in good if r["max_note"] is not None]
        if mins and maxs:
            print("%-18s min=%d max=%d (game range %d-%d)" % ("pitch", min(mins), max(maxs), RANGE_LO, RANGE_HI))
        print("in-game range violations:", sum(1 for r in good if r["min_note"] < RANGE_LO or r["max_note"] > RANGE_HI))
        print("with chords (max_sim>1):", sum(1 for r in good if r["max_sim"] > 1))
        print("with <60ms gaps:", sum(1 for r in good if r["min_gap_ms"] is not None and r["min_gap_ms"] < MIN_GAP_MS))
        print("with same-note overlap:", sum(1 for r in good if r["overlapping"] > 0))
        print("midi types:", sorted(set(r["midi_type"] for r in good)))
        print("tracks used:", sorted(set(r["tracks"] for r in good))[:20])


if __name__ == "__main__":
    main()
