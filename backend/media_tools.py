#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Video/audio download and AI transcription helpers."""
import glob
import io
import importlib.util
import math
import os
import queue
import shutil
import subprocess
import sys
import threading
import time
from contextlib import redirect_stderr, redirect_stdout

import mido

CREATE_NO_WINDOW = 0x08000000 if os.name == "nt" else 0
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def tool_path(name):
    return shutil.which(name)


def ai_python_path():
    candidates = [
        os.path.join(BASE_DIR, ".venv-ai", "Scripts", "python.exe"),
        os.path.join(BASE_DIR, ".venv-ai", "bin", "python"),
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    return None


def basic_pitch_path():
    candidates = [
        os.path.join(BASE_DIR, ".venv-ai", "Scripts", "basic-pitch.exe"),
        os.path.join(BASE_DIR, ".venv-ai", "bin", "basic-pitch"),
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    return shutil.which("basic-pitch")


def native_onnx_model_path():
    candidates = [
        os.path.join(BASE_DIR, "GenshinMusicStudio.WinUI", "Assets", "Models", "nmp.onnx"),
        os.path.join(BASE_DIR, "Assets", "Models", "nmp.onnx"),
    ]
    for path in candidates:
        if os.path.isfile(path):
            return path
    return None


def venv_site_packages():
    venv = os.path.join(BASE_DIR, ".venv-ai")
    for sub in ("Lib", "lib"):
        site = os.path.join(venv, sub, "site-packages")
        if os.path.isdir(site):
            return site
    matches = sorted(glob.glob(os.path.join(venv, "lib", "python*", "site-packages")))
    return matches[0] if matches else None


def venv_package_path(*names):
    site = venv_site_packages()
    if not site:
        return None
    for name in names:
        pkg = os.path.join(site, name)
        if os.path.isdir(pkg) and os.listdir(pkg):
            return pkg
        if os.path.isfile(pkg + ".py"):
            return pkg + ".py"
    for name in names:
        for pattern in (name + "-*.dist-info", name + ".dist-info"):
            matches = sorted(glob.glob(os.path.join(site, pattern)))
            if matches:
                return matches[-1]
    return None


def install_ai_environment(callback=None, cancel_event=None, python_version="3.11", targets=None):
    uv = tool_path("uv")
    if not uv:
        raise RuntimeError("未找到 uv，请先安装 uv：https://docs.astral.sh/uv/")
    env_dir = os.path.join(BASE_DIR, ".venv-ai")
    python_path = ai_python_path()
    if not python_path:
        run_command([uv, "venv", "--python", python_version, env_dir], callback=callback, cancel_event=cancel_event)
        python_path = ai_python_path()
    if not python_path:
        raise RuntimeError("AI 环境创建失败")
    groups = set(targets) if targets else {"basic", "piano", "demucs", "crepe"}
    needs_torch = bool(groups & {"piano", "demucs", "crepe"})

    if "basic" in groups:
        run_command(
            [uv, "pip", "install", "--python", python_path, "-U", "basic-pitch", "yt-dlp", "librosa", "soundfile", "setuptools<81"],
            callback=callback,
            cancel_event=cancel_event,
        )
    if needs_torch:
        if callback:
            callback("安装 PyTorch CPU 版本（体积较大，请耐心等待）")
        run_command(
            [uv, "pip", "install", "--python", python_path, "torch", "torchaudio", "--index-url", "https://download.pytorch.org/whl/cpu"],
            callback=callback,
            cancel_event=cancel_event,
        )
    if "piano" in groups:
        if callback:
            callback("安装钢琴转录模型")
        run_command(
            [uv, "pip", "install", "--python", python_path, "-U", "piano_transcription_inference"],
            callback=callback,
            cancel_event=cancel_event,
        )
    if "demucs" in groups:
        if callback:
            callback("安装 Demucs 人声/伴奏分离模型")
        run_command(
            [uv, "pip", "install", "--python", python_path, "-U", "demucs"],
            callback=callback,
            cancel_event=cancel_event,
        )
    if "crepe" in groups:
        if callback:
            callback("安装 CREPE 单音旋律模型")
        run_command(
            [uv, "pip", "install", "--python", python_path, "-U", "torchcrepe"],
            callback=callback,
            cancel_event=cancel_event,
        )
    return python_path


def dependency_status():
    def module_path(name):
        spec = importlib.util.find_spec(name)
        return spec.origin if spec else None
    status = {
        "yt-dlp": tool_path("yt-dlp"),
        "ffmpeg": tool_path("ffmpeg"),
        "uv": tool_path("uv"),
        "AI环境": ai_python_path(),
        "soundfile": module_path("soundfile"),
        "Basic Pitch ONNX": native_onnx_model_path(),
        "Basic Pitch (Python)": venv_package_path("basic_pitch", "basic-pitch"),
        "PyTorch": venv_package_path("torch"),
        "Piano Transcription": venv_package_path("piano_transcription_inference"),
        "Demucs": venv_package_path("demucs"),
        "CREPE": venv_package_path("torchcrepe"),
        "librosa pyin": module_path("librosa") or venv_package_path("librosa"),
    }
    return status


def terminate_process(proc):
    if proc.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/F", "/T", "/PID", str(proc.pid)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            creationflags=CREATE_NO_WINDOW,
        )
    else:
        proc.terminate()


def run_command(cmd, callback=None, cancel_event=None, cwd=None):
    env = os.environ.copy()
    env["PYTHONUTF8"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    env["LANG"] = "zh_CN.UTF-8"
    env["LC_ALL"] = "zh_CN.UTF-8"
    env["TF_CPP_MIN_LOG_LEVEL"] = "2"
    proc = subprocess.Popen(
        cmd,
        cwd=cwd,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=CREATE_NO_WINDOW,
    )
    output_queue = queue.Queue()

    def read_output():
        try:
            if proc.stdout:
                for line in proc.stdout:
                    output_queue.put(line)
        finally:
            output_queue.put(None)

    threading.Thread(target=read_output, daemon=True).start()
    try:
        while True:
            if cancel_event is not None and cancel_event.is_set():
                terminate_process(proc)
                try:
                    proc.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    proc.kill()
                raise RuntimeError("任务已取消")
            try:
                line = output_queue.get(timeout=0.1)
            except queue.Empty:
                if proc.poll() is not None:
                    try:
                        line = output_queue.get(timeout=1.0)
                    except queue.Empty:
                        break
                else:
                    continue
            if line is None:
                break
            if callback:
                try:
                    callback(line.rstrip())
                except Exception:
                    pass
    finally:
        if proc.stdout:
            try:
                proc.stdout.close()
            except Exception:
                pass
    proc.wait()
    if proc.returncode != 0:
        raise RuntimeError("命令执行失败，退出码 %s" % proc.returncode)


def _new_files(before, directory, extensions):
    exts = tuple(x.lower() for x in extensions)
    found = []
    for path in glob.glob(os.path.join(directory, "**", "*"), recursive=True):
        if not os.path.isfile(path):
            continue
        if path in before or not path.lower().endswith(exts):
            continue
        found.append(path)
    if not found:
        candidates = [p for p in glob.glob(os.path.join(directory, "**", "*"), recursive=True)
                      if os.path.isfile(p) and p.lower().endswith(exts)]
        found = sorted(candidates, key=os.path.getmtime)[-1:]
    return sorted(found, key=os.path.getmtime)


def download_media(url, out_dir, mode="audio", cookies_file="", browser="", allow_playlist=False,
                   callback=None, cancel_event=None):
    exe = tool_path("yt-dlp")
    if not exe:
        raise RuntimeError("未找到 yt-dlp，请先安装：python -m pip install -U yt-dlp")
    os.makedirs(out_dir, exist_ok=True)
    before = set(glob.glob(os.path.join(out_dir, "**", "*"), recursive=True))
    cmd = [
        exe,
        "--newline",
        "--no-warnings",
        "-o", os.path.join(out_dir, "%(title).180s.%(ext)s"),
    ]
    if not allow_playlist:
        cmd.append("--no-playlist")
    if cookies_file:
        cmd += ["--cookies", cookies_file]
    if browser and browser != "不使用":
        cmd += ["--cookies-from-browser", browser]
    if mode == "audio":
        if not tool_path("ffmpeg"):
            raise RuntimeError("音频转换需要 ffmpeg，请先安装 ffmpeg")
        cmd += ["-x", "--audio-format", "wav", "--audio-quality", "0"]
        extensions = [".wav", ".mp3", ".m4a", ".flac", ".ogg", ".webm"]
    else:
        cmd += ["-f", "bv*+ba/b", "--merge-output-format", "mp4"]
        extensions = [".mp4", ".mkv", ".webm"]
    cmd.append(url)
    run_command(cmd, callback=callback, cancel_event=cancel_event)
    files = _new_files(before, out_dir, extensions)
    if not files:
        raise RuntimeError("下载完成但没有找到媒体文件")
    return files


def extract_audio(input_path, out_dir, callback=None, cancel_event=None):
    ffmpeg = tool_path("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("提取音频需要 ffmpeg，请先安装 ffmpeg")
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(input_path))[0] + ".wav")
    cmd = [ffmpeg, "-y", "-i", input_path, "-vn", "-acodec", "pcm_s16le", "-ar", "44100", "-ac", "2", out_path]
    run_command(cmd, callback=callback, cancel_event=cancel_event)
    return out_path


def separate_audio(input_path, out_dir, callback=None, cancel_event=None):
    python = ai_python_path() or sys.executable
    os.makedirs(out_dir, exist_ok=True)
    if callback:
        callback("Demucs 分离人声/伴奏：" + os.path.basename(input_path))
    command = [
        python,
        "-m",
        "demucs.separate",
        "-n",
        "htdemucs",
        "--two-stems",
        "vocals",
        "-o",
        out_dir,
        input_path,
    ]
    run_command(command, callback=callback, cancel_event=cancel_event)
    base = os.path.splitext(os.path.basename(input_path))[0]
    stem_dir = os.path.join(out_dir, "htdemucs", base)
    vocals = os.path.join(stem_dir, "vocals.wav")
    accompaniment = os.path.join(stem_dir, "no_vocals.wav")
    if not os.path.exists(vocals) and not os.path.exists(accompaniment):
        raise RuntimeError("Demucs 没有生成分离音轨")
    return {"vocals": vocals if os.path.exists(vocals) else None, "accompaniment": accompaniment if os.path.exists(accompaniment) else None, "directory": stem_dir}


def transcribe_melody_pyin(audio_path, out_dir, min_note_ms=80, callback=None, cancel_event=None):
    try:
        import librosa
        import numpy as np
    except Exception as exc:
        raise RuntimeError("内置旋律扒谱需要 librosa，请运行：python -m pip install -U librosa soundfile") from exc

    os.makedirs(out_dir, exist_ok=True)
    if callback:
        callback("使用内置旋律扒谱（librosa pyin）")
    y, sr = librosa.load(audio_path, sr=22050, mono=True)
    if cancel_event is not None and cancel_event.is_set():
        raise RuntimeError("任务已取消")
    harmonic = librosa.effects.harmonic(y, margin=3.0)
    f0, voiced_flag, voiced_prob = librosa.pyin(
        harmonic,
        fmin=librosa.note_to_hz("C2"),
        fmax=librosa.note_to_hz("C6"),
        sr=sr,
        frame_length=2048,
        hop_length=512,
    )
    times = librosa.times_like(f0, sr=sr, hop_length=512)
    notes = []
    current = None
    for index, freq in enumerate(f0):
        if cancel_event is not None and cancel_event.is_set():
            raise RuntimeError("任务已取消")
        valid = bool(voiced_flag[index]) and math.isfinite(float(freq)) and float(voiced_prob[index]) >= 0.35
        midi_note = None
        velocity = 80
        if valid:
            midi_note = int(round(librosa.hz_to_midi(float(freq))))
            velocity = int(max(45, min(115, 55 + 60 * float(voiced_prob[index]))))
        if midi_note is None:
            if current is not None:
                start, end, note, vel = current
                if end - start >= min_note_ms / 1000.0:
                    notes.append((start, end, note, vel))
                current = None
            continue
        if current is None:
            current = [float(times[index]), float(times[index]), midi_note, velocity]
        elif abs(midi_note - current[2]) <= 0.6:
            current[1] = float(times[index])
            current[3] = max(current[3], velocity)
        else:
            start, end, note, vel = current
            if end - start >= min_note_ms / 1000.0:
                notes.append((start, end, note, vel))
            current = [float(times[index]), float(times[index]), midi_note, velocity]
    if current is not None and current[1] - current[0] >= min_note_ms / 1000.0:
        notes.append((current[0], current[1], current[2], current[3]))

    merged = []
    for note in notes:
        if merged and note[2] == merged[-1][2] and note[0] - merged[-1][1] < 0.06:
            merged[-1] = (merged[-1][0], note[1], note[2], max(merged[-1][3], note[3]))
        else:
            merged.append(note)
    if not merged:
        raise RuntimeError("没有从音频中识别到稳定旋律音高")

    tpb = 480
    tempo = 500000
    ticks_per_second = tpb * 1e6 / tempo
    mid = mido.MidiFile(type=0, ticks_per_beat=tpb)
    track = mido.MidiTrack()
    track.append(mido.MetaMessage("set_tempo", tempo=tempo, time=0))
    events = []
    for start, end, note, velocity in merged:
        on_tick = int(round(start * ticks_per_second))
        off_tick = max(on_tick + 1, int(round(max(end, start + min_note_ms / 1000.0) * ticks_per_second)))
        events.append((on_tick, mido.Message("note_on", note=note, velocity=velocity, time=0)))
        events.append((off_tick, mido.Message("note_off", note=note, velocity=0, time=0)))
    events.sort(key=lambda x: x[0])
    prev = 0
    for event_tick, msg in events:
        msg.time = event_tick - prev
        track.append(msg)
        prev = event_tick
    track.append(mido.MetaMessage("end_of_track", time=0))
    mid.tracks.append(track)
    out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(audio_path))[0] + "_melody.mid")
    mid.save(out_path)
    if callback:
        callback("内置旋律扒谱完成：%d 个音符" % len(merged))
    return out_path


def transcribe_piano(audio_path, out_dir, callback=None, cancel_event=None):
    try:
        import librosa
        import torch
        from piano_transcription_inference import PianoTranscription, sample_rate
    except Exception as exc:
        raise RuntimeError("未安装钢琴专用模型。请运行：uv pip install --python .venv-ai\\Scripts\\python.exe piano_transcription_inference torch torchaudio") from exc

    os.makedirs(out_dir, exist_ok=True)
    checkpoint = os.path.join(
        os.path.expanduser("~"),
        "piano_transcription_inference_data",
        "note_F1=0.9677_pedal_F1=0.9186.pth",
    )
    if not os.path.exists(checkpoint) or os.path.getsize(checkpoint) < 160_000_000:
        os.makedirs(os.path.dirname(checkpoint), exist_ok=True)
        url = "https://zenodo.org/record/4034264/files/CRNN_note_F1%3D0.9677_pedal_F1%3D0.9186.pth?download=1"
        if callback:
            callback("首次使用钢琴模型，正在下载约 165MB 权重...")
        temp_path = checkpoint + ".download"
        try:
            import urllib.request
            urllib.request.urlretrieve(url, temp_path)
            os.replace(temp_path, checkpoint)
        finally:
            if os.path.exists(temp_path) and os.path.getsize(temp_path) < 160_000_000:
                os.remove(temp_path)
    if cancel_event is not None and cancel_event.is_set():
        raise RuntimeError("任务已取消")
    if callback:
        callback("使用钢琴专用模型 piano_transcription_inference")
    audio, _ = librosa.load(audio_path, sr=sample_rate, mono=True)
    midi_path = os.path.join(out_dir, os.path.splitext(os.path.basename(audio_path))[0] + "_piano_transcription.mid")
    with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
        transcriptor = PianoTranscription(device=torch.device("cpu"))
        transcriptor.transcribe(audio, midi_path)
    if callback:
        callback("钢琴模型推理完成：" + midi_path)
    return midi_path


def transcribe_melody_crepe(audio_path, out_dir, min_note_ms=80, callback=None, cancel_event=None):
    try:
        import librosa
        import numpy as np
        import torch
        import torchcrepe
    except Exception as exc:
        raise RuntimeError("CREPE 未安装。请运行：uv pip install --python .venv-ai\\Scripts\\python.exe torchcrepe") from exc

    os.makedirs(out_dir, exist_ok=True)
    if callback:
        callback("使用 CREPE 单音主旋律模型")
    audio, _ = librosa.load(audio_path, sr=torchcrepe.SAMPLE_RATE, mono=True)
    audio_tensor = torch.from_numpy(audio).float().unsqueeze(0)
    pitch, periodicity = torchcrepe.predict(
        audio_tensor,
        torchcrepe.SAMPLE_RATE,
        hop_length=160,
        fmin=librosa.note_to_hz("C2"),
        fmax=librosa.note_to_hz("C6"),
        model="full",
        decoder=torchcrepe.decode.viterbi,
        return_periodicity=True,
        batch_size=512,
        device="cpu",
    )
    pitch = pitch.detach().cpu().numpy().reshape(-1)
    periodicity = periodicity.detach().cpu().numpy().reshape(-1)
    frame_seconds = 160 / float(torchcrepe.SAMPLE_RATE)
    notes = []
    current = None
    for index, frequency in enumerate(pitch):
        if cancel_event is not None and cancel_event.is_set():
            raise RuntimeError("任务已取消")
        valid = bool(np.isfinite(frequency)) and float(periodicity[index]) >= 0.40
        midi_note = int(round(librosa.hz_to_midi(float(frequency)))) if valid else None
        time_seconds = index * frame_seconds
        if midi_note is None:
            if current is not None:
                start, end, note = current
                if end - start >= min_note_ms / 1000.0:
                    notes.append((start, end, note, int(55 + 60 * float(periodicity[index - 1]))))
                current = None
            continue
        if current is None:
            current = [time_seconds, time_seconds, midi_note]
        elif abs(midi_note - current[2]) <= 0.6:
            current[1] = time_seconds
        else:
            start, end, note = current
            if end - start >= min_note_ms / 1000.0:
                notes.append((start, end, note, 85))
            current = [time_seconds, time_seconds, midi_note]
    if current is not None and current[1] - current[0] >= min_note_ms / 1000.0:
        notes.append((current[0], current[1], current[2], 85))
    if not notes:
        raise RuntimeError("CREPE 没有识别到稳定旋律音高")

    tpb = 480
    tempo = 500000
    ticks_per_second = tpb * 1e6 / tempo
    mid = mido.MidiFile(type=0, ticks_per_beat=tpb)
    track = mido.MidiTrack()
    track.append(mido.MetaMessage("set_tempo", tempo=tempo, time=0))
    events = []
    for start, end, note, velocity in notes:
        on_tick = int(round(start * ticks_per_second))
        off_tick = max(on_tick + 1, int(round(max(end, start + min_note_ms / 1000.0) * ticks_per_second)))
        events.append((on_tick, mido.Message("note_on", note=note, velocity=max(1, min(127, velocity)), time=0)))
        events.append((off_tick, mido.Message("note_off", note=note, velocity=0, time=0)))
    events.sort(key=lambda item: item[0])
    previous = 0
    for tick, msg in events:
        msg.time = tick - previous
        track.append(msg)
        previous = tick
    track.append(mido.MetaMessage("end_of_track", time=0))
    mid.tracks.append(track)
    out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(audio_path))[0] + "_crepe.mid")
    mid.save(out_path)
    if callback:
        callback("CREPE 扒谱完成：%d 个音符" % len(notes))
    return out_path


def transcribe_audio(audio_path, out_dir, onset_threshold=0.5, frame_threshold=0.3,
                     minimum_note_length=58, callback=None, cancel_event=None):
    os.makedirs(out_dir, exist_ok=True)
    before = set(glob.glob(os.path.join(out_dir, "**", "*"), recursive=True))
    exe = basic_pitch_path()
    if exe:
        cmd = [
            exe,
            out_dir,
            audio_path,
            "--onset-threshold", str(onset_threshold),
            "--frame-threshold", str(frame_threshold),
            "--minimum-note-length", str(minimum_note_length),
            "--save-midi",
        ]
        try:
            run_command(cmd, callback=callback, cancel_event=cancel_event)
        except Exception as exc:
            if importlib.util.find_spec("librosa"):
                if callback:
                    callback("basic-pitch 执行失败，改用内置旋律扒谱：%s" % exc)
                return transcribe_melody_pyin(audio_path, out_dir, callback=callback, cancel_event=cancel_event)
            raise
    else:
        try:
            from basic_pitch.inference import predict
        except Exception as exc:
            if importlib.util.find_spec("librosa"):
                if callback:
                    callback("未找到 basic-pitch，改用内置旋律扒谱")
                return transcribe_melody_pyin(audio_path, out_dir, callback=callback, cancel_event=cancel_event)
            raise RuntimeError(
                "未找到 basic-pitch 或 librosa。请安装：python -m pip install -U basic-pitch 或 python -m pip install -U librosa soundfile"
            ) from exc
        _, midi_data, _ = predict(
            audio_path,
            onset_threshold=onset_threshold,
            frame_threshold=frame_threshold,
            minimum_note_length=minimum_note_length,
        )
        out_path = os.path.join(out_dir, os.path.splitext(os.path.basename(audio_path))[0] + "_basic_pitch.mid")
        midi_data.write(out_path)

    files = _new_files(before, out_dir, [".mid", ".midi"])
    if not files:
        fallback = os.path.join(out_dir, os.path.splitext(os.path.basename(audio_path))[0] + "_basic_pitch.mid")
        if os.path.exists(fallback):
            return fallback
        raise RuntimeError("扒谱完成但没有找到 MIDI 文件")
    return files[-1]
