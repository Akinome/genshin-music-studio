#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
原神专用 MIDI 转换器
=====================
把 AI 扒谱出来的 MIDI 转成「原神能 100% 演奏」的 MIDI 文件，喂给你的自动演奏工具：

  * 去掉鼓组音轨（打击乐通道）
  * 把音符八度折叠/移调到风物之诗琴音域（默认 C3-B5）
  * 和弦自动拆成琶音（游戏内一次只能弹一个音）
  * 按最小按键间隔自动放慢速度（避免音符过密按不过来）
  * 输出为单轨标准 MIDI（type 0），兼容绝大多数自动演奏工具

用法:
  python midi_to_genshin.py 输入.mid
  python midi_to_genshin.py 输入.mid -o 输出.mid
  python midi_to_genshin.py 输入.mid --tempo 1.2        # 手动放慢 1.2 倍
  python midi_to_genshin.py 输入.mid --track 2          # 只用第 2 条音轨
  python midi_to_genshin.py 输入.mid --preview          # 只看处理结果摘要，不生成文件
  python midi_to_genshin.py --init                      # 生成/刷新 config.json

首次使用: 先运行 python midi_to_genshin.py --init 生成配置，再转换。
"""

import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass
import argparse
import json
import os
import sys

try:
    import mido
except ImportError:
    sys.exit("缺少 mido 库，请先安装: pip install mido")

NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
DEFAULT_TEMPO = 500000  # 120 BPM

DEFAULT_CONFIG = {
    "range": {"low": 48, "high": 83},  # MIDI 音号: 48=C3, 83=B5（风物之诗琴音域）
    "transpose_semitones": 0,          # 整体移调（半音）
    "chord_mode": "arpeggio",          # arpeggio=和弦拆琶音  top=只留最高音
    "arpeggio_gap_ms": 25,             # 琶音里相邻音符间隔（毫秒）
    "press_ms": 120,                   # 输出音符的持续时长（毫秒）
    "tempo_scale": 1.0,                # 放慢倍数，>1 变慢
    "min_gap_ms": 60,                  # 允许的最短相邻按键间隔（毫秒）
    "chord_window_ms": 30,             # 判定为同一个和弦的时间窗（毫秒）
    "auto_slow": True,                 # 间隔不够时自动放慢（保证 100% 可演奏）
    "quantize_ms": 0,                  # 时间量化网格（毫秒），0=关闭；对 AI 扒谱的抖动有用
}


def note_name(n):
    return "%s%d" % (NOTE_NAMES[n % 12], n // 12 - 1)


def default_config():
    return json.loads(json.dumps(DEFAULT_CONFIG))


def load_config(path):
    cfg = default_config()
    if path and os.path.exists(path):
        try:
            with open(path, "r", encoding="utf-8") as f:
                user = json.load(f)
            for k, v in user.items():
                cfg[k] = v
        except Exception as e:
            print("[警告] 读取配置失败，使用默认配置: %s" % e)
    if cfg["range"]["high"] - cfg["range"]["low"] + 1 > 96:
        sys.exit("音域跨度太大，请检查 config.json 的 range")
    return cfg


def write_default_config(path):
    cfg = default_config()
    with open(path, "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)
    print("已生成 %s" % path)


# ---------------------------------------------------------------------------
# 读取 MIDI
# ---------------------------------------------------------------------------

def load_notes(midi_path, track_index=None):
    """返回 (notes, tpb, first_tempo, tempo_set)。
    notes: [(原始tick, 原始秒, MIDI音号, 力度)]，按时间排序，已跳过鼓组。
    """
    mid = mido.MidiFile(midi_path)
    if mid.type == 2 and track_index is None:
        raise ValueError(
            "暂不支持 type-2（多条不同步音轨）MIDI，请先用 MuseScore/FL Studio 导出为标准 MIDI"
        )
    if track_index is not None:
        if not (1 <= track_index <= len(mid.tracks)):
            raise ValueError("音轨编号超出范围（该文件共 %d 条音轨）" % len(mid.tracks))
        tracks = [mid.tracks[track_index - 1]]
    else:
        tracks = mid.tracks

    merged = mido.merge_tracks(tracks)
    tempo = DEFAULT_TEMPO
    first_tempo = None
    tempo_set = set()
    abs_tick = 0
    onsets = {}   # note -> [(tick, sec, vel)]
    notes = []    # (tick, sec, note, vel, 时长秒)
    for msg in merged:
        abs_tick += msg.time
        if msg.is_meta:
            if msg.type == "set_tempo":
                tempo = msg.tempo
                tempo_set.add(msg.tempo)
                if first_tempo is None:
                    first_tempo = msg.tempo
            continue
        if msg.type == "note_on" and msg.velocity > 0 and msg.channel != 9:
            sec = mido.tick2second(abs_tick, mid.ticks_per_beat, tempo)
            onsets.setdefault(msg.note, []).append((abs_tick, sec, msg.velocity))
        elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
            lst = onsets.get(msg.note)
            if lst:
                t0, s0, v0 = lst.pop(0)
                sec = mido.tick2second(abs_tick, mid.ticks_per_beat, tempo)
                notes.append((t0, s0, msg.note, v0, sec - s0))
    # 没有 note_off 的孤儿音符：给一个短默认时长（避免误判成和弦）
    for note, lst in onsets.items():
        for t0, s0, v0 in lst:
            notes.append((t0, s0, note, v0, 0.05))
    notes.sort(key=lambda x: x[0])
    if first_tempo is None:
        first_tempo = DEFAULT_TEMPO
    return notes, mid.ticks_per_beat, first_tempo, tempo_set


# ---------------------------------------------------------------------------
# 音域折叠 / 和弦拆分 / 密度控制
# ---------------------------------------------------------------------------

def normalize(notes, cfg):
    """八度折叠到琴音域 + 可选整体移调。"""
    low = cfg["range"]["low"]
    high = cfg["range"]["high"]
    transpose = cfg.get("transpose_semitones", 0)
    out = []
    dropped_range = 0
    for tick, sec, note, vel, dur in notes:
        n = note + transpose
        while n < low:
            n += 12
        while n > high:
            n -= 12
        if n < low or n > high:
            dropped_range += 1
            continue
        out.append((tick, sec, n, vel, dur))
    return out, dropped_range


def plan(strikes, cfg, tpb, first_tempo):
    """和弦拆分。返回 (events, smallest_inter_sec)。
    events: [(tick, 音号, 力度, 组号)]，组内已按琶音展开。
    """
    chord_win = cfg["chord_window_ms"] / 1000.0
    arp_gap_ticks = max(1, round(cfg["arpeggio_gap_ms"] / 1000.0 * tpb * 1e6 / first_tempo))
    mode = cfg["chord_mode"]

    groups = []
    cur = []
    start = None
    end = None
    for tick, sec, note, vel, dur in strikes:
        if start is None or (sec - start <= chord_win and sec < end):
            cur.append((tick, sec, note, vel, dur))
            if start is None:
                start = sec
                end = sec + dur
            else:
                end = max(end, sec + dur)
        else:
            groups.append(cur)
            cur = [(tick, sec, note, vel, dur)]
            start = sec
            end = sec + dur
    if cur:
        groups.append(cur)

    events = []
    for gi, g in enumerate(groups):
        base_tick = g[0][0]
        notes = sorted(g, key=lambda x: x[2])  # 低 -> 高
        if mode == "top":
            notes = notes[-1:]
        for i, (tick, sec, note, vel, dur) in enumerate(notes):
            events.append((base_tick + i * arp_gap_ticks, note, vel, gi))
    events.sort(key=lambda x: (x[0], x[1]))

    smallest_inter = None
    for a, b in zip(events, events[1:]):
        if a[3] != b[3]:
            gap_sec = (b[0] - a[0]) * first_tempo / (tpb * 1e6)
            if smallest_inter is None or gap_sec < smallest_inter:
                smallest_inter = gap_sec
    return events, smallest_inter


def compute_scale(cfg, smallest_inter_sec):
    scale = cfg.get("tempo_scale", 1.0) or 1.0
    min_gap = cfg["min_gap_ms"] / 1000.0
    if cfg.get("auto_slow", True) and smallest_inter_sec is not None:
        if smallest_inter_sec * scale < min_gap:
            scale = min_gap / smallest_inter_sec * 1.02
    return min(scale, 20.0)


def dense_filter(events, min_gap_ticks):
    """不同和弦之间太密则丢弃。返回 (final_presses, dropped)。"""
    final = []
    last_tick = None
    last_group = None
    dropped = 0
    for tick, note, vel, gi in events:
        if last_tick is not None and gi != last_group and tick - last_tick < min_gap_ticks:
            dropped += 1
            continue
        final.append((tick, note, vel))
        last_tick = tick
        last_group = gi
    return final, dropped


# ---------------------------------------------------------------------------
# 写出原神专用 MIDI
# ---------------------------------------------------------------------------

def write_output(presses, out_path, tpb, first_tempo, scale, cfg, tempo_set):
    tempo_out = int(round(first_tempo * scale))
    press_ticks = max(1, round(cfg["press_ms"] / 1000.0 * tpb * 1e6 / tempo_out))
    quantize = cfg.get("quantize_ms", 0)
    q_ticks = round(quantize / 1000.0 * tpb * 1e6 / tempo_out) if quantize > 0 else 0

    if tempo_set and len(tempo_set) > 1:
        print("[提示] 原 MIDI 存在变速，已统一为 %.0f BPM" % (60e6 / tempo_out))

    events = [(0, mido.MetaMessage("set_tempo", tempo=tempo_out, time=0))]
    ordered = sorted(presses, key=lambda x: x[0])
    for i, (tick, note, vel) in enumerate(ordered):
        on_tick = tick
        if q_ticks:
            on_tick = int(round(on_tick / q_ticks) * q_ticks)
        off_tick = on_tick + press_ticks
        if i + 1 < len(ordered):
            off_tick = min(off_tick, ordered[i + 1][0] - 1)
        off_tick = max(off_tick, on_tick + 1)
        events.append((on_tick, mido.Message("note_on", note=note, velocity=max(1, min(127, vel)), time=0)))
        events.append((off_tick, mido.Message("note_off", note=note, velocity=0, time=0)))

    events.sort(key=lambda x: x[0])
    mid = mido.MidiFile()
    mid.type = 0
    mid.ticks_per_beat = tpb
    track = mido.MidiTrack()
    prev = 0
    for tick, msg in events:
        msg.time = tick - prev
        track.append(msg)
        prev = tick
    track.append(mido.MetaMessage("end_of_track", time=0))
    mid.tracks.append(track)
    mid.save(out_path)


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="原神专用 MIDI 转换器")
    parser.add_argument("midi", nargs="?", help="输入的 MIDI 文件路径")
    parser.add_argument("-o", "--output", help="输出 MIDI 路径（默认 输入名_genshin.mid）")
    parser.add_argument("--init", action="store_true", help="生成/刷新 config.json 并退出")
    parser.add_argument("--preview", action="store_true", help="只打印结果摘要，不生成文件")
    parser.add_argument("--tempo", type=float, help="放慢倍数，如 1.2")
    parser.add_argument("--track", type=int, help="只使用第 N 条音轨（1 开始）")
    parser.add_argument("--chord-mode", choices=["arpeggio", "top"], help="和弦处理方式")
    parser.add_argument("--transpose", type=int, help="整体移调半音数")
    parser.add_argument("--no-auto-slow", action="store_true", help="关闭自动放慢")
    parser.add_argument("--config", default="config.json", help="配置文件路径")
    args = parser.parse_args()

    if args.init:
        write_default_config(args.config)
        return
    if not args.midi:
        parser.print_help()
        return
    if not os.path.exists(args.midi):
        sys.exit("找不到文件: %s" % args.midi)

    cfg = load_config(args.config)
    if args.tempo:
        cfg["tempo_scale"] = args.tempo
    if args.chord_mode:
        cfg["chord_mode"] = args.chord_mode
    if args.transpose is not None:
        cfg["transpose_semitones"] = args.transpose
    if args.no_auto_slow:
        cfg["auto_slow"] = False

    notes, tpb, first_tempo, tempo_set = load_notes(args.midi, args.track)
    if not notes:
        sys.exit("MIDI 中没有找到可演奏的音符（可能全是鼓组或空音轨）")

    strikes, dropped_range = normalize(notes, cfg)
    events, smallest_inter = plan(strikes, cfg, tpb, first_tempo)
    scale = compute_scale(cfg, smallest_inter)

    min_gap_ticks = max(1, round(cfg["min_gap_ms"] / 1000.0 * tpb * 1e6 / (first_tempo * scale)))
    final_presses, dropped_dense = dense_filter(events, min_gap_ticks)

    low = min((n for _, _, n, _, _ in strikes), default=0)
    high = max((n for _, _, n, _, _ in strikes), default=0)
    total_sec = (final_presses[-1][0] if final_presses else 0) * first_tempo * scale / (tpb * 1e6)

    print("=" * 52)
    print("输入      : %s" % os.path.basename(args.midi))
    print("音轨      : %s" % ("全部(去鼓)" if not args.track else "第 %d 轨" % args.track))
    print("音符总数  : %d" % len(notes))
    print("音域折叠丢弃: %d" % dropped_range)
    print("过密丢弃  : %d" % dropped_dense)
    print("实际音符  : %d" % len(final_presses))
    print("实际音域  : %s - %s" % (note_name(low), note_name(high)))
    print("速度      : %.2f 倍（输出 BPM %.0f）" % (scale, 60e6 / (first_tempo * scale)))
    print("预计时长  : %.1f 秒" % total_sec)
    print("=" * 52)

    if args.preview:
        return
    out_path = args.output or os.path.splitext(args.midi)[0] + "_genshin.mid"
    write_output(final_presses, out_path, tpb, first_tempo, scale, cfg, tempo_set)
    print("已生成原神可用 MIDI: %s" % out_path)
    print("直接把这个文件喂给你的自动演奏工具即可。")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n已中止")
        sys.exit(130)
