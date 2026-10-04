#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Evaluate generated MIDI against a reference MIDI."""
import sys

import mido

DEFAULT_TEMPO = 500000


def build_tempo_map(mid):
    events = []
    for track in mid.tracks:
        tick = 0
        for msg in track:
            tick += msg.time
            if msg.is_meta and msg.type == "set_tempo":
                events.append((tick, msg.tempo))
    events.sort()
    collapsed = []
    for tick, tempo in events:
        if collapsed and collapsed[-1][0] == tick:
            collapsed[-1] = (tick, tempo)
        else:
            collapsed.append((tick, tempo))
    return collapsed


def tick_to_seconds(tick, tempo_events, tpb):
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


def load_notes(path):
    mid = mido.MidiFile(path)
    tempos = build_tempo_map(mid)
    active = {}
    notes = []
    tick = 0
    for msg in mido.merge_tracks(mid.tracks):
        tick += msg.time
        if msg.is_meta:
            continue
        key = (msg.channel, msg.note)
        if msg.type == "note_on" and msg.velocity > 0:
            active.setdefault(key, []).append((tick, msg.velocity))
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            if active.get(key):
                start_tick, velocity = active[key].pop(0)
                notes.append({
                    "start": tick_to_seconds(start_tick, tempos, mid.ticks_per_beat),
                    "end": tick_to_seconds(tick, tempos, mid.ticks_per_beat),
                    "pitch": msg.note,
                    "velocity": velocity,
                })
    for (channel, pitch), pending in active.items():
        for start_tick, velocity in pending:
            start = tick_to_seconds(start_tick, tempos, mid.ticks_per_beat)
            notes.append({"start": start, "end": start + 0.05, "pitch": pitch, "velocity": velocity})
    return mid.length, notes


def match_onsets(reference, predicted, tolerance=0.05):
    used = set()
    matches = []
    for pred in predicted:
        best = None
        for ref_index, ref in enumerate(reference):
            if ref_index in used:
                continue
            distance = abs(pred["start"] - ref["start"])
            if distance <= tolerance and (best is None or distance < best[0]):
                best = (distance, ref_index, ref)
        if best is not None:
            used.add(best[1])
            matches.append((pred, best[2], best[0]))
    return matches


def evaluate(reference_path, predicted_path, tolerance=0.05):
    ref_length, reference = load_notes(reference_path)
    pred_length, predicted = load_notes(predicted_path)
    onset_matches = match_onsets(reference, predicted, tolerance)
    note_matches = [item for item in onset_matches if item[0]["pitch"] == item[1]["pitch"]]
    precision = len(note_matches) / len(predicted) if predicted else 0.0
    recall = len(note_matches) / len(reference) if reference else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    pitch_accuracy = len(note_matches) / len(onset_matches) if onset_matches else 0.0
    onset_error = sum(item[2] for item in onset_matches) / len(onset_matches) if onset_matches else 0.0
    return {
        "reference_notes": len(reference),
        "predicted_notes": len(predicted),
        "matched_notes": len(note_matches),
        "precision": precision,
        "recall": recall,
        "note_f1": f1,
        "onset_accuracy": len(onset_matches) / len(reference) if reference else 0.0,
        "pitch_accuracy": pitch_accuracy,
        "mean_onset_error_ms": onset_error * 1000.0,
        "duration_ratio": pred_length / ref_length if ref_length else 0.0,
    }


def main():
    if len(sys.argv) != 3:
        print("usage: python model_evaluator.py reference.mid predicted.mid")
        return 2
    result = evaluate(sys.argv[1], sys.argv[2])
    for key, value in result.items():
        print("%-22s %.4f" % (key, value) if isinstance(value, float) else "%-22s %s" % (key, value))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
