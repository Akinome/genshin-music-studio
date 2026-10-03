#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Modern CustomTkinter studio for download, AI transcription, and conversion."""
import os
import queue
import sys
import threading
from datetime import datetime

import tkinter as tk
from tkinter import filedialog, messagebox

import customtkinter as ctk

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


class ModernMusicStudio(ctk.CTk):
    def __init__(self):
        ctk.set_appearance_mode("System")
        ctk.set_default_color_theme("blue")
        super().__init__(fg_color=("gray96", "gray10"))
        self.title("原神音乐工作台 - 下载 / AI扒谱 / 转换")
        self.geometry("1260x900")
        self.minsize(1080, 760)

        self.msg_queue = queue.Queue()
        self.cancel_event = threading.Event()
        self.worker = None
        self.running = False
        self.action_buttons = []
        self.stop_buttons = []
        self.page_buttons = {}
        self.pages = {}

        self.grid_columnconfigure(0, weight=0)
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)
        self._build_sidebar()
        self._build_content()
        self._show_page("download")
        self._refresh_dependencies()
        self._refresh_library()
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.after(120, self._poll_queue)

    def _font(self, size=13, bold=False):
        return ctk.CTkFont(family="Microsoft YaHei UI", size=size, weight="bold" if bold else "normal")

    def _build_sidebar(self):
        side = ctk.CTkFrame(self, width=224, corner_radius=0, fg_color=("gray90", "#111827"))
        side.grid(row=0, column=0, sticky="nsew")
        side.grid_rowconfigure(12, weight=1)
        ctk.CTkLabel(side, text="原神音乐工作台", font=self._font(20, True)).grid(
            row=0, column=0, padx=20, pady=(22, 2), sticky="w")
        ctk.CTkLabel(side, text="下载 · 扒谱 · 主旋律 · 转换", font=self._font(11), text_color=("gray40", "gray65")).grid(
            row=1, column=0, padx=20, pady=(0, 18), sticky="w")

        items = [
            ("download", "链接一键处理"),
            ("local", "本地文件"),
            ("env", "环境与安装"),
            ("library", "谱库"),
        ]
        for index, (key, text) in enumerate(items, start=2):
            button = ctk.CTkButton(
                side, text=text, anchor="w", height=42, corner_radius=8,
                fg_color="transparent", hover_color=("gray80", "#1f2937"),
                font=self._font(13), command=lambda k=key: self._show_page(k),
            )
            button.grid(row=index, column=0, padx=12, pady=3, sticky="ew")
            self.page_buttons[key] = button

        self.dark_var = tk.BooleanVar(value=ctk.get_appearance_mode().lower() == "dark")
        ctk.CTkSwitch(side, text="深色模式", variable=self.dark_var, command=self._toggle_theme,
                      font=self._font(12)).grid(row=11, column=0, padx=20, pady=(0, 18), sticky="w")
        ctk.CTkLabel(side, text="Basic Pitch + uv AI 环境", font=self._font(10), text_color=("gray45", "gray60")).grid(
            row=13, column=0, padx=20, pady=(0, 18), sticky="w")

    def _build_content(self):
        content = ctk.CTkFrame(self, corner_radius=0, fg_color="transparent")
        content.grid(row=0, column=1, sticky="nsew", padx=18, pady=14)
        content.grid_columnconfigure(0, weight=1)
        content.grid_rowconfigure(1, weight=1)

        header = ctk.CTkFrame(content, fg_color="transparent")
        header.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        header.grid_columnconfigure(0, weight=1)
        self.page_title = ctk.CTkLabel(header, text="链接一键处理", font=self._font(22, True))
        self.page_title.grid(row=0, column=0, sticky="w")
        self.header_status = ctk.CTkLabel(header, text="就绪", font=self._font(12), text_color=("gray40", "gray65"))
        self.header_status.grid(row=0, column=1, sticky="e")
        self._action_button(header, "停止", self._stop, danger=True, stop=True).grid(row=0, column=2, padx=(12, 0), sticky="e")

        self.page_host = ctk.CTkFrame(content, fg_color="transparent")
        self.page_host.grid(row=1, column=0, sticky="nsew")
        self.page_host.grid_columnconfigure(0, weight=1)
        self.page_host.grid_rowconfigure(0, weight=1)

        self.pages["download"] = self._build_download_page()
        self.pages["local"] = self._build_local_page()
        self.pages["env"] = self._build_env_page()
        self.pages["library"] = self._build_library_page()

        footer = ctk.CTkFrame(content, fg_color=("gray92", "gray13"), corner_radius=12)
        footer.grid(row=2, column=0, sticky="ew", pady=(10, 0))
        footer.grid_columnconfigure(0, weight=1)
        self.progress = ctk.CTkProgressBar(footer, height=8)
        self.progress.grid(row=0, column=0, sticky="ew", padx=12, pady=(10, 4))
        self.progress.set(0)
        self.status_label = ctk.CTkLabel(footer, text="就绪", font=self._font(11), text_color=("gray40", "gray65"))
        self.status_label.grid(row=1, column=0, sticky="w", padx=12)
        self.log_box = ctk.CTkTextbox(
            footer, height=170, corner_radius=8, font=ctk.CTkFont(family="Consolas", size=11),
            fg_color=("gray88", "#0f172a"), text_color=("gray15", "#dbeafe"), wrap="word",
        )
        self.log_box.grid(row=2, column=0, sticky="ew", padx=12, pady=(6, 12))
        self.log_box.configure(state="disabled")

    def _page_frame(self):
        return ctk.CTkScrollableFrame(self.page_host, fg_color="transparent")

    def _card(self, parent, title, row):
        card = ctk.CTkFrame(parent, corner_radius=12, border_width=1,
                            border_color=("gray85", "gray25"), fg_color=("white", "#1c1c1e"))
        card.grid(row=row, column=0, sticky="ew", pady=(0, 10))
        parent.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(card, text=title, font=self._font(14, True)).grid(
            row=0, column=0, columnspan=6, sticky="w", padx=14, pady=(12, 8))
        return card

    def _action_button(self, parent, text, command, primary=False, danger=False, stop=False):
        if primary:
            fg, hover = "#2563eb", "#1d4ed8"
        elif danger:
            fg, hover = "#dc2626", "#b91c1c"
        else:
            fg, hover = ("gray85", "#2c2c2e"), ("gray78", "#3a3a3c")
        button = ctk.CTkButton(parent, text=text, command=command, height=38, corner_radius=9,
                               fg_color=fg, hover_color=hover, font=self._font(12, True))
        if stop:
            self.stop_buttons.append(button)
        else:
            self.action_buttons.append(button)
        return button

    def _build_download_page(self):
        page = self._page_frame()
        url_card = self._card(page, "视频链接", 0)
        url_card.grid_columnconfigure(0, weight=1)
        self.url_text = ctk.CTkTextbox(url_card, height=110, corner_radius=8, font=self._font(12))
        self.url_text.grid(row=1, column=0, columnspan=6, sticky="ew", padx=14, pady=(0, 4))
        ctk.CTkLabel(url_card, text="每行一个链接，支持 yt-dlp 可处理的 Bilibili / YouTube 等网站。",
                     font=self._font(11), text_color=("gray40", "gray65")).grid(
            row=2, column=0, columnspan=6, sticky="w", padx=14, pady=(0, 12))

        dirs = self._card(page, "工作目录", 1)
        dirs.grid_columnconfigure(1, weight=1)
        self.work_var = tk.StringVar(value=DEFAULT_WORK)
        self.pipeline_out_var = tk.StringVar(value=DEFAULT_OUTPUT)
        ctk.CTkLabel(dirs, text="下载/中间文件", font=self._font(12)).grid(row=1, column=0, sticky="w", padx=14, pady=4)
        ctk.CTkEntry(dirs, textvariable=self.work_var, height=34).grid(row=1, column=1, sticky="ew", padx=6, pady=4)
        ctk.CTkButton(dirs, text="选择", width=64, command=self._choose_work).grid(row=1, column=2, padx=4)
        ctk.CTkButton(dirs, text="打开", width=64, command=lambda: self._open_path(self.work_var.get())).grid(row=1, column=3, padx=(4, 14))
        ctk.CTkLabel(dirs, text="原神MIDI输出", font=self._font(12)).grid(row=2, column=0, sticky="w", padx=14, pady=4)
        ctk.CTkEntry(dirs, textvariable=self.pipeline_out_var, height=34).grid(row=2, column=1, sticky="ew", padx=6, pady=4)
        ctk.CTkButton(dirs, text="选择", width=64, command=self._choose_pipeline_out).grid(row=2, column=2, padx=4)
        ctk.CTkButton(dirs, text="打开", width=64, command=lambda: self._open_path(self.pipeline_out_var.get())).grid(row=2, column=3, padx=(4, 14))

        options = self._card(page, "下载与转换选项", 2)
        options.grid_columnconfigure(0, weight=3)
        options.grid_columnconfigure(2, weight=2)
        left = ctk.CTkFrame(options, fg_color="transparent")
        left.grid(row=1, column=0, sticky="nsew", padx=(14, 12), pady=(0, 14))
        left.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(left, text="下载选项", font=self._font(13, True)).grid(row=0, column=0, columnspan=3, sticky="w", pady=(0, 6))
        ctk.CTkLabel(left, text="Cookies 文件", font=self._font(12)).grid(row=1, column=0, sticky="w", padx=(0, 8), pady=4)
        self.cookies_var = tk.StringVar()
        ctk.CTkEntry(left, textvariable=self.cookies_var, height=34).grid(row=1, column=1, sticky="ew", pady=4)
        ctk.CTkButton(left, text="选择", width=64, command=self._choose_cookies).grid(row=1, column=2, padx=(8, 0))
        ctk.CTkLabel(left, text="浏览器 Cookies", font=self._font(12)).grid(row=2, column=0, sticky="w", padx=(0, 8), pady=4)
        self.browser_var = tk.StringVar(value="不使用")
        ctk.CTkOptionMenu(left, values=["不使用", "chrome", "edge", "firefox"], variable=self.browser_var,
                          width=150, height=34).grid(row=2, column=1, sticky="w", pady=4)
        self.playlist_var = tk.BooleanVar(value=False)
        ctk.CTkCheckBox(left, text="下载整个播放列表", variable=self.playlist_var, font=self._font(12)).grid(
            row=3, column=0, columnspan=3, sticky="w", pady=(8, 0))

        ctk.CTkFrame(options, width=1, fg_color=("gray80", "gray30")).grid(row=1, column=1, sticky="ns", padx=4, pady=(0, 14))

        right = ctk.CTkFrame(options, fg_color="transparent")
        right.grid(row=1, column=2, sticky="nsew", padx=(12, 14), pady=(0, 14))
        right.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(right, text="转换选项", font=self._font(13, True)).grid(row=0, column=0, columnspan=2, sticky="w", pady=(0, 6))
        self.melody_var = tk.BooleanVar(value=True)
        self.preserve_var = tk.BooleanVar(value=True)
        ctk.CTkCheckBox(right, text="只保留主旋律", variable=self.melody_var, font=self._font(12)).grid(
            row=1, column=0, columnspan=2, sticky="w", pady=4)
        ctk.CTkCheckBox(right, text="保持原曲时长", variable=self.preserve_var, font=self._font(12)).grid(
            row=2, column=0, columnspan=2, sticky="w", pady=4)
        ctk.CTkLabel(right, text="最短按键间隔 (ms)", font=self._font(12)).grid(row=3, column=0, sticky="w", pady=(8, 0))
        self.min_gap_var = tk.IntVar(value=60)
        ctk.CTkEntry(right, textvariable=self.min_gap_var, width=80, height=34).grid(row=3, column=1, sticky="w", pady=(8, 0))

        actions = self._card(page, "执行", 3)
        self._action_button(actions, "下载音频", lambda: self._run_download("audio")).grid(row=1, column=0, padx=(14, 6), pady=(0, 14))
        self._action_button(actions, "下载视频", lambda: self._run_download("video")).grid(row=1, column=1, padx=6, pady=(0, 14))
        self._action_button(actions, "下载 + AI扒谱 + 转换", self._run_full_pipeline, primary=True).grid(row=1, column=2, padx=6, pady=(0, 14))
        self._action_button(actions, "停止", self._stop, danger=True, stop=True).grid(row=1, column=3, padx=(6, 14), pady=(0, 14))
        return page

    def _build_local_page(self):
        page = self._page_frame()
        card = self._card(page, "本地媒体或 MIDI", 0)
        card.grid_columnconfigure(1, weight=1)
        self.local_input_var = tk.StringVar()
        self.local_out_var = tk.StringVar(value=os.path.join(BASE_DIR, "本地处理输出"))
        ctk.CTkLabel(card, text="输入文件", font=self._font(12)).grid(row=1, column=0, sticky="w", padx=14, pady=5)
        ctk.CTkEntry(card, textvariable=self.local_input_var, height=34).grid(row=1, column=1, sticky="ew", padx=6, pady=5)
        ctk.CTkButton(card, text="选择文件", width=84, command=self._choose_local_file).grid(row=1, column=2, padx=(6, 14))
        ctk.CTkLabel(card, text="输出文件夹", font=self._font(12)).grid(row=2, column=0, sticky="w", padx=14, pady=5)
        ctk.CTkEntry(card, textvariable=self.local_out_var, height=34).grid(row=2, column=1, sticky="ew", padx=6, pady=5)
        ctk.CTkButton(card, text="选择", width=64, command=self._choose_local_out).grid(row=2, column=2, padx=(6, 14))
        ctk.CTkLabel(card, text="音频/视频会先提取音频，再用 basic-pitch 扒谱；MIDI 会直接转换。",
                     font=self._font(11), text_color=("gray40", "gray65")).grid(
            row=3, column=0, columnspan=3, sticky="w", padx=14, pady=(4, 14))

        actions = self._card(page, "执行", 1)
        self._action_button(actions, "提取音频 + AI扒谱 + 转换", self._run_local_full, primary=True).grid(row=1, column=0, padx=(14, 6), pady=(0, 14))
        self._action_button(actions, "仅 AI 扒谱", self._run_local_transcribe).grid(row=1, column=1, padx=6, pady=(0, 14))
        self._action_button(actions, "仅 MIDI 转换", self._run_local_midi).grid(row=1, column=2, padx=6, pady=(0, 14))
        self._action_button(actions, "停止", self._stop, danger=True, stop=True).grid(row=1, column=3, padx=(6, 14), pady=(0, 14))
        return page

    def _build_env_page(self):
        page = self._page_frame()
        card = self._card(page, "依赖状态", 0)
        card.grid_columnconfigure(0, weight=1)
        self.env_frame = ctk.CTkFrame(card, fg_color="transparent")
        self.env_frame.grid(row=1, column=0, columnspan=6, sticky="ew", padx=14, pady=(0, 8))
        self._action_button(card, "重新检测", self._refresh_dependencies).grid(row=2, column=0, sticky="w", padx=14, pady=(0, 14))
        self._action_button(card, "使用 uv 安装/更新 AI 环境", self._run_install_ai, primary=True).grid(
            row=2, column=1, sticky="w", padx=6, pady=(0, 14))

        help_card = self._card(page, "AI 环境说明", 1)
        help_card.grid_columnconfigure(0, weight=1)
        text = ctk.CTkTextbox(help_card, height=180, corner_radius=8, font=ctk.CTkFont(family="Consolas", size=11), wrap="word")
        text.grid(row=1, column=0, sticky="ew", padx=14, pady=(0, 8))
        text.insert("1.0", """工作台会优先使用 .venv-ai 中的 basic-pitch。

uv venv --python 3.11 .venv-ai
uv pip install --python .venv-ai\\Scripts\\python.exe -U basic-pitch yt-dlp librosa soundfile "setuptools<81"

如果 basic-pitch 不可用，程序会自动回退到 librosa pyin 内置旋律扒谱。
Bilibili 登录内容可导出 Netscape 格式 Cookies 文件后在上传选项中选择。""")
        text.configure(state="disabled")
        self._action_button(help_card, "复制安装命令", lambda: self._copy_text(INSTALL_COMMAND)).grid(
            row=2, column=0, sticky="w", padx=14, pady=(0, 14))
        return page

    def _build_library_page(self):
        page = self._page_frame()
        card = self._card(page, "现有谱库", 0)
        card.grid_columnconfigure(0, weight=1)
        self.library_frame = ctk.CTkFrame(card, fg_color="transparent")
        self.library_frame.grid(row=1, column=0, columnspan=6, sticky="ew", padx=14, pady=(0, 8))
        self._action_button(card, "刷新", self._refresh_library).grid(row=2, column=0, sticky="w", padx=14, pady=(0, 14))
        return page

    def _show_page(self, key):
        for name, page in self.pages.items():
            if name == key:
                page.grid(row=0, column=0, sticky="nsew")
            else:
                page.grid_forget()
        titles = {"download": "链接一键处理", "local": "本地文件", "env": "环境与安装", "library": "谱库"}
        self.page_title.configure(text=titles.get(key, key))
        for name, button in self.page_buttons.items():
            button.configure(fg_color=("#dbeafe", "#1e3a5f") if name == key else "transparent")

    def _toggle_theme(self):
        ctk.set_appearance_mode("Dark" if self.dark_var.get() else "Light")

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

    def _start_worker(self, func):
        if self.running:
            return
        self.cancel_event.clear()
        self.running = True
        self.progress.set(0)
        self._set_running(True)
        self._append_log("=" * 72)
        self.worker = threading.Thread(target=self._worker_entry, args=(func,), daemon=True)
        self.worker.start()

    def _worker_entry(self, func):
        try:
            self.msg_queue.put(("done", func()))
        except Exception as exc:
            self.msg_queue.put(("error", "%s: %s" % (type(exc).__name__, exc)))

    def _set_running(self, running):
        for button in self.action_buttons:
            button.configure(state="disabled" if running else "normal")
        for button in self.stop_buttons:
            button.configure(state="normal" if running else "disabled")

    def _stop(self):
        if not self.running:
            return
        self.cancel_event.set()
        for button in self.stop_buttons:
            button.configure(state="disabled")
        self.status_label.configure(text="正在停止，请稍候...")
        self.header_status.configure(text="正在停止...")
        self._append_log("请求停止：正在终止当前任务及子进程...")

    def _run_download(self, mode):
        urls = self._urls()
        if not urls:
            messagebox.showwarning("缺少链接", "请先输入至少一个视频链接")
            return
        work, cookies, browser, playlist = self.work_var.get().strip(), self.cookies_var.get().strip(), self.browser_var.get(), bool(self.playlist_var.get())

        def job():
            files = []
            for index, url in enumerate(urls, 1):
                if self.cancel_event.is_set():
                    raise RuntimeError("任务已取消")
                self.msg_queue.put(("status", "下载 %d/%d" % (index, len(urls))))
                files.extend(media_tools.download_media(
                    url, os.path.join(work, "downloads"), mode=mode,
                    cookies_file=cookies, browser=browser, allow_playlist=playlist,
                    callback=self._log_callback, cancel_event=self.cancel_event,
                ))
                self.msg_queue.put(("progress", index * 100 / len(urls)))
            return {"files": files, "output": os.path.join(work, "downloads")}
        self._start_worker(job)

    def _run_full_pipeline(self):
        urls = self._urls()
        if not urls:
            messagebox.showwarning("缺少链接", "请先输入至少一个视频链接")
            return
        work, out_dir, cookies, browser, playlist, cfg = (
            self.work_var.get().strip(), self.pipeline_out_var.get().strip(),
            self.cookies_var.get().strip(), self.browser_var.get(),
            bool(self.playlist_var.get()), self._cfg(),
        )

        def job():
            results = []
            for index, url in enumerate(urls, 1):
                if self.cancel_event.is_set():
                    raise RuntimeError("任务已取消")
                self.msg_queue.put(("status", "完整处理 %d/%d" % (index, len(urls))))
                results.append(studio_pipeline.process_url(
                    url, work, out_dir, cfg, cookies_file=cookies, browser=browser,
                    allow_playlist=playlist, callback=self._log_callback, cancel_event=self.cancel_event,
                ))
                self.msg_queue.put(("progress", index * 100 / len(urls)))
            return {"results": results, "output": out_dir}
        self._start_worker(job)

    def _run_local_full(self):
        path = self.local_input_var.get().strip()
        if not os.path.exists(path):
            messagebox.showwarning("缺少文件", "请选择有效的音频、视频或 MIDI 文件")
            return
        out_dir = self.local_out_var.get().strip()
        cfg = self._cfg()
        if path.lower().endswith((".mid", ".midi")):
            func = lambda: studio_pipeline.process_midi_file(path, out_dir, cfg)
        else:
            func = lambda: studio_pipeline.process_media_file(
                path, os.path.join(out_dir, "中间文件"), out_dir, cfg, extract_audio=True,
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
            messagebox.showwarning("文件类型", "请选择 .mid 或 .midi 文件")
            return
        self._start_worker(lambda: studio_pipeline.process_midi_file(path, self.local_out_var.get().strip(), self._cfg()))

    def _run_install_ai(self):
        def job():
            self.msg_queue.put(("status", "使用 uv 安装/更新 AI 环境"))
            return {"output": media_tools.install_ai_environment(callback=self._log_callback, cancel_event=self.cancel_event)}
        self._start_worker(job)

    def _append_log(self, text):
        stamp = datetime.now().strftime("%H:%M:%S")
        self.log_box.configure(state="normal")
        self.log_box.insert("end", "[%s] %s\n" % (stamp, text))
        self.log_box.see("end")
        self.log_box.configure(state="disabled")

    def _poll_queue(self):
        try:
            while True:
                kind, payload = self.msg_queue.get_nowait()
                if kind == "log":
                    self._append_log(payload)
                elif kind == "status":
                    self.status_label.configure(text=payload)
                    self.header_status.configure(text=payload)
                elif kind == "progress":
                    self.progress.set(float(payload) / 100.0)
                elif kind == "done":
                    self._finish(payload)
                elif kind == "error":
                    self._fail(payload)
        except queue.Empty:
            pass
        self.after(120, self._poll_queue)

    def _finish(self, result):
        self.running = False
        self._set_running(False)
        self.progress.set(1)
        self.status_label.configure(text="完成")
        self.header_status.configure(text="完成")
        output = result.get("output") if isinstance(result, dict) else ""
        if output:
            self._append_log("完成：" + output)
        self._refresh_dependencies()
        messagebox.showinfo("完成", "任务已完成")

    def _fail(self, error):
        self.running = False
        self._set_running(False)
        if "任务已取消" in error:
            self.status_label.configure(text="已停止")
            self.header_status.configure(text="已停止")
            self._append_log("任务已停止")
            return
        self.status_label.configure(text="失败")
        self.header_status.configure(text="失败")
        self._append_log("失败：" + error)
        self._refresh_dependencies()
        messagebox.showerror("任务失败", error)

    def _refresh_dependencies(self):
        if not hasattr(self, "env_frame"):
            return
        for child in self.env_frame.winfo_children():
            child.destroy()
        status = media_tools.dependency_status()
        for row, (tool, path) in enumerate(status.items()):
            ctk.CTkLabel(self.env_frame, text=tool, font=self._font(12, True), width=110, anchor="w").grid(
                row=row, column=0, sticky="w", padx=(0, 10), pady=3)
            ok = bool(path)
            ctk.CTkLabel(self.env_frame, text="已安装" if ok else "未安装", font=self._font(11),
                         text_color=("#059669", "#34d399") if ok else ("#dc2626", "#f87171")).grid(
                row=row, column=1, sticky="w", padx=(0, 10), pady=3)
            ctk.CTkLabel(self.env_frame, text=path or "未找到", font=self._font(11),
                         text_color=("gray45", "gray65"), anchor="w").grid(row=row, column=2, sticky="w", pady=3)

    def _refresh_library(self):
        if not hasattr(self, "library_frame"):
            return
        for child in self.library_frame.winfo_children():
            child.destroy()
        playable_source = os.environ.get("GENSHIN_PLAYABLE_DIR", os.path.join(BASE_DIR, "示例谱库", "成熟的原琴"))
        backup_source = os.environ.get("GENSHIN_BACKUP_DIR", os.path.join(BASE_DIR, "示例谱库", "不可播备份"))
        folders = [
            ("推荐纯旋律", os.path.join(BASE_DIR, "优化完成_原神可用")),
            ("和弦简化", os.path.join(BASE_DIR, "优化完成_和弦简化_时长匹配")),
            ("成熟原琴源", playable_source),
            ("不可播备份源", backup_source),
            ("零丢音旧版", os.path.join(BASE_DIR, "旧_零丢音_时长会变")),
        ]
        for row, (name, path) in enumerate(folders):
            count = 0
            if os.path.isdir(path):
                for _, _, files in os.walk(path):
                    count += sum(1 for f in files if f.lower().endswith((".mid", ".midi")))
            ctk.CTkLabel(self.library_frame, text=name, font=self._font(12, True), width=140, anchor="w").grid(
                row=row, column=0, sticky="w", padx=(0, 10), pady=4)
            ctk.CTkLabel(self.library_frame, text="%d 个 MIDI" % count, font=self._font(11), width=100, anchor="w").grid(
                row=row, column=1, sticky="w", pady=4)
            ctk.CTkLabel(self.library_frame, text=path, font=self._font(10), text_color=("gray45", "gray65"), anchor="w").grid(
                row=row, column=2, sticky="w", padx=(0, 10), pady=4)
            ctk.CTkButton(self.library_frame, text="打开", width=58, command=lambda p=path: self._open_path(p)).grid(
                row=row, column=3, sticky="e", pady=4)

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

    def _on_close(self):
        if self.running:
            self.cancel_event.set()
        self.destroy()


def main():
    app = ModernMusicStudio()
    app.mainloop()


if __name__ == "__main__":
    main()
