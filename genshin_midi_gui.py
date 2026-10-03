#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Tkinter desktop UI for the Genshin MIDI converter."""
import glob
import io
import os
import queue
import sys
import threading
from contextlib import redirect_stdout

import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from tkinter.scrolledtext import ScrolledText

import midi_to_genshin as mtg
import duration_preserving as dp
import melody_extract as me

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_OUTPUT = os.path.join(BASE_DIR, "输出_原神可用")


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


class GenshinMidiGui(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("原神琴谱转换器")
        self.geometry("1060x820")
        self.minsize(960, 720)
        self.configure(bg="#f3f6f8")

        self.msg_queue = queue.Queue()
        self.cancel_event = threading.Event()
        self.worker = None
        self._running = False

        self._build_style()
        self._build_ui()
        self._load_config(mtg.load_config(os.path.join(BASE_DIR, "config.json")))
        self.after(120, self._poll_queue)

    def _build_style(self):
        style = ttk.Style(self)
        try:
            style.theme_use("vista" if "vista" in style.theme_names() else "clam")
        except tk.TclError:
            pass
        style.configure(".", font=("Microsoft YaHei UI", 9), background="#f3f6f8")
        style.configure("TFrame", background="#f3f6f8")
        style.configure("Card.TLabelframe", background="#ffffff", borderwidth=1, relief="solid")
        style.configure("Card.TLabelframe.Label", background="#ffffff", foreground="#1f2937",
                        font=("Microsoft YaHei UI", 10, "bold"))
        style.configure("TLabel", background="#ffffff", foreground="#374151")
        style.configure("Hint.TLabel", foreground="#6b7280")
        style.configure("Title.TLabel", background="#f3f6f8", foreground="#111827",
                        font=("Microsoft YaHei UI", 18, "bold"))
        style.configure("Sub.TLabel", background="#f3f6f8", foreground="#6b7280")
        style.configure("Accent.TButton", font=("Microsoft YaHei UI", 10, "bold"))
        style.configure("Danger.TButton", foreground="#b91c1c")
        style.configure("TNotebook", background="#f3f6f8", borderwidth=0)
        style.configure("TNotebook.Tab", padding=(16, 8), font=("Microsoft YaHei UI", 9))

    def _build_ui(self):
        root = ttk.Frame(self, padding=(18, 14, 18, 14))
        root.pack(fill="both", expand=True)
        header = ttk.Frame(root)
        header.pack(fill="x", pady=(0, 10))
        ttk.Label(header, text="原神琴谱转换器", style="Title.TLabel").pack(side="left")
        ttk.Label(header, text="  单音化 · 音域折叠 · 防过密 · 批量转换", style="Sub.TLabel").pack(side="left", pady=(8, 0))

        notebook = ttk.Notebook(root)
        notebook.pack(fill="both", expand=True)
        self.tab_convert = ttk.Frame(notebook, padding=10)
        self.tab_help = ttk.Frame(notebook, padding=10)
        notebook.add(self.tab_convert, text="转换")
        notebook.add(self.tab_help, text="说明")
        self._build_convert_tab()
        self._build_help_tab()

    def _build_convert_tab(self):
        page = self.tab_convert
        page.columnconfigure(0, weight=1)

        io_card = ttk.LabelFrame(page, text="输入与输出", style="Card.TLabelframe", padding=12)
        io_card.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        io_card.columnconfigure(1, weight=1)

        self.input_var = tk.StringVar()
        self.output_var = tk.StringVar(value=DEFAULT_OUTPUT)
        self.track_var = tk.IntVar(value=0)

        ttk.Label(io_card, text="输入 MIDI").grid(row=0, column=0, sticky="w", padx=(0, 8), pady=4)
        ttk.Entry(io_card, textvariable=self.input_var).grid(row=0, column=1, sticky="ew", pady=4)
        ttk.Button(io_card, text="选择文件", command=self._choose_input_file).grid(row=0, column=2, padx=(8, 0), pady=4)
        ttk.Button(io_card, text="选择文件夹", command=self._choose_input_dir).grid(row=0, column=3, padx=(6, 0), pady=4)

        ttk.Label(io_card, text="输出文件夹").grid(row=1, column=0, sticky="w", padx=(0, 8), pady=4)
        ttk.Entry(io_card, textvariable=self.output_var).grid(row=1, column=1, sticky="ew", pady=4)
        ttk.Button(io_card, text="选择", command=self._choose_output_dir).grid(row=1, column=2, padx=(8, 0), pady=4)
        ttk.Button(io_card, text="打开", command=self._open_output_dir).grid(row=1, column=3, padx=(6, 0), pady=4)

        ttk.Label(io_card, text="处理音轨").grid(row=2, column=0, sticky="w", padx=(0, 8), pady=4)
        ttk.Spinbox(io_card, from_=0, to=32, textvariable=self.track_var, width=8).grid(row=2, column=1, sticky="w", pady=4)
        ttk.Label(io_card, text="0 = 全部轨道并自动去鼓；1 开始为指定音轨", style="Hint.TLabel").grid(row=2, column=1, columnspan=3, sticky="w", padx=(90, 0), pady=4)

        cfg_card = ttk.LabelFrame(page, text="转换参数", style="Card.TLabelframe", padding=12)
        cfg_card.grid(row=1, column=0, sticky="ew", pady=(0, 10))
        for c in range(4):
            cfg_card.columnconfigure(c, weight=1)

        self.low_var = tk.IntVar(value=48)
        self.high_var = tk.IntVar(value=83)
        self.transpose_var = tk.IntVar(value=0)
        self.chord_mode_var = tk.StringVar(value="arpeggio")
        self.arp_var = tk.IntVar(value=60)
        self.press_var = tk.IntVar(value=120)
        self.tempo_var = tk.DoubleVar(value=1.0)
        self.min_gap_var = tk.IntVar(value=60)
        self.window_var = tk.IntVar(value=30)
        self.quantize_var = tk.IntVar(value=0)
        self.auto_slow_var = tk.BooleanVar(value=True)
        self.strict_gap_var = tk.BooleanVar(value=True)
        self.preserve_duration_var = tk.BooleanVar(value=True)
        self.melody_only_var = tk.BooleanVar(value=True)

        self._spin(cfg_card, 0, 0, "最低音 MIDI", self.low_var, 24, 96)
        self._spin(cfg_card, 0, 1, "最高音 MIDI", self.high_var, 36, 108)
        self._spin(cfg_card, 0, 2, "整体移调", self.transpose_var, -24, 24)
        mode_cell = ttk.Frame(cfg_card)
        mode_cell.grid(row=0, column=3, sticky="ew", padx=6, pady=4)
        mode_cell.columnconfigure(0, weight=1)
        ttk.Label(mode_cell, text="和弦模式").grid(row=0, column=0, sticky="w")
        mode = ttk.Combobox(mode_cell, state="readonly", width=11, values=["arpeggio", "top"], textvariable=self.chord_mode_var)
        mode.grid(row=0, column=1, sticky="e", padx=(6, 0))

        self._spin(cfg_card, 1, 0, "琶音间隔 ms", self.arp_var, 10, 250)
        self._spin(cfg_card, 1, 1, "按键时长 ms", self.press_var, 20, 1000)
        self._spin(cfg_card, 1, 2, "放慢倍数", self.tempo_var, 0.5, 20.0, float)
        self._spin(cfg_card, 1, 3, "最短间隔 ms", self.min_gap_var, 20, 250)
        self._spin(cfg_card, 2, 0, "和弦窗口 ms", self.window_var, 0, 200)
        self._spin(cfg_card, 2, 1, "量化 ms", self.quantize_var, 0, 200)
        ttk.Checkbutton(cfg_card, text="自动放慢", variable=self.auto_slow_var).grid(row=2, column=2, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(cfg_card, text="严格保证按键间隔", variable=self.strict_gap_var).grid(row=2, column=3, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(cfg_card, text="只保留主旋律（推荐）", variable=self.melody_only_var).grid(
            row=3, column=0, columnspan=2, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(cfg_card, text="保持原曲时长（冲突音自动简化）", variable=self.preserve_duration_var).grid(
            row=3, column=2, columnspan=2, sticky="w", padx=6, pady=4)

        preset = ttk.Frame(page)
        preset.grid(row=2, column=0, sticky="ew", pady=(0, 10))
        ttk.Button(preset, text="纯旋律（推荐）", command=self._preset_strict).pack(side="left")
        ttk.Button(preset, text="和弦简化（保持时长）", command=self._preset_simple).pack(side="left", padx=8)
        ttk.Button(preset, text="零丢音（时长会变）", command=self._preset_zero_drop).pack(side="left")
        ttk.Button(preset, text="恢复 config.json", command=self._reset_config).pack(side="left")

        action = ttk.Frame(page)
        action.grid(row=3, column=0, sticky="ew", pady=(0, 8))
        self.preview_btn = ttk.Button(action, text="预览统计", command=lambda: self._start_job(True))
        self.preview_btn.pack(side="left")
        self.convert_btn = ttk.Button(action, text="开始转换", style="Accent.TButton", command=lambda: self._start_job(False))
        self.convert_btn.pack(side="left", padx=8)
        self.stop_btn = ttk.Button(action, text="停止", style="Danger.TButton", command=self._stop_job, state="disabled")
        self.stop_btn.pack(side="left")
        ttk.Button(action, text="打开输出文件夹", command=self._open_output_dir).pack(side="right")

        status = ttk.Frame(page)
        status.grid(row=4, column=0, sticky="ew")
        status.columnconfigure(0, weight=1)
        self.progress_var = tk.DoubleVar(value=0)
        ttk.Progressbar(status, variable=self.progress_var, maximum=100).grid(row=0, column=0, sticky="ew")
        self.status_var = tk.StringVar(value="就绪")
        ttk.Label(status, textvariable=self.status_var, style="Hint.TLabel").grid(row=1, column=0, sticky="w", pady=(4, 0))

        self.log = ScrolledText(page, height=12, wrap="word", font=("Consolas", 9), bg="#0f172a", fg="#dbeafe", insertbackground="#dbeafe")
        self.log.grid(row=5, column=0, sticky="nsew", pady=(8, 0))
        page.rowconfigure(5, weight=1)

    def _build_help_tab(self):
        text = ScrolledText(self.tab_help, wrap="word", font=("Microsoft YaHei UI", 10), padx=12, pady=12)
        text.pack(fill="both", expand=True)
        text.insert("1.0", """使用建议

1. 单首转换：点击“选择文件”，再点击“开始转换”。
2. 批量转换：输入框选择文件夹，程序会处理其中的 .mid/.midi 文件，并保留子目录结构。
3. 默认“只保留主旋律”：自动选择主旋律轨，单轨复音时使用 Viterbi 选音。
4. “保持原曲时长”不会改变全局速度；冲突音符会自动简化。
5. “严格保证按键间隔”会把琶音间隔自动提高到不低于最短间隔，推荐保持开启。
6. 需要更多和弦时，可切换到“和弦简化（保持时长）”。

输出格式

- 单轨 Type 0 MIDI
- 音域默认 C3-B5（MIDI 48-83）
- 已去鼓组、单音化、限制过密按键

当前优化目录

优化完成_原神可用 目录中的谱子使用严格参数生成：
音域 48-83、琶音间隔 60ms、最短间隔 60ms、自动放慢、无时间量化。
""")
        text.configure(state="disabled")

    def _spin(self, parent, row, col, label, var, lo, hi, cast=int):
        cell = ttk.Frame(parent)
        cell.grid(row=row, column=col, sticky="ew", padx=6, pady=4)
        cell.columnconfigure(0, weight=1)
        ttk.Label(cell, text=label).grid(row=0, column=0, sticky="w")
        widget = ttk.Spinbox(cell, from_=lo, to=hi, textvariable=var, width=10)
        if cast is float:
            widget.configure(increment=0.1)
        widget.grid(row=0, column=1, sticky="e", padx=(6, 0))

    def _load_config(self, cfg):
        self.low_var.set(cfg["range"]["low"])
        self.high_var.set(cfg["range"]["high"])
        self.transpose_var.set(cfg.get("transpose_semitones", 0))
        self.chord_mode_var.set(cfg.get("chord_mode", "arpeggio"))
        self.arp_var.set(cfg.get("arpeggio_gap_ms", 60))
        self.press_var.set(cfg.get("press_ms", 120))
        self.tempo_var.set(cfg.get("tempo_scale", 1.0))
        self.min_gap_var.set(cfg.get("min_gap_ms", 60))
        self.window_var.set(cfg.get("chord_window_ms", 30))
        self.quantize_var.set(cfg.get("quantize_ms", 0))
        self.auto_slow_var.set(bool(cfg.get("auto_slow", True)))
        self.strict_gap_var.set(True)
        self.preserve_duration_var.set(True)
        self.melody_only_var.set(True)

    def _preset_strict(self):
        self.low_var.set(48); self.high_var.set(83); self.transpose_var.set(0)
        self.chord_mode_var.set("arpeggio"); self.arp_var.set(60); self.press_var.set(120)
        self.tempo_var.set(1.0); self.min_gap_var.set(60); self.window_var.set(30)
        self.quantize_var.set(0); self.auto_slow_var.set(False); self.strict_gap_var.set(True)
        self.preserve_duration_var.set(True); self.melody_only_var.set(True)
        self._append_log("已应用：纯旋律 + 保持原曲时长")

    def _preset_simple(self):
        self.chord_mode_var.set("arpeggio"); self.arp_var.set(60); self.min_gap_var.set(60)
        self.auto_slow_var.set(False); self.strict_gap_var.set(True)
        self.preserve_duration_var.set(True); self.melody_only_var.set(False)
        self._append_log("已应用：和弦简化 + 保持原曲时长")

    def _preset_zero_drop(self):
        self.chord_mode_var.set("arpeggio"); self.arp_var.set(60); self.min_gap_var.set(60)
        self.auto_slow_var.set(True); self.strict_gap_var.set(True)
        self.preserve_duration_var.set(False); self.melody_only_var.set(False)
        self._append_log("已应用：零丢音（输出时长会变长）")

    def _reset_config(self):
        self._load_config(mtg.load_config(os.path.join(BASE_DIR, "config.json")))
        self._append_log("已恢复 config.json")

    def _choose_input_file(self):
        path = filedialog.askopenfilename(title="选择 MIDI 文件", filetypes=[("MIDI 文件", "*.mid *.midi"), ("所有文件", "*.*")])
        if path:
            self.input_var.set(path)

    def _choose_input_dir(self):
        path = filedialog.askdirectory(title="选择包含 MIDI 的文件夹")
        if path:
            self.input_var.set(path)

    def _choose_output_dir(self):
        path = filedialog.askdirectory(title="选择输出文件夹")
        if path:
            self.output_var.set(path)

    def _open_output_dir(self):
        path = self.output_var.get().strip() or DEFAULT_OUTPUT
        try:
            os.makedirs(path, exist_ok=True)
            os.startfile(path)
        except Exception as exc:
            messagebox.showerror("打开失败", str(exc))

    def _current_config(self):
        cfg = {
            "range": {"low": int(self.low_var.get()), "high": int(self.high_var.get())},
            "transpose_semitones": int(self.transpose_var.get()),
            "chord_mode": self.chord_mode_var.get(),
            "arpeggio_gap_ms": int(self.arp_var.get()),
            "press_ms": int(self.press_var.get()),
            "tempo_scale": float(self.tempo_var.get()),
            "min_gap_ms": int(self.min_gap_var.get()),
            "chord_window_ms": int(self.window_var.get()),
            "auto_slow": bool(self.auto_slow_var.get()),
            "quantize_ms": int(self.quantize_var.get()),
            "strict_gap": bool(self.strict_gap_var.get()),
            "preserve_duration": bool(self.preserve_duration_var.get()),
            "melody_only": bool(self.melody_only_var.get()),
            "collapse_window_ms": 10,
        }
        if cfg["range"]["low"] >= cfg["range"]["high"]:
            raise ValueError("最低音必须小于最高音")
        if cfg["range"]["high"] - cfg["range"]["low"] + 1 > 96:
            raise ValueError("音域跨度太大")
        for key in ("arpeggio_gap_ms", "press_ms", "min_gap_ms"):
            if cfg[key] <= 0:
                raise ValueError("%s 必须大于 0" % key)
        if cfg["tempo_scale"] <= 0:
            raise ValueError("放慢倍数必须大于 0")
        if self.strict_gap_var.get():
            cfg["arpeggio_gap_ms"] = max(cfg["arpeggio_gap_ms"], cfg["min_gap_ms"])
        return cfg

    def _collect_tasks(self):
        source = self.input_var.get().strip().strip('"')
        if os.path.isfile(source):
            return [(source, "")], os.path.dirname(source)
        if not os.path.isdir(source):
            raise ValueError("请选择有效的 MIDI 文件或文件夹")
        tasks = []
        for root, _, names in os.walk(source):
            for name in sorted(names):
                if name.lower().endswith((".mid", ".midi")):
                    rel_dir = os.path.relpath(root, source)
                    tasks.append((os.path.join(root, name), "" if rel_dir == "." else rel_dir))
        return tasks, source

    def _start_job(self, preview):
        if self._running:
            return
        try:
            tasks, _ = self._collect_tasks()
            cfg = self._current_config()
            out_dir = self.output_var.get().strip().strip('"') or DEFAULT_OUTPUT
        except Exception as exc:
            messagebox.showerror("参数错误", str(exc))
            return
        if not tasks:
            messagebox.showwarning("没有文件", "没有找到 .mid 或 .midi 文件")
            return
        self.cancel_event.clear()
        self._set_running(True)
        self.progress_var.set(0)
        self._append_log("=" * 60)
        self._append_log(("预览 " if preview else "转换 ") + "%d 个文件" % len(tasks))
        self.worker = threading.Thread(target=self._worker_run, args=(tasks, cfg, out_dir, preview), daemon=True)
        self.worker.start()

    def _worker_run(self, tasks, cfg, out_dir, preview):
        ok = failed = 0
        total_notes = total_actual = total_range = total_dense = 0
        max_scale = 1.0
        last_output = ""
        for i, (path, rel_dir) in enumerate(tasks, 1):
            if self.cancel_event.is_set():
                break
            dest_dir = os.path.join(out_dir, rel_dir) if rel_dir else out_dir
            self.msg_queue.put(("log", "[%d/%d] %s" % (i, len(tasks), os.path.basename(path))))
            try:
                row = convert_one_file(path, cfg, dest_dir, int(self.track_var.get()), not preview)
                ok += 1
                total_notes += row["notes"]
                total_actual += row["actual"]
                total_range += row["dropped_range"]
                total_dense += row["dropped_dense"]
                max_scale = max(max_scale, row["scale"])
                last_output = row["output"] or dest_dir
                extra = ""
                if "duration_ratio" in row:
                    extra = "，时长 %.3fx" % row["duration_ratio"]
                if "selected_track" in row:
                    extra += "，音轨 %d/%d，%s" % (row["selected_track"], row["track_count"], row.get("method", ""))
                self.msg_queue.put(("log", "  完成：%d 个音符，简化/丢弃 %d/%d%s，放慢 %.2fx，范围 %s-%s" % (
                    row["actual"], row["dropped_range"], row["dropped_dense"], extra, row["scale"], row["low"], row["high"])))
            except Exception as exc:
                failed += 1
                self.msg_queue.put(("log", "  失败：%s: %s" % (type(exc).__name__, exc)))
            self.msg_queue.put(("progress", i * 100.0 / len(tasks)))
        self.msg_queue.put(("done", {
            "ok": ok, "failed": failed, "preview": preview,
            "notes": total_notes, "actual": total_actual,
            "dropped_range": total_range, "dropped_dense": total_dense,
            "max_scale": max_scale, "last_output": last_output,
            "canceled": self.cancel_event.is_set(),
        }))

    def _set_running(self, running):
        self._running = running
        state = "disabled" if running else "normal"
        self.preview_btn.configure(state=state)
        self.convert_btn.configure(state=state)
        self.stop_btn.configure(state="normal" if running else "disabled")

    def _stop_job(self):
        self.cancel_event.set()
        self.status_var.set("正在停止...")

    def _append_log(self, text):
        self.log.insert("end", text + "\n")
        self.log.see("end")

    def _poll_queue(self):
        try:
            while True:
                kind, payload = self.msg_queue.get_nowait()
                if kind == "log":
                    self._append_log(payload)
                elif kind == "progress":
                    self.progress_var.set(payload)
                elif kind == "done":
                    self._finish_job(payload)
        except queue.Empty:
            pass
        self.after(120, self._poll_queue)

    def _finish_job(self, result):
        self._set_running(False)
        status = "完成" if not result["canceled"] else "已停止"
        self.status_var.set("%s：成功 %d，失败 %d，总音符 %d，最大放慢 %.2fx" % (
            status, result["ok"], result["failed"], result["actual"], result["max_scale"]))
        self._append_log("-" * 60)
        self._append_log(self.status_var.get())
        if not result["preview"] and result["ok"]:
            self._append_log("输出：%s" % result["last_output"])
        if result["failed"] and not result["canceled"]:
            messagebox.showwarning("部分失败", "有 %d 个文件转换失败，请查看日志。" % result["failed"])
        elif result["ok"] and not result["preview"]:
            messagebox.showinfo("转换完成", "已成功转换 %d 个文件。" % result["ok"])


def main():
    app = GenshinMidiGui()
    app.mainloop()


if __name__ == "__main__":
    main()
