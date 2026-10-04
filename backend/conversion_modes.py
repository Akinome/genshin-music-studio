#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Core MIDI conversion modes shared by the WinUI backend and CLI tools."""
import io
import os
from contextlib import redirect_stdout

import duration_preserving as dp
import melody_extract as me
import midi_to_genshin as mtg


def convert_one_file(path, cfg, out_dir, track_index=0, write=True):
    if cfg.get("melody_only"):
        return me.convert_file(path, cfg, out_dir, track_index, write)
    if cfg.get("preserve_duration"):
        return dp.convert_file(path, cfg, out_dir, track_index, write)

    notes, tpb, first_tempo, tempo_set = mtg.load_notes(path, track_index or None)
    strikes, dropped_range = mtg.normalize(notes, cfg)
    if not strikes:
        raise ValueError("没有可演奏音符")
    events, smallest_inter = mtg.plan(strikes, cfg, tpb, first_tempo)
    scale = mtg.compute_scale(cfg, smallest_inter)
    min_gap_ticks = max(1, round(cfg["min_gap_ms"] / 1000.0 * tpb * 1e6 / (first_tempo * scale)))
    final_presses, dropped_dense = mtg.dense_filter(events, min_gap_ticks)
    if not final_presses:
        raise ValueError("所有音符都被过密过滤")

    low = min(n for _, _, n, _, _ in strikes)
    high = max(n for _, _, n, _, _ in strikes)
    total_sec = final_presses[-1][0] * first_tempo * scale / (tpb * 1e6)
    out_name = os.path.splitext(os.path.basename(path))[0] + "_genshin.mid"
    out_path = ""
    if write:
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, out_name)
        if os.path.abspath(out_path) == os.path.abspath(path):
            out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(path))[0] + "_converted.mid")
        with redirect_stdout(io.StringIO()):
            mtg.write_output(final_presses, out_path, tpb, first_tempo, scale, cfg, tempo_set)

    return {
        "source": os.path.basename(path),
        "notes": len(notes),
        "dropped_range": dropped_range,
        "dropped_dense": dropped_dense,
        "actual": len(final_presses),
        "scale": scale,
        "low": mtg.note_name(low),
        "high": mtg.note_name(high),
        "seconds": total_sec,
        "output": out_path,
    }
