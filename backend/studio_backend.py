#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""JSON-lines backend used by the WinUI 3 shell."""
import json
import os
import sys

import media_tools
import studio_pipeline
import model_evaluator

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def emit(event_type, **data):
    payload = {"type": event_type}
    payload.update(data)
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def log(text):
    emit("log", text=str(text))


def build_config(request):
    return studio_pipeline.conversion_config(
        min_gap_ms=int(request.get("min_gap_ms", 60)),
        melody_only=bool(request.get("melody_only", True)),
        preserve_duration=bool(request.get("preserve_duration", True)),
    )


def handle(request):
    action = request.get("action")
    if action == "status":
        emit("done", dependencies=media_tools.dependency_status(), output="")
        return

    if action == "evaluate":
        reference = request.get("reference_path", "")
        prediction = request.get("prediction_path", "")
        metrics = model_evaluator.evaluate(reference, prediction)
        emit("done", dependencies={key: str(value) for key, value in metrics.items()})
        return

    if action == "install_ai":
        python_path = media_tools.install_ai_environment(
            callback=log,
            targets=request.get("install_targets") or None,
        )
        emit("done", output=python_path, dependencies=media_tools.dependency_status())
        return

    if action in ("download_audio", "download_video"):
        mode = "audio" if action == "download_audio" else "video"
        work = request.get("work_dir") or os.path.join(REPO_ROOT, "工作区")
        files = []
        urls = request.get("urls") or []
        for index, url in enumerate(urls, 1):
            emit("status", text="下载 %d/%d" % (index, len(urls)))
            files.extend(media_tools.download_media(
                url,
                os.path.join(work, "downloads"),
                mode=mode,
                cookies_file=request.get("cookies_file", ""),
                browser=request.get("browser", ""),
                allow_playlist=bool(request.get("allow_playlist", False)),
                callback=log,
            ))
            emit("progress", value=index * 100.0 / max(1, len(urls)))
        emit("done", output=os.path.join(work, "downloads"), files=files)
        return

    if action == "full_pipeline":
        work = request.get("work_dir") or os.path.join(REPO_ROOT, "工作区")
        out_dir = request.get("out_dir") or os.path.join(REPO_ROOT, "AI扒谱输出")
        cfg = build_config(request)
        model = request.get("model", "basic_pitch_onnx")
        results = []
        urls = request.get("urls") or []
        for index, url in enumerate(urls, 1):
            emit("status", text="完整处理 %d/%d" % (index, len(urls)))
            results.append(studio_pipeline.process_url(
                url,
                work,
                out_dir,
                cfg,
                cookies_file=request.get("cookies_file", ""),
                browser=request.get("browser", ""),
                allow_playlist=bool(request.get("allow_playlist", False)),
                convert=True,
                model=model,
                callback=log,
            ))
            emit("progress", value=index * 100.0 / max(1, len(urls)))
        emit("done", output=out_dir, results=results)
        return

    if action == "transcribe_url":
        work = request.get("work_dir") or os.path.join(REPO_ROOT, "工作区")
        out_dir = request.get("out_dir") or os.path.join(REPO_ROOT, "AI扒谱输出")
        cfg = build_config(request)
        model = request.get("model", "basic_pitch_onnx")
        results = []
        urls = request.get("urls") or []
        for index, url in enumerate(urls, 1):
            emit("status", text="仅扒谱 %d/%d" % (index, len(urls)))
            results.append(studio_pipeline.process_url(
                url,
                work,
                out_dir,
                cfg,
                cookies_file=request.get("cookies_file", ""),
                browser=request.get("browser", ""),
                allow_playlist=bool(request.get("allow_playlist", False)),
                convert=False,
                model=model,
                midi_dir=out_dir,
                callback=log,
            ))
            emit("progress", value=index * 100.0 / max(1, len(urls)))
        emit("done", output=out_dir, results=results)
        return

    if action == "local_full":
        path = request.get("path", "")
        out_dir = request.get("out_dir") or os.path.join(REPO_ROOT, "本地处理输出")
        cfg = build_config(request)
        model = request.get("model", "basic_pitch_onnx")
        if path.lower().endswith((".mid", ".midi")):
            result = studio_pipeline.process_midi_file(path, out_dir, cfg)
        else:
            result = studio_pipeline.process_media_file(
                path,
                os.path.join(out_dir, "中间文件"),
                out_dir,
                cfg,
                extract_audio=True,
                convert=True,
                model=model,
                callback=log,
            )
        emit("done", output=out_dir, result=result)
        return

    if action == "transcribe":
        path = request.get("path", "")
        out_dir = request.get("out_dir") or os.path.join(REPO_ROOT, "本地处理输出", "扒谱MIDI")
        if path.lower().endswith((".mid", ".midi")):
            midi = path
        else:
            audio = path
            if not path.lower().endswith((".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm")):
                audio = media_tools.extract_audio(path, out_dir, callback=log)
            midi = studio_pipeline.transcribe_with_model(
                audio,
                os.path.dirname(out_dir),
                request.get("model", "basic_pitch_onnx"),
                callback=log,
                midi_dir=out_dir,
            )
        emit("done", output=out_dir, midi=midi)
        return

    if action == "convert_midi":
        path = request.get("path", "")
        out_dir = request.get("out_dir") or os.path.join(REPO_ROOT, "本地处理输出")
        result = studio_pipeline.process_midi_file(path, out_dir, build_config(request))
        emit("done", output=out_dir, result=result)
        return

    raise ValueError("未知操作: %s" % action)


def main():
    try:
        request = json.loads(sys.stdin.read() or "{}")
        handle(request)
    except Exception as exc:
        emit("error", text="%s: %s" % (type(exc).__name__, exc))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
