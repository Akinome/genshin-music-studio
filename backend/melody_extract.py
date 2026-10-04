#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract a monophonic melody line from multi-track MIDI and keep duration."""
import io
import os
from contextlib import redirect_stdout

import mido

import duration_preserving as dp
import midi_to_genshin as mtg

DEFAULT_TEMPO = 500000


def build_tempo_map(mid):
    events = []
    for track in mid.tracks:
        abs_tick = 0
        for msg in track:
            abs_tick += msg.time
            if msg.is_meta and msg.type == "set_tempo":
                events.append((abs_tick, msg.tempo))
    events.sort(key=lambda x: x[0])
    collapsed = []
    for tick, tempo in events:
        if collapsed and collapsed[-1][0] == tick:
            collapsed[-1] = (tick, tempo)
        else:
            collapsed.append((tick, tempo))
    return collapsed


def tick_to_seconds(tick, tempo_events, tpb):
    if not tpb:
        return 0.0
    tempo = DEFAULT_TEMPO
    last_tick = 0
    seconds = 0.0
    for event_tick, event_tempo in tempo_events:
        if event_tick > tick:
            break
        seconds += (event_tick - last_tick) * tempo / (tpb * 1e6)
        last_tick = event_tick
        tempo = event_tempo
    seconds += (tick - last_tick) * tempo / (tpb * 1e6)
    return seconds


def load_tracks(path):
    mid = mido.MidiFile(path)
    tempos = build_tempo_map(mid)
    tracks = []
    for track_index, track in enumerate(mid.tracks):
        onsets = {}
        notes = []
        abs_tick = 0
        for msg in track:
            abs_tick += msg.time
            if msg.is_meta:
                continue
            if msg.type == "note_on" and msg.velocity > 0 and msg.channel != 9:
                onsets.setdefault((msg.channel, msg.note), []).append((abs_tick, msg.velocity))
            elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
                key = (msg.channel, msg.note)
                if onsets.get(key):
                    start_tick, velocity = onsets[key].pop(0)
                    start_sec = tick_to_seconds(start_tick, tempos, mid.ticks_per_beat)
                    end_sec = tick_to_seconds(abs_tick, tempos, mid.ticks_per_beat)
                    notes.append({
                        "track": track_index,
                        "channel": msg.channel,
                        "tick": start_tick,
                        "sec": start_sec,
                        "note": msg.note,
                        "vel": velocity,
                        "dur": max(0.01, end_sec - start_sec),
                    })
        for (channel, note), pending in onsets.items():
            for start_tick, velocity in pending:
                start_sec = tick_to_seconds(start_tick, tempos, mid.ticks_per_beat)
                notes.append({
                    "track": track_index,
                    "channel": channel,
                    "tick": start_tick,
                    "sec": start_sec,
                    "note": note,
                    "vel": velocity,
                    "dur": 0.05,
                })
        notes.sort(key=lambda n: (n["sec"], -n["note"], -n["vel"]))
        tracks.append(notes)
    return mid, tempos, tracks


def group_by_onset(notes, window_sec):
    ordered = sorted(notes, key=lambda n: (n["sec"], -n["note"], -n["vel"]))
    groups = []
    for note in ordered:
        if not groups or note["sec"] - groups[-1][0]["sec"] > window_sec:
            groups.append([note])
        else:
            groups[-1].append(note)
    return groups


def track_statistics(tracks):
    all_notes = [n for track in tracks for n in track]
    global_groups = group_by_onset(all_notes, 0.03) if all_notes else []
    top_count = {}
    for group in global_groups:
        top_pitch = max(n["note"] for n in group)
        for note in group:
            if note["note"] == top_pitch:
                key = note["track"]
                top_count[key] = top_count.get(key, 0) + 1

    stats = []
    for index, notes in enumerate(tracks):
        if not notes:
            stats.append({"track": index, "count": 0, "score": -1.0})
            continue
        groups = group_by_onset(notes, 0.03)
        mean_pitch = sum(n["note"] for n in notes) / len(notes)
        mean_vel = sum(n["vel"] for n in notes) / len(notes)
        mean_dur = sum(n["dur"] for n in notes) / len(notes)
        avg_group = sum(len(g) for g in groups) / len(groups)
        top_ratio = top_count.get(index, 0) / len(notes)
        note_span = max(n["sec"] + n["dur"] for n in notes) - min(n["sec"] for n in notes)
        score = (
            0.48 * top_ratio
            + 0.22 * (mean_pitch / 127.0)
            + 0.10 * (mean_vel / 127.0)
            + 0.12 * (1.0 - min(avg_group, 5.0) / 5.0)
            + 0.08 * min(mean_dur / 0.5, 1.0)
        )
        stats.append({
            "track": index,
            "count": len(notes),
            "mean_pitch": mean_pitch,
            "mean_vel": mean_vel,
            "mean_dur": mean_dur,
            "avg_group": avg_group,
            "top_ratio": top_ratio,
            "span": note_span,
            "score": score,
        })
    return stats


def choose_track(stats):
    valid = [s for s in stats if s.get("count", 0) > 0]
    if not valid:
        raise ValueError("MIDI 中没有可演奏音符")
    return max(valid, key=lambda s: s["score"])["track"]


def viterbi_melody(notes, onset_window_ms=30, max_candidates=10):
    groups = group_by_onset(notes, onset_window_ms / 1000.0)
    if not groups:
        return []
    candidates = []
    for group in groups:
        ranked = sorted(group, key=lambda n: (-n["note"], -n["vel"], -n["dur"]))[:max_candidates]
        candidates.append(ranked)

    states = []
    for gi, group in enumerate(candidates):
        current = []
        group_median = sorted(n["note"] for n in group)[len(group) // 2]
        for note in group:
            pitch_score = note["note"] / 127.0
            vel_score = note["vel"] / 127.0
            dur_score = min(note["dur"] / 0.5, 1.0)
            top_bonus = 1.0 if note["note"] == max(n["note"] for n in group) else 0.0
            register_penalty = min(abs(note["note"] - group_median) / 12.0, 1.0)
            base = (0.30 * pitch_score + 0.25 * vel_score + 0.25 * dur_score
                    + 0.20 * top_bonus - 0.15 * register_penalty)
            if gi == 0:
                current.append((base, -1, note))
                continue
            best_score = None
            best_prev = -1
            for pi, (prev_score, _, prev_note) in enumerate(states[-1]):
                leap = abs(note["note"] - prev_note["note"])
                continuity = max(0.0, 1.0 - leap / 12.0)
                repeat_bonus = 0.15 if leap <= 2 else 0.0
                octave_penalty = 0.25 if leap >= 12 else 0.0
                score = prev_score + base + 0.45 * continuity + repeat_bonus - octave_penalty
                if best_score is None or score > best_score:
                    best_score = score
                    best_prev = pi
            current.append((best_score, best_prev, note))
        states.append(current)

    best_index = max(range(len(states[-1])), key=lambda i: states[-1][i][0])
    melody = []
    for gi in range(len(states) - 1, -1, -1):
        _, prev_index, note = states[gi][best_index]
        melody.append(dict(note))
        best_index = prev_index
    melody.reverse()
    # Fix isolated octave spikes that immediately return to the previous register.
    for index in range(1, len(melody) - 1):
        previous = melody[index - 1]
        current = melody[index]
        following = melody[index + 1]
        if (current["note"] - previous["note"] >= 12
                and following["note"] - current["note"] <= -10):
            current["note"] -= 12
        elif (previous["note"] - current["note"] >= 12
              and following["note"] - current["note"] >= 10):
            current["note"] += 12
    return melody


def extract_melody(path, track_index=0, collapse_window_ms=10):
    mid, _, tracks = load_tracks(path)
    stats = track_statistics(tracks)
    all_notes = [n for track in tracks for n in track]
    if not all_notes:
        raise ValueError("MIDI 中没有可演奏音符")

    method = "track"
    if track_index:
        selected_track = track_index - 1
        if selected_track < 0 or selected_track >= len(tracks):
            raise ValueError("音轨编号超出范围")
        selected = tracks[selected_track]
        if not selected:
            raise ValueError("所选音轨没有可演奏音符")
    else:
        selected_track = choose_track(stats)
        selected = tracks[selected_track]
        selected_stats = stats[selected_track]
        non_empty = [s for s in stats if s.get("count", 0) > 0]
        if len(non_empty) > 1 and selected_stats["top_ratio"] < 0.5:
            method = "viterbi-all"
            selected = all_notes

    if method == "viterbi-all":
        melody = viterbi_melody(selected)
    elif stats[selected_track].get("avg_group", 1.0) > 1.2:
        melody = viterbi_melody(selected)
    else:
        melody = []
        for group in group_by_onset(selected, collapse_window_ms / 1000.0):
            melody.append(max(group, key=lambda n: (n["note"], n["vel"], n["dur"])))
    melody.sort(key=lambda n: (n["sec"], n["note"]))
    return melody, {
        "track_count": len(tracks),
        "selected_track": selected_track + 1,
        "method": method,
        "stats": stats,
    }


def convert_file(path, cfg, out_dir, track_index=0, write=True):
    source_mid = mido.MidiFile(path)
    source_seconds = source_mid.length
    melody, info = extract_melody(path, track_index, cfg.get("collapse_window_ms", 10))
    strikes = [(n["tick"], n["sec"], n["note"], n["vel"], n["dur"]) for n in melody]
    strikes, dropped_range = mtg.normalize(strikes, cfg)
    if not strikes:
        raise ValueError("旋律没有落入琴音域")

    melody_cfg = dict(cfg)
    melody_cfg["chord_mode"] = "top"
    presses, dropped_dense = dp.plan_duration_preserving(strikes, melody_cfg, source_seconds)
    if not presses:
        raise ValueError("旋律全部因过密被简化")

    out_name = os.path.splitext(os.path.basename(path))[0] + "_genshin.mid"
    out_path = ""
    output_seconds = source_seconds
    if write:
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, out_name)
        if os.path.abspath(out_path) == os.path.abspath(path):
            out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(path))[0] + "_melody.mid")
        with redirect_stdout(io.StringIO()):
            dp.write_output_seconds(presses, out_path, source_mid.ticks_per_beat, mtg.DEFAULT_TEMPO, source_seconds, melody_cfg)
        output_seconds = mido.MidiFile(out_path).length

    low = min(n for _, _, n, _, _ in strikes)
    high = max(n for _, _, n, _, _ in strikes)
    return {
        "source": os.path.basename(path),
        "notes": len(melody),
        "dropped_range": dropped_range,
        "dropped_dense": dropped_dense,
        "actual": len(presses),
        "scale": 1.0,
        "low": mtg.note_name(low),
        "high": mtg.note_name(high),
        "seconds": output_seconds,
        "source_seconds": source_seconds,
        "duration_ratio": output_seconds / source_seconds if source_seconds else 1.0,
        "track_count": info["track_count"],
        "selected_track": info["selected_track"],
        "method": info["method"],
        "output": out_path,
    }
