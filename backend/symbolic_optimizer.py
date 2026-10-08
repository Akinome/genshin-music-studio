#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Symbolic cleanup for AI-transcribed MIDI before Genshin arrangement."""
import math
import os
from collections import defaultdict

import mido

MAJOR_PROFILE = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
MINOR_PROFILE = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
MAJOR_SCALE = [0, 2, 4, 5, 7, 9, 11]
MINOR_SCALE = [0, 2, 3, 5, 7, 8, 10]


def _correlation(a, b):
    mean_a = sum(a) / len(a)
    mean_b = sum(b) / len(b)
    numerator = sum((x - mean_a) * (y - mean_b) for x, y in zip(a, b))
    denominator = math.sqrt(sum((x - mean_a) ** 2 for x in a) * sum((y - mean_b) ** 2 for y in b))
    return numerator / denominator if denominator else 0.0


def read_notes(path):
    midi = mido.MidiFile(path)
    merged = mido.merge_tracks(midi.tracks)
    active = defaultdict(list)
    notes = []
    tick = 0
    tempo = 500000
    first_tempo = None
    for msg in merged:
        tick += msg.time
        if msg.is_meta:
            if msg.type == "set_tempo":
                tempo = msg.tempo
                if first_tempo is None:
                    first_tempo = tempo
            continue
        if msg.type == "note_on" and msg.velocity > 0:
            active[(msg.channel, msg.note)].append((tick, msg.velocity))
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            key = (msg.channel, msg.note)
            if active.get(key):
                start, velocity = active[key].pop(0)
                notes.append({"start": start, "end": tick, "note": msg.note, "velocity": velocity, "channel": msg.channel})
    for (channel, note), pending in active.items():
        for start, velocity in pending:
            notes.append({"start": start, "end": start + 1, "note": note, "velocity": velocity, "channel": channel})
    return midi.ticks_per_beat, first_tempo or 500000, notes


def detect_key(notes):
    histogram = [0.0] * 12
    for note in notes:
        histogram[note["note"] % 12] += max(1, note["end"] - note["start"])
    total = sum(histogram)
    if total:
        histogram = [x / total for x in histogram]
    best = None
    for tonic in range(12):
        major = histogram[tonic:] + histogram[:tonic]
        minor = histogram[tonic:] + histogram[:tonic]
        major_score = _correlation(major, MAJOR_PROFILE)
        minor_score = _correlation(minor, MINOR_PROFILE)
        if best is None or major_score > best[0]:
            best = (major_score, tonic, "major", MAJOR_SCALE)
        if minor_score > best[0]:
            best = (minor_score, tonic, "minor", MINOR_SCALE)
    if best is None:
        return 0, "major", MAJOR_SCALE
    return best[1], best[2], best[3]


def merge_fragments(notes, max_gap_ticks):
    grouped = defaultdict(list)
    for note in notes:
        grouped[(note["channel"], note["note"])].append(note)
    merged = []
    for group in grouped.values():
        group.sort(key=lambda n: n["start"])
        current = None
        for note in group:
            if current is None:
                current = dict(note)
            elif note["start"] - current["end"] <= max_gap_ticks and current["end"] - current["start"] <= max_gap_ticks * 3:
                current["end"] = max(current["end"], note["end"])
                current["velocity"] = max(current["velocity"], note["velocity"])
            else:
                merged.append(current)
                current = dict(note)
        if current is not None:
            merged.append(current)
    return merged


def stabilize_pitch(pitch, tonic, scale):
    scale_pitches = [(tonic + degree) % 12 for degree in scale]
    pitch_class = pitch % 12
    if pitch_class in scale_pitches:
        return pitch
    best = None
    for candidate in scale_pitches:
        delta = (candidate - pitch_class + 12) % 12
        if delta > 6:
            delta -= 12
        if best is None or abs(delta) < abs(best[0]):
            best = (delta, candidate)
    if best and abs(best[0]) <= 1:
        return pitch + best[0]
    return pitch


def optimize_midi(input_path, output_path, grid=None, merge_gap_ms=15, min_note_ms=30,
                  scale_snap=False, auto_transpose=True):
    tpb, tempo, notes = read_notes(input_path)
    if not notes:
        raise ValueError("MIDI 中没有音符")
    if grid is None:
        grid = max(1, tpb // 4)
    min_ticks = max(1, round(min_note_ms / 1000.0 * tpb * 1e6 / tempo))
    merge_gap_ticks = max(1, round(merge_gap_ms / 1000.0 * tpb * 1e6 / tempo))

    notes = merge_fragments(notes, merge_gap_ticks)
    tonic, mode, scale = detect_key(notes) if (scale_snap or auto_transpose) else (0, "major", MAJOR_SCALE)
    transposed_by = 0
    if auto_transpose and not scale_snap:
        # Force the whole song into C major / A minor so every pitch lands on a
        # natural key: the melody's intervals survive, unlike per-note snapping.
        target_tonic = 0 if mode == "major" else 9
        transposed_by = (target_tonic - tonic) % 12
        if transposed_by:
            for note in notes:
                note["note"] = max(0, min(127, note["note"] + transposed_by))
        tonic = target_tonic
    optimized = []
    for note in notes:
        start = int(round(note["start"] / grid) * grid)
        end = max(start + min_ticks, int(round(note["end"] / grid) * grid))
        if end - start < min_ticks:
            continue
        pitch = stabilize_pitch(note["note"], tonic, scale) if scale_snap else note["note"]
        optimized.append({**note, "start": start, "end": end, "note": max(0, min(127, pitch))})

    events = [(0, mido.MetaMessage("set_tempo", tempo=tempo, time=0))]
    for note in sorted(optimized, key=lambda n: (n["start"], n["note"])):
        events.append((note["start"], mido.Message("note_on", channel=note["channel"], note=note["note"], velocity=note["velocity"], time=0)))
        events.append((note["end"], mido.Message("note_off", channel=note["channel"], note=note["note"], velocity=0, time=0)))
    events.sort(key=lambda item: (item[0], 1 if getattr(item[1], "type", "") == "note_off" else 0))

    out = mido.MidiFile(type=0, ticks_per_beat=tpb)
    track = mido.MidiTrack()
    previous = 0
    for tick, msg in events:
        msg.time = tick - previous
        track.append(msg)
        previous = tick
    track.append(mido.MetaMessage("end_of_track", time=0))
    out.tracks.append(track)
    os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
    out.save(output_path)
    return {
        "notes_in": len(notes),
        "notes_out": len(optimized),
        "key": "%s %s" % (["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"][tonic], mode),
        "grid_ticks": grid,
        "transposed_by": transposed_by,
    }
