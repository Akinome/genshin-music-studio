#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""End-to-end download -> AI transcription -> Genshin MIDI pipeline."""
import os
import shutil

import media_tools
import symbolic_optimizer


def conversion_config(min_gap_ms=60, melody_only=True, preserve_duration=True, low=48, high=83,
                      transpose=0, press_ms=120, collapse_window_ms=10):
    return {
        "range": {"low": low, "high": high},
        "transpose_semitones": transpose,
        "chord_mode": "top" if melody_only else "arpeggio",
        "arpeggio_gap_ms": min_gap_ms,
        "press_ms": press_ms,
        "tempo_scale": 1.0,
        "min_gap_ms": min_gap_ms,
        "chord_window_ms": 30,
        "auto_slow": not preserve_duration,
        "quantize_ms": 0,
        "strict_gap": True,
        "preserve_duration": preserve_duration,
        "melody_only": melody_only,
        "collapse_window_ms": collapse_window_ms,
    }


def convert_midi(midi_path, out_dir, cfg, track_index=0):
    from conversion_modes import convert_one_file
    return convert_one_file(midi_path, cfg, out_dir, track_index, True)


def publish_triple_output(raw_midi, out_dir, cfg, callback=None):
    def log(text):
        if callback:
            callback(text)

    base = os.path.splitext(os.path.basename(raw_midi))[0]
    for suffix in ("_basic_pitch_native", "_basic_pitch", "_piano_transcription", "_melody", "_raw"):
        if base.endswith(suffix):
            base = base[: -len(suffix)]
            break
    raw_dir = os.path.join(out_dir, "raw")
    optimized_dir = os.path.join(out_dir, "optimized")
    genshin_dir = os.path.join(out_dir, "genshin")
    os.makedirs(raw_dir, exist_ok=True)
    os.makedirs(optimized_dir, exist_ok=True)
    os.makedirs(genshin_dir, exist_ok=True)
    raw_path = os.path.join(raw_dir, base + "_raw.mid")
    optimized_path = os.path.join(optimized_dir, base + "_optimized.mid")
    if os.path.abspath(raw_midi) != os.path.abspath(raw_path):
        shutil.copy2(raw_midi, raw_path)
    stats = symbolic_optimizer.optimize_midi(raw_path, optimized_path)
    log("符号优化完成：%s，%d -> %d 个音符" % (stats["key"], stats["notes_in"], stats["notes_out"]))
    result = convert_midi(optimized_path, genshin_dir, cfg)
    result["raw"] = raw_path
    result["optimized"] = optimized_path
    return result


def transcribe_with_model(audio_path, work_dir, model="basic_pitch_onnx", callback=None, cancel_event=None, midi_dir=None):
    midi_dir = midi_dir or os.path.join(work_dir, "transcription")
    if model in ("demucs_accompaniment_piano", "demucs_vocals_crepe"):
        stems = media_tools.separate_audio(audio_path, os.path.join(work_dir, "stems"), callback=callback, cancel_event=cancel_event)
        if model == "demucs_vocals_crepe":
            selected = stems.get("vocals") or stems.get("accompaniment")
            if not selected:
                raise RuntimeError("Demucs 没有生成可用的 vocal stem")
            return media_tools.transcribe_melody_crepe(selected, midi_dir, callback=callback, cancel_event=cancel_event)
        selected = stems.get("accompaniment") or stems.get("vocals")
        if not selected:
            raise RuntimeError("Demucs 没有生成可用的 accompaniment stem")
        return media_tools.transcribe_piano(selected, midi_dir, callback=callback, cancel_event=cancel_event)
    if model == "librosa_pyin":
        return media_tools.transcribe_melody_pyin(audio_path, midi_dir, callback=callback, cancel_event=cancel_event)
    if model == "piano_transcription":
        return media_tools.transcribe_piano(audio_path, midi_dir, callback=callback, cancel_event=cancel_event)
    if model == "crepe":
        return media_tools.transcribe_melody_crepe(audio_path, midi_dir, callback=callback, cancel_event=cancel_event)
    return media_tools.transcribe_audio(audio_path, midi_dir, callback=callback, cancel_event=cancel_event)


def process_media_file(media_path, work_dir, out_dir, cfg, extract_audio=False, convert=True,
                       model="basic_pitch_onnx", callback=None, cancel_event=None):
    def log(text):
        if callback:
            callback(text)

    os.makedirs(work_dir, exist_ok=True)
    os.makedirs(out_dir, exist_ok=True)
    audio_path = media_path
    if extract_audio:
        log("提取音频：" + os.path.basename(media_path))
        audio_dir = os.path.join(work_dir, "audio")
        audio_path = media_tools.extract_audio(media_path, audio_dir, callback=log, cancel_event=cancel_event)

    log("AI 扒谱：" + os.path.basename(audio_path))
    midi_path = transcribe_with_model(audio_path, work_dir, model, callback=log, cancel_event=cancel_event)
    if not convert:
        return {"audio": audio_path, "midi": midi_path, "output": os.path.dirname(midi_path)}
    log("生成 raw / optimized / genshin 三份 MIDI")
    result = publish_triple_output(midi_path, out_dir, cfg, callback=log)
    result["audio"] = audio_path
    result["midi"] = midi_path
    return result


def process_url(url, work_dir, out_dir, cfg, cookies_file="", browser="", allow_playlist=False,
                convert=True, model="basic_pitch_onnx", midi_dir=None, callback=None, cancel_event=None):
    def log(text):
        if callback:
            callback(text)

    audio_dir = os.path.join(work_dir, "downloads")
    os.makedirs(out_dir, exist_ok=True)
    midi_dir = midi_dir or os.path.join(work_dir, "transcription")
    os.makedirs(midi_dir, exist_ok=True)
    log("下载音频：" + url)
    files = media_tools.download_media(
        url,
        audio_dir,
        mode="audio",
        cookies_file=cookies_file,
        browser=browser,
        allow_playlist=allow_playlist,
        callback=log,
        cancel_event=cancel_event,
    )
    last_result = None
    for audio_path in files:
        log("AI 扒谱：" + os.path.basename(audio_path))
        midi_path = transcribe_with_model(
            audio_path,
            work_dir,
            model,
            callback=log,
            cancel_event=cancel_event,
            midi_dir=midi_dir,
        )
        if not convert:
            last_result = {"audio": audio_path, "midi": midi_path, "output": os.path.dirname(midi_path)}
            continue
        log("生成 raw / optimized / genshin 三份 MIDI")
        last_result = publish_triple_output(midi_path, out_dir, cfg, callback=log)
        last_result["audio"] = audio_path
        last_result["midi"] = midi_path
    if last_result is None:
        raise RuntimeError("没有下载到可处理的音频")
    return last_result


def process_midi_file(midi_path, out_dir, cfg, track_index=0):
    return publish_triple_output(midi_path, out_dir, cfg)
