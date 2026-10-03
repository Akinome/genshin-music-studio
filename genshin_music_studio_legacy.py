#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Genshin music studio: download, AI transcription, and MIDI conversion."""
import os
import queue
import sys
import threading
from datetime import datetime

import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from tkinter.scrolledtext import ScrolledText

import media_tools
import studio_pipeline

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WORK = os.path.join(BASE_DIR, "工作区")
DEFAULT_OUTPUT = os.path.join(BASE_DIR, "AI扒谱输出")
INSTALL_COMMAND = (
    "uv venv --python 3.11 .venv-ai\n"
    "uv pip install --python .venv-ai\\Scripts\\python.exe -U basic-pitch yt-dlp librosa soundfile \"setuptools<81\""
)


class GenshinMusicStudio(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("原神音乐工作台 - 下载 / AI扒谱 / 转换")
        self.geometry("1180x900")
        self.minsize(1020, 760)
        self.configure(bg="#f4f7f9")
        self.msg_queue = queue.Queue()
        self.cancel_event = threading.Event()
        self.worker = None
        self.running = False
        self.action_buttons = []
        self._build_style()
        self._build_ui()
        self._refresh_dependencies()
        self._refresh_library()
        self.after(120, self._poll_queue)

    def _build_style(self):
        style = ttk.Style(self)
        try:
            style.theme_use("vista" if "vista" in style.theme_names() else "clam")
        except tk.TclError:
            pass
        style.configure(".", font=("Microsoft YaHei UI", 9), background="#f4f7f9")
        style.configure("TFrame", background="#f4f7f9")
        style.configure("Card.TLabelframe", background="#ffffff", relief="solid", borderwidth=1)
        style.configure("Card.TLabelframe.Label", background="#ffffff", foreground="#1f2937",
                        font=("Microsoft YaHei UI", 10, "bold"))
        style.configure("TLabel", background="#ffffff", foreground="#374151")
        style.configure("Title.TLabel", background="#f4f7f9", foreground="#111827",
                        font=("Microsoft YaHei UI", 18, "bold"))
        style.configure("Sub.TLabel", background="#f4f7f9", foreground="#64748b")
        style.configure("Accent.TButton", font=("Microsoft YaHei UI", 10, "bold"))
        style.configure("TNotebook.Tab", padding=(16, 8))

    def _build_ui(self):
        root = ttk.Frame(self, padding=(16, 12, 16, 12))
        root.pack(fill="both", expand=True)
        header = ttk.Frame(root)
        header.pack(fill="x", pady=(0, 8))
        ttk.Label(header, text="原神音乐工作台", style="Title.TLabel").pack(side="left")
        ttk.Label(header, text="  视频下载 · AI扒谱 · 主旋律提取 · 原神MIDI", style="Sub.TLabel").pack(side="left", pady=(8, 0))

        notebook = ttk.Notebook(root)
        notebook.pack(fill="both", expand=True)
        self.tab_pipeline = ttk.Frame(notebook, padding=10)
        self.tab_local = ttk.Frame(notebook, padding=10)
        self.tab_env = ttk.Frame(notebook, padding=10)
        self.tab_library = ttk.Frame(notebook, padding=10)
        notebook.add(self.tab_pipeline, text="链接一键处理")
        notebook.add(self.tab_local, text="本地文件")
        notebook.add(self.tab_env, text="环境与安装")
        notebook.add(self.tab_library, text="谱库")
        self._build_pipeline_tab()
        self._build_local_tab()
        self._build_env_tab()
        self._build_library_tab()
        self._build_footer(root)

    def _card(self, parent, title, row):
        card = ttk.LabelFrame(parent, text=title, style="Card.TLabelframe", padding=10)
        card.grid(row=row, column=0, sticky="ew", pady=(0, 8))
        parent.columnconfigure(0, weight=1)
        return card

    def _build_pipeline_tab(self):
        page = self.tab_pipeline
        page.rowconfigure(4, weight=1)
        card = self._card(page, "视频链接", 0)
        card.columnconfigure(0, weight=1)
        self.url_text = ScrolledText(card, height=6, wrap="word", font=("Microsoft YaHei UI", 9))
        self.url_text.grid(row=0, column=0, sticky="nsew")
        ttk.Label(card, text="每行一个链接，支持 yt-dlp 能处理的网站，例如 Bilibili / YouTube。", foreground="#64748b").grid(
            row=1, column=0, sticky="w", pady=(4, 0))

        dirs = self._card(page, "工作目录", 1)
        dirs.columnconfigure(1, weight=1)
        self.work_var = tk.StringVar(value=DEFAULT_WORK)
        self.pipeline_out_var = tk.StringVar(value=DEFAULT_OUTPUT)
        ttk.Label(dirs, text="下载/中间文件").grid(row=0, column=0, sticky="w", padx=(0, 8), pady=3)
        ttk.Entry(dirs, textvariable=self.work_var).grid(row=0, column=1, sticky="ew", pady=3)
        ttk.Button(dirs, text="选择", command=self._choose_work).grid(row=0, column=2, padx=(6, 0))
        ttk.Button(dirs, text="打开", command=lambda: self._open_path(self.work_var.get())).grid(row=0, column=3, padx=(6, 0))
        ttk.Label(dirs, text="原神MIDI输出").grid(row=1, column=0, sticky="w", padx=(0, 8), pady=3)
        ttk.Entry(dirs, textvariable=self.pipeline_out_var).grid(row=1, column=1, sticky="ew", pady=3)
        ttk.Button(dirs, text="选择", command=self._choose_pipeline_out).grid(row=1, column=2, padx=(6, 0))
        ttk.Button(dirs, text="打开", command=lambda: self._open_path(self.pipeline_out_var.get())).grid(row=1, column=3, padx=(6, 0))

        opt = self._card(page, "下载与转换选项", 2)
        opt.columnconfigure(0, weight=3)
        opt.columnconfigure(2, weight=2)
        self.cookies_var = tk.StringVar()
        self.browser_var = tk.StringVar(value="不使用")
        self.playlist_var = tk.BooleanVar(value=False)
        self.melody_var = tk.BooleanVar(value=True)
        self.preserve_var = tk.BooleanVar(value=True)
        self.min_gap_var = tk.IntVar(value=60)

        download_box = ttk.Frame(opt)
        download_box.grid(row=0, column=0, sticky="nsew", padx=(0, 12))
        download_box.columnconfigure(1, weight=1)
        ttk.Label(download_box, text="下载选项", font=("Microsoft YaHei UI", 10, "bold")).grid(
            row=0, column=0, columnspan=3, sticky="w", pady=(0, 6))
        ttk.Label(download_box, text="Cookies 文件").grid(row=1, column=0, sticky="w", padx=(0, 8), pady=3)
        ttk.Entry(download_box, textvariable=self.cookies_var).grid(row=1, column=1, sticky="ew", pady=3)
        ttk.Button(download_box, text="选择", command=self._choose_cookies).grid(row=1, column=2, padx=(8, 0), pady=3)
        ttk.Label(download_box, text="浏览器 Cookies").grid(row=2, column=0, sticky="w", padx=(0, 8), pady=3)
        ttk.Combobox(
            download_box, state="readonly", width=14,
            values=["不使用", "chrome", "edge", "firefox"], textvariable=self.browser_var,
        ).grid(row=2, column=1, sticky="w", pady=3)
        ttk.Checkbutton(download_box, text="下载整个播放列表", variable=self.playlist_var).grid(
            row=3, column=0, columnspan=3, sticky="w", pady=(6, 0))

        ttk.Separator(opt, orient="vertical").grid(row=0, column=1, sticky="ns", padx=4)

        convert_box = ttk.Frame(opt)
        convert_box.grid(row=0, column=2, sticky="nsew", padx=(12, 0))
        convert_box.columnconfigure(1, weight=1)
        ttk.Label(convert_box, text="转换选项", font=("Microsoft YaHei UI", 10, "bold")).grid(
            row=0, column=0, columnspan=2, sticky="w", pady=(0, 6))
        ttk.Checkbutton(convert_box, text="只保留主旋律", variable=self.melody_var).grid(
            row=1, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Checkbutton(convert_box, text="保持原曲时长", variable=self.preserve_var).grid(
            row=2, column=0, columnspan=2, sticky="w", pady=3)
        ttk.Label(convert_box, text="最短按键间隔 (ms)").grid(row=3, column=0, sticky="w", padx=(0, 8), pady=(6, 0))
        ttk.Spinbox(convert_box, from_=20, to=200, textvariable=self.min_gap_var, width=8).grid(
            row=3, column=1, sticky="w", pady=(6, 0))

        actions = self._card(page, "执行", 3)
        self._button(actions, "下载音频", lambda: self._run_download("audio")).pack(side="left")
        self._button(actions, "下载视频", lambda: self._run_download("video")).pack(side="left", padx=6)
        self._button(actions, "下载 + AI扒谱 + 转换", self._run_full_pipeline, accent=True).pack(side="left", padx=6)
        self._button(actions, "停止", self._stop, danger=True).pack(side="left", padx=6)

    def _build_local_tab(self):
        page = self.tab_local
        card = self._card(page, "本地媒体或 MIDI", 0)
        card.columnconfigure(1, weight=1)
        self.local_input_var = tk.StringVar()
        self.local_out_var = tk.StringVar(value=os.path.join(BASE_DIR, "本地处理输出"))
        ttk.Label(card, text="输入文件").grid(row=0, column=0, sticky="w", padx=(0, 8), pady=4)
        ttk.Entry(card, textvariable=self.local_input_var).grid(row=0, column=1, sticky="ew", pady=4)
        ttk.Button(card, text="选择文件", command=self._choose_local_file).grid(row=0, column=2, padx=(6, 0))
        ttk.Label(card, text="输出文件夹").grid(row=1, column=0, sticky="w", padx=(0, 8), pady=4)
        ttk.Entry(card, textvariable=self.local_out_var).grid(row=1, column=1, sticky="ew", pady=4)
        ttk.Button(card, text="选择", command=self._choose_local_out).grid(row=1, column=2, padx=(6, 0))
        ttk.Label(card, text="选择音频/视频会先用 ffmpeg 提取音频，再用 basic-pitch 扒谱；选择 MIDI 会直接转换。", foreground="#64748b").grid(
            row=2, column=0, columnspan=3, sticky="w", pady=(4, 0))

        actions = self._card(page, "执行", 1)
        self._button(actions, "提取音频 + AI扒谱 + 转换", self._run_local_full, accent=True).pack(side="left")
        self._button(actions, "仅 AI 扒谱", self._run_local_transcribe).pack(side="left", padx=6)
        self._button(actions, "仅 MIDI 转换", self._run_local_midi).pack(side="left", padx=6)
        self._button(actions, "停止", self._stop, danger=True).pack(side="left", padx=6)

    def _build_env_tab(self):
        page = self.tab_env
        card = self._card(page, "依赖状态", 0)
        card.columnconfigure(0, weight=1)
        self.env_tree = ttk.Treeview(card, columns=("tool", "status", "path"), show="headings", height=6)
        for col, text, width in [("tool", "工具", 140), ("status", "状态", 120), ("path", "路径", 600)]:
            self.env_tree.heading(col, text=text)
            self.env_tree.column(col, width=width)
        self.env_tree.grid(row=0, column=0, sticky="ew")
        ttk.Button(card, text="重新检测", command=self._refresh_dependencies).grid(row=1, column=0, sticky="w", pady=(8, 0))
        self._button(card, "使用 uv 一键安装/更新 AI 环境", self._run_install_ai, accent=True).grid(row=1, column=0, sticky="e", pady=(8, 0))

        help_card = self._card(page, "安装说明", 1)
        help_card.columnconfigure(0, weight=1)
        self.install_text = tk.Text(help_card, height=8, wrap="word", font=("Consolas", 9))
        self.install_text.grid(row=0, column=0, sticky="ew")
        self.install_text.insert("1.0", """yt-dlp 和 ffmpeg 已经检测到可执行文件时，视频/音频下载可直接使用。

推荐使用 uv 创建独立 Python 3.11 环境，再安装 basic-pitch：

uv venv --python 3.11 .venv-ai
uv pip install --python .venv-ai\\Scripts\\python.exe -U basic-pitch yt-dlp librosa soundfile "setuptools<81"

工作台会优先使用 .venv-ai 中的 basic-pitch 做复音 AI 扒谱；不可用时才回退到
librosa pyin 内置旋律扒谱。

若 Bilibili 需要登录内容，可导出 Netscape 格式 Cookies 文件，然后在“链接一键处理”中选择。""")
        self.install_text.configure(state="disabled")
        ttk.Button(help_card, text="复制安装命令", command=lambda: self._copy_text(INSTALL_COMMAND)).grid(row=1, column=0, sticky="w", pady=(8, 0))

    def _build_library_tab(self):
        page = self.tab_library
        card = self._card(page, "现有谱库", 0)
        card.columnconfigure(0, weight=1)
        self.library_tree = ttk.Treeview(card, columns=("name", "count", "path"), show="headings", height=10)
        for col, text, width in [("name", "目录", 220), ("count", "MIDI数量", 100), ("path", "路径", 650)]:
            self.library_tree.heading(col, text=text)
            self.library_tree.column(col, width=width)
        self.library_tree.grid(row=0, column=0, sticky="ew")
        ttk.Button(card, text="刷新", command=self._refresh_library).grid(row=1, column=0, sticky="w", pady=(8, 0))
        ttk.Button(card, text="打开选中目录", command=self._open_selected_library).grid(row=1, column=0, sticky="e", pady=(8, 0))

    def _build_footer(self, root):
        footer = ttk.Frame(root)
        footer.pack(fill="both", expand=False, pady=(8, 0))
        self.progress_var = tk.DoubleVar(value=0)
        ttk.Progressbar(footer, variable=self.progress_var, maximum=100).pack(fill="x")
        self.status_var = tk.StringVar(value="就绪")
        ttk.Label(footer, textvariable=self.status_var, style="Sub.TLabel").pack(anchor="w", pady=(4, 0))
        self.log = ScrolledText(footer, height=10, wrap="word", font=("Consolas", 9), bg="#0f172a", fg="#e2e8f0", insertbackground="#e2e8f0")
        self.log.pack(fill="both", expand=True, pady=(6, 0))

    def _button(self, parent, text, command, accent=False, danger=False):
        style = "Accent.TButton" if accent else "Danger.TButton" if danger else "TButton"
        btn = ttk.Button(parent, text=text, command=command, style=style)
        self.action_buttons.append(btn)
        return btn

    def _choose_work(self):
        path = filedialog.askdirectory(title="选择工作目录")
        if path:
            self.work_var.set(path)

    def _choose_pipeline_out(self):
        path = filedialog.askdirectory(title="选择输出目录")
        if path:
            self.pipeline_out_var.set(path)

    def _choose_cookies(self):
        path = filedialog.askopenfilename(title="选择 Cookies 文件", filetypes=[("Cookies", "*.txt"), ("所有文件", "*.*")])
        if path:
            self.cookies_var.set(path)

    def _choose_local_file(self):
        path = filedialog.askopenfilename(title="选择媒体或 MIDI 文件", filetypes=[
            ("支持的文件", "*.mid *.midi *.mp3 *.wav *.m4a *.flac *.ogg *.mp4 *.mkv *.webm"),
            ("所有文件", "*.*"),
        ])
        if path:
            self.local_input_var.set(path)

    def _choose_local_out(self):
        path = filedialog.askdirectory(title="选择输出目录")
        if path:
            self.local_out_var.set(path)

    def _urls(self):
        return [line.strip() for line in self.url_text.get("1.0", "end").splitlines() if line.strip()]

    def _cfg(self):
        return studio_pipeline.conversion_config(
            min_gap_ms=int(self.min_gap_var.get()),
            melody_only=bool(self.melody_var.get()),
            preserve_duration=bool(self.preserve_var.get()),
        )

    def _log_callback(self, text):
        self.msg_queue.put(("log", text))

    def _run_download(self, mode):
        urls = self._urls()
        if not urls:
            messagebox.showwarning("缺少链接", "请先输入至少一个视频链接")
            return
        work = self.work_var.get().strip()
        cookies = self.cookies_var.get().strip()
        browser = self.browser_var.get()
        playlist = bool(self.playlist_var.get())

        def job():
            files = []
            for index, url in enumerate(urls, 1):
                self.msg_queue.put(("status", "下载 %d/%d" % (index, len(urls))))
                files.extend(media_tools.download_media(
                    url, os.path.join(work, "downloads"), mode=mode,
                    cookies_file=cookies, browser=browser, allow_playlist=playlist,
                    callback=self._log_callback, cancel_event=self.cancel_event,
                ))
                self.msg_queue.put(("progress", index * 100.0 / len(urls)))
            return {"files": files, "output": os.path.join(work, "downloads")}
        self._start_worker(job)

    def _run_full_pipeline(self):
        urls = self._urls()
        if not urls:
            messagebox.showwarning("缺少链接", "请先输入至少一个视频链接")
            return
        work = self.work_var.get().strip()
        out_dir = self.pipeline_out_var.get().strip()
        cookies = self.cookies_var.get().strip()
        browser = self.browser_var.get()
        playlist = bool(self.playlist_var.get())
        cfg = self._cfg()

        def job():
            results = []
            for index, url in enumerate(urls, 1):
                self.msg_queue.put(("status", "完整处理 %d/%d" % (index, len(urls))))
                results.append(studio_pipeline.process_url(
                    url, work, out_dir, cfg, cookies_file=cookies, browser=browser,
                    allow_playlist=playlist, callback=self._log_callback, cancel_event=self.cancel_event,
                ))
                self.msg_queue.put(("progress", index * 100.0 / len(urls)))
            return {"results": results, "output": out_dir}
        self._start_worker(job)

    def _run_local_full(self):
        path = self.local_input_var.get().strip()
        if not os.path.exists(path):
            messagebox.showwarning("缺少文件", "请选择有效的音频、视频或 MIDI 文件")
            return
        work = os.path.join(self.local_out_var.get().strip(), "中间文件")
        out_dir = self.local_out_var.get().strip()
        cfg = self._cfg()
        if path.lower().endswith((".mid", ".midi")):
            func = lambda: studio_pipeline.process_midi_file(path, out_dir, cfg)
        else:
            func = lambda: studio_pipeline.process_media_file(
                path, work, out_dir, cfg, extract_audio=True,
                callback=self._log_callback, cancel_event=self.cancel_event,
            )
        self._start_worker(func)

    def _run_local_transcribe(self):
        path = self.local_input_var.get().strip()
        if not os.path.exists(path):
            messagebox.showwarning("缺少文件", "请选择有效的音频或视频文件")
            return
        out_dir = os.path.join(self.local_out_var.get().strip(), "扒谱MIDI")

        def job():
            self.msg_queue.put(("status", "AI 扒谱"))
            if path.lower().endswith((".mid", ".midi")):
                return {"midi": path}
            audio = path
            if not path.lower().endswith((".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm")):
                audio = media_tools.extract_audio(path, out_dir, callback=self._log_callback, cancel_event=self.cancel_event)
            midi = media_tools.transcribe_audio(audio, out_dir, callback=self._log_callback, cancel_event=self.cancel_event)
            return {"midi": midi, "output": out_dir}
        self._start_worker(job)

    def _run_local_midi(self):
        path = self.local_input_var.get().strip()
        if not path.lower().endswith((".mid", ".midi")):
            messagebox.showwarning("文件类型", "仅 MIDI 转换需要选择 .mid 或 .midi 文件")
            return
        out_dir = self.local_out_var.get().strip()
        cfg = self._cfg()
        self._start_worker(lambda: studio_pipeline.process_midi_file(path, out_dir, cfg))

    def _run_install_ai(self):
        def job():
            self.msg_queue.put(("status", "使用 uv 安装/更新 AI 环境"))
            python_path = media_tools.install_ai_environment(
                callback=self._log_callback,
                cancel_event=self.cancel_event,
            )
            return {"output": python_path}
        self._start_worker(job)

    def _start_worker(self, func):
        if self.running:
            return
        self.cancel_event.clear()
        self.running = True
        self.progress_var.set(0)
        self._set_buttons(False)
        self._append_log("=" * 70)
        self.worker = threading.Thread(target=self._worker_entry, args=(func,), daemon=True)
        self.worker.start()

    def _worker_entry(self, func):
        try:
            result = func()
            self.msg_queue.put(("done", result))
        except Exception as exc:
            self.msg_queue.put(("error", "%s: %s" % (type(exc).__name__, exc)))

    def _stop(self):
        self.cancel_event.set()
        self.status_var.set("正在停止...")

    def _set_buttons(self, enabled):
        state = "normal" if enabled else "disabled"
        for button in self.action_buttons:
            if button.winfo_exists():
                button.configure(state=state)

    def _append_log(self, text):
        stamp = datetime.now().strftime("%H:%M:%S")
        self.log.insert("end", "[%s] %s\n" % (stamp, text))
        self.log.see("end")

    def _poll_queue(self):
        try:
            while True:
                kind, payload = self.msg_queue.get_nowait()
                if kind == "log":
                    self._append_log(payload)
                elif kind == "status":
                    self.status_var.set(payload)
                elif kind == "progress":
                    self.progress_var.set(payload)
                elif kind == "done":
                    self._finish(payload)
                elif kind == "error":
                    self._fail(payload)
        except queue.Empty:
            pass
        self.after(120, self._poll_queue)

    def _finish(self, result):
        self.running = False
        self._set_buttons(True)
        self.progress_var.set(100)
        self.status_var.set("完成")
        output = result.get("output") if isinstance(result, dict) else ""
        self._append_log("完成" + ("：" + output if output else ""))
        if isinstance(result, dict) and result.get("midi"):
            self._append_log("MIDI：" + result["midi"])
        self._refresh_dependencies()
        messagebox.showinfo("完成", "任务已完成")

    def _fail(self, error):
        self.running = False
        self._set_buttons(True)
        self.status_var.set("失败")
        self._append_log("失败：" + error)
        if "basic-pitch" in error:
            error += "\n\n可点击“环境与安装”复制安装命令。"
        self._refresh_dependencies()
        messagebox.showerror("任务失败", error)

    def _open_path(self, path):
        if not path:
            return
        try:
            os.makedirs(path, exist_ok=True)
            os.startfile(path)
        except Exception as exc:
            messagebox.showerror("打开失败", str(exc))

    def _copy_text(self, text):
        self.clipboard_clear()
        self.clipboard_append(text)
        messagebox.showinfo("已复制", text)

    def _refresh_dependencies(self):
        if not hasattr(self, "env_tree"):
            return
        for item in self.env_tree.get_children():
            self.env_tree.delete(item)
        status = media_tools.dependency_status()
        for tool, path in status.items():
            self.env_tree.insert("", "end", values=(tool, "已安装" if path else "未安装", path or "未找到"))

    def _refresh_library(self):
        if not hasattr(self, "library_tree"):
            return
        for item in self.library_tree.get_children():
            self.library_tree.delete(item)
        playable_source = os.environ.get("GENSHIN_PLAYABLE_DIR", os.path.join(BASE_DIR, "示例谱库", "成熟的原琴"))
        backup_source = os.environ.get("GENSHIN_BACKUP_DIR", os.path.join(BASE_DIR, "示例谱库", "不可播备份"))
        folders = [
            ("推荐纯旋律", os.path.join(BASE_DIR, "优化完成_原神可用")),
            ("和弦简化", os.path.join(BASE_DIR, "优化完成_和弦简化_时长匹配")),
            ("成熟原琴源", playable_source),
            ("不可播备份源", backup_source),
            ("零丢音旧版", os.path.join(BASE_DIR, "旧_零丢音_时长会变")),
        ]
        for name, path in folders:
            count = 0
            if os.path.isdir(path):
                for _, _, files in os.walk(path):
                    count += sum(1 for f in files if f.lower().endswith((".mid", ".midi")))
            self.library_tree.insert("", "end", values=(name, count, path))

    def _open_selected_library(self):
        selection = self.library_tree.selection()
        if not selection:
            return
        values = self.library_tree.item(selection[0], "values")
        if values:
            self._open_path(values[2])


def main():
    app = GenshinMusicStudio()
    app.mainloop()


if __name__ == "__main__":
    main()
