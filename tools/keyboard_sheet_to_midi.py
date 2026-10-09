#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Convert a 21-key computer-keyboard sheet (Q/A/Z rows, / beats, () chords) to MIDI."""
import argparse
import os
import sys

import mido

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

KEY_PITCHES = {
    "Q": 72, "W": 74, "E": 76, "R": 77, "T": 79, "Y": 81, "U": 83,
    "A": 60, "S": 62, "D": 64, "F": 65, "G": 67, "H": 69, "J": 71,
    "Z": 48, "X": 50, "C": 52, "V": 53, "B": 55, "N": 57, "M": 59,
}


def parse_sheet(text, bpm):
    """Each line is one bar: beat groups split by '/', each token = one eighth slot."""
    slot_seconds = 60.0 / bpm / 3.0
    events = []
    bar = 0
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("电脑谱") or line.upper().startswith("BPM"):
            continue
        groups = [g.strip() for g in line.split("/")]
        if groups and groups[-1].isdigit():
            groups = groups[:-1]
        for group_index, group in enumerate(groups):
            if not group:
                continue
            slot = 0
            for token in group.split():
                chorded = token.startswith("(") and token.endswith(")")
                keys = token[1:-1] if chorded else token
                start = (bar * 12 + group_index * 3 + slot) * slot_seconds
                velocity = 90 if chorded else 80
                for key in keys:
                    pitch = KEY_PITCHES.get(key.upper())
                    if pitch is not None:
                        events.append((start, pitch, velocity))
                slot += 1
        bar += 1
    return events, bar, slot_seconds


def build_midi(events, slot_seconds, bpm):
    events.sort(key=lambda e: (e[0], e[1]))
    ordered = sorted(events, key=lambda e: (e[0], e[1]))
    next_onset = {}
    for start, pitch, _ in ordered:
        next_onset[pitch] = start
    fixed = []
    for start, pitch, velocity in events:
        following = next_onset.get(pitch)
        limit = following if following is not None and following > start + 0.001 else start + 3 * slot_seconds
        duration = max(slot_seconds, min(3 * slot_seconds, limit - start))
        fixed.append((start, pitch, velocity, duration))
    return fixed


def write_midi(notes, bpm, path):
    mid = mido.MidiFile(type=0, ticks_per_beat=480)
    track = mido.MidiTrack()
    micro = int(60_000_000 / bpm)
    track.append(mido.MetaMessage("set_tempo", tempo=micro, time=0))
    mid.tracks.append(track)
    events = []
    for start, pitch, velocity, duration in notes:
        events.append((start, mido.Message("note_on", note=pitch, velocity=velocity, time=0)))
        events.append((start + duration, mido.Message("note_off", note=pitch, velocity=0, time=0)))
    events.sort(key=lambda item: (item[0], 1 if item[1].type == "note_off" else 0))
    previous = 0.0
    for seconds, msg in events:
        msg.time = int(round((seconds - previous) * 480 * 1e6 / micro))
        track.append(msg)
        previous = seconds
    track.append(mido.MetaMessage("end_of_track", time=0))
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    mid.save(path)


def main():
    parser = argparse.ArgumentParser(description="21 键电脑谱转 MIDI")
    parser.add_argument("input", help="键盘谱文本文件")
    parser.add_argument("-o", "--output", required=True, help="输出 MIDI 路径")
    parser.add_argument("--bpm", type=float, default=181.0)
    args = parser.parse_args()

    with open(args.input, "r", encoding="utf-8") as f:
        text = f.read()
    events, bars, slot = parse_sheet(text, args.bpm)
    notes = build_midi(events, slot, args.bpm)
    write_midi(notes, args.bpm, args.output)
    duration = max((s + d for s, _, _, d in notes), default=0)
    print("bars: %d | notes: %d | duration: %.1fs -> %s" % (bars, len(notes), duration, args.output))


if __name__ == "__main__":
    main()
