#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Print structural diagnostics for MIDI files."""
import os
import statistics
import sys

import mido

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass


def analyze(path):
    mid = mido.MidiFile(path)
    merged = mido.merge_tracks(mid.tracks)
    tempo = 500000
    tempos = []
    active = {}
    poly = 0
    max_poly = 0
    poly_samples = []
    notes = []
    onset_ticks = []
    channels = set()
    programs = []
    for msg in merged:
        if msg.is_meta:
            if msg.type == "set_tempo":
                tempo = msg.tempo
                tempos.append(msg.tempo)
            continue
        if msg.type == "program_change":
            programs.append((msg.channel, msg.program))
        if msg.type == "note_on" and msg.velocity > 0:
            active[msg.note] = active.get(msg.note, 0) + 1
            poly += 1
            max_poly = max(max_poly, poly)
            poly_samples.append(poly)
            onset_ticks.append(msg.time)
            channels.add(msg.channel)
            notes.append(msg.note)
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            if active.get(msg.note, 0) > 0:
                active[msg.note] -= 1
                poly -= 1
    # Re-read with absolute ticks for quantization and gaps.
    abs_ticks = []
    abs_tick = 0
    for msg in merged:
        abs_tick += msg.time
        if not msg.is_meta and msg.type == "note_on" and msg.velocity > 0:
            abs_ticks.append(abs_tick)
    grid = max(1, mid.ticks_per_beat // 4)
    on_grid = sum(1 for tick in abs_ticks if tick % grid == 0)
    gaps = [b - a for a, b in zip(abs_ticks, abs_ticks[1:]) if b >= a]
    track_notes = []
    for track in mid.tracks:
        track_notes.append(sum(1 for msg in track if msg.type == "note_on" and msg.velocity > 0))
    return {
        "file": os.path.basename(path),
        "type": mid.type,
        "tracks": len(mid.tracks),
        "note_count": len(notes),
        "duration_sec": mid.length,
        "pitch_min": min(notes) if notes else None,
        "pitch_max": max(notes) if notes else None,
        "unique_pitches": len(set(notes)),
        "max_polyphony": max_poly,
        "avg_polyphony": statistics.mean(poly_samples) if poly_samples else 0,
        "grid_ratio": on_grid / len(abs_ticks) if abs_ticks else 0,
        "median_gap_ticks": statistics.median(gaps) if gaps else 0,
        "tempo_changes": len(tempos),
        "channels": sorted(channels),
        "programs": sorted(set(programs)),
        "per_track_notes": track_notes,
    }


def main():
    for path in sys.argv[1:]:
        row = analyze(path)
        print("=" * 72)
        for key, value in row.items():
            print(f"{key:18}: {value}")


if __name__ == "__main__":
    main()
