#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Convert MIDI to a monophonic Genshin sequence without changing song duration."""
import io
import math
import os
from contextlib import redirect_stdout

import mido

import midi_to_genshin as mtg


def group_strikes(strikes, chord_window_sec):
    ordered = sorted(strikes, key=lambda x: (x[1], -x[2], -x[3]))
    groups = []
    for strike in ordered:
        if not groups or strike[1] - groups[-1][0][1] > chord_window_sec:
            groups.append([strike])
        else:
            groups[-1].append(strike)
    return groups


def plan_duration_preserving(strikes, cfg, song_end_sec):
    min_gap = cfg["min_gap_ms"] / 1000.0
    chord_window = cfg["chord_window_ms"] / 1000.0
    arp_gap = cfg["arpeggio_gap_ms"] / 1000.0
    if cfg.get("strict_gap", True):
        arp_gap = max(arp_gap, min_gap)

    groups = group_strikes(strikes, chord_window)
    presses = []
    dropped = 0
    last_time = None

    for gi, group in enumerate(groups):
        group_start = min(s[1] for s in group)
        if gi + 1 < len(groups):
            limit = groups[gi + 1][0][1] - min_gap
        else:
            limit = song_end_sec - 0.001
        limit = max(group_start, limit)

        if cfg.get("chord_mode") == "top":
            candidates = [max(group, key=lambda x: (x[2], x[3]))]
        else:
            candidates = sorted(group, key=lambda x: (-x[2], -x[3]))

        scheduled = 0
        for tick, sec, note, vel, dur in candidates:
            desired = group_start + scheduled * arp_gap
            candidate = desired
            if last_time is not None:
                candidate = max(candidate, last_time + min_gap)
            if candidate > limit + 1e-9:
                dropped += 1
                continue
            candidate = max(0.0, candidate)
            presses.append((candidate, note, vel))
            last_time = candidate
            scheduled += 1

    return presses, dropped


def write_output_seconds(presses, out_path, tpb, tempo, song_end_sec, cfg):
    press_sec = cfg["press_ms"] / 1000.0
    min_gap_ticks = max(1, int(math.ceil(cfg["min_gap_ms"] / 1000.0 * tpb * 1e6 / tempo)))

    def tick(sec):
        return int(round(sec * tpb * 1e6 / tempo))

    end_tick = max(1, tick(song_end_sec))
    ordered = sorted(presses, key=lambda x: x[0])
    events = [(0, mido.MetaMessage("set_tempo", tempo=tempo, time=0))]
    prev_on_tick = None
    for i, (sec, note, vel) in enumerate(ordered):
        on_tick = tick(sec)
        if prev_on_tick is not None:
            on_tick = max(on_tick, prev_on_tick + min_gap_ticks)
        on_tick = min(on_tick, end_tick - 1)
        off_tick = min(tick(sec + press_sec), end_tick)
        if i + 1 < len(ordered):
            off_tick = min(off_tick, tick(ordered[i + 1][0]) - 1)
        off_tick = max(on_tick + 1, off_tick)
        events.append((on_tick, mido.Message("note_on", note=note, velocity=max(1, min(127, vel)), time=0)))
        events.append((off_tick, mido.Message("note_off", note=note, velocity=0, time=0)))
        prev_on_tick = on_tick

    events.sort(key=lambda x: x[0])
    mid = mido.MidiFile()
    mid.type = 0
    mid.ticks_per_beat = tpb
    track = mido.MidiTrack()
    prev = 0
    for event_tick, msg in events:
        msg.time = event_tick - prev
        track.append(msg)
        prev = event_tick
    if end_tick > prev:
        track.append(mido.MetaMessage("end_of_track", time=end_tick - prev))
    else:
        track.append(mido.MetaMessage("end_of_track", time=0))
    mid.tracks.append(track)
    mid.save(out_path)


def convert_file(path, cfg, out_dir, track_index=0, write=True):
    source_mid = mido.MidiFile(path)
    source_seconds = source_mid.length
    notes, tpb, first_tempo, _ = mtg.load_notes(path, track_index or None)
    strikes, dropped_range = mtg.normalize(notes, cfg)
    if not strikes:
        raise ValueError("没有可演奏音符")
    presses, dropped_dense = plan_duration_preserving(strikes, cfg, source_seconds)
    if not presses:
        raise ValueError("所有音符都因过密被简化")

    out_name = os.path.splitext(os.path.basename(path))[0] + "_genshin.mid"
    out_path = ""
    output_seconds = source_seconds
    if write:
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, out_name)
        if os.path.abspath(out_path) == os.path.abspath(path):
            out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(path))[0] + "_converted.mid")
        with redirect_stdout(io.StringIO()):
            write_output_seconds(presses, out_path, tpb, first_tempo, source_seconds, cfg)
        output_seconds = mido.MidiFile(out_path).length

    low = min(n for _, _, n, _, _ in strikes)
    high = max(n for _, _, n, _, _ in strikes)
    return {
        "source": os.path.basename(path),
        "notes": len(notes),
        "dropped_range": dropped_range,
        "dropped_dense": dropped_dense,
        "actual": len(presses),
        "scale": 1.0,
        "low": mtg.note_name(low),
        "high": mtg.note_name(high),
        "seconds": output_seconds,
        "source_seconds": source_seconds,
        "duration_ratio": output_seconds / source_seconds if source_seconds else 1.0,
        "output": out_path,
    }
