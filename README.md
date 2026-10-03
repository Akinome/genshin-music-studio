# 原神专用 MIDI 转换器

开源许可证：GPL-3.0。第三方模型和项目说明见 `THIRD_PARTY_NOTICES.md`。

把 AI 扒谱出来的 MIDI 转成**原神能 100% 演奏**的 MIDI 文件，直接喂给你已有的自动演奏工具。

## 它能做什么

| 功能 | 说明 |
|---|---|
| 去鼓组 | 自动跳过打击乐通道（channel 9） |
| 音域约束 | 把音符八度折叠/移调到**风物之诗琴音域 C3-B5**（可改），不超出琴能弹的范围 |
| 和弦拆分 | 同时发声的和弦自动拆成琶音（游戏内一次只能弹一个音） |
| 保持时长 | 不改变全局速度，冲突音符自动简化，输出时长与源 MIDI 一致 |
| 零丢音可选 | 需要保留所有音符时，可开启自动放慢；输出时长会变长 |
| 输出标准 MIDI | 单轨 type 0，绝大多数自动演奏工具都能直接读取 |

## 安装

```powershell
pip install mido
```

## 可选源谱库路径

批量分析和配对脚本不再写死个人目录，可通过环境变量指定：

```powershell
$env:GENSHIN_PLAYABLE_DIR = "D:\Music\成熟的原琴"
$env:GENSHIN_BACKUP_DIR = "D:\Music\不可播备份"
```

未设置时会回退到项目下的 `示例谱库` 目录。

## 图形界面

双击 `启动原神琴谱转换器.bat`，或运行：

```powershell
python genshin_midi_gui.py
```

界面支持选择单个 MIDI 或整个文件夹，预览统计，批量转换，并可直接打开输出目录。默认使用“纯旋律 + 保持原曲时长”。

当前已经生成好的推荐谱子位于：

```text
优化完成_原神可用
├── 成熟原琴        (72 首)
├── 不可播备份      (35 首)
└── 全部转换报告.csv
```

该目录是纯旋律版：107 首全部校验通过，只保留主旋律，时长比例均为 `1.000000`，最短按键间隔 `60.417ms`。

其他版本：

- `优化完成_和弦简化_时长匹配`：保留简化和弦，时长不变。
- `旧_零丢音_时长会变`：保留全部音符，但歌曲时长会变长。

## 完整音乐工作台

双击 `启动原神音乐工作台.bat`，或运行：

```powershell
python genshin_music_studio.py
```

工作台包含：

- 现代 CustomTkinter 界面：侧边栏、深色模式、始终可见的停止按钮
- 视频/音频下载：使用 `yt-dlp` + `ffmpeg`
- AI 扒谱：使用 `basic-pitch`
- 原神主旋律提取与 MIDI 转换
- 本地媒体、MIDI 文件处理
- 依赖状态检测和现有谱库浏览

当前机器已检测到 `yt-dlp`、`ffmpeg`、`uv`，并已创建独立 AI 环境 `.venv-ai`：

```powershell
uv venv --python 3.11 .venv-ai
uv pip install --python .venv-ai\Scripts\python.exe -U basic-pitch yt-dlp librosa soundfile "setuptools<81"
```

也可以在“环境与安装”页点击“一键使用 uv 安装/更新 AI 环境”，或双击 `安装AI扒谱环境_uv.bat`。

程序会优先调用 `.venv-ai` 中的 `basic-pitch` 做复音 AI 扒谱；如果它不可用，会自动回退到 `librosa pyin` 主旋律扒谱。

新版界面入口是 `genshin_music_studio.py`，旧版保留在 `genshin_music_studio_legacy.py`。停止按钮现在会终止当前进程树，yt-dlp、ffmpeg 和 TensorFlow 子进程都会被清理。

## WinUI 3 原生版

推荐架构已经落地：WinUI 3 负责原生界面，Python 负责下载、AI 扒谱和 MIDI 转换。

```powershell
dotnet run --project GenshinMusicStudio.WinUI\GenshinMusicStudio.WinUI.csproj -c Debug
```

双击 `启动WinUI工作台.bat` 也可以启动。项目当前使用：

- .NET 8
- WinUI 3 / Windows App SDK
- Mica 原生背景
- NavigationView 页面导航
- `studio_backend.py` JSON-lines 后端
- C# `Process.Kill(entireProcessTree: true)` 停止整个后端进程树

### 原生 ONNX 扒谱

音频/视频 AI 扒谱已切换为 C# 原生实现，不再依赖 Python 或 TensorFlow：

- 内置 Basic Pitch `nmp.onnx` 模型，约 225 KB
- `Microsoft.ML.OnnxRuntime` 直接推理
- C# 调用 ffmpeg 解码为 22050 Hz 单声道 float PCM
- C# 分窗、结果拼接、onset/note 后处理
- C# Viterbi 主旋律提取
- C# 直接写出 SMF Type 0 MIDI

当前 WinUI 版中，视频/音频下载和 AI 扒谱走原生 ONNX；已有 MIDI 文件转换暂时保留 Python 兼容路径，便于继续复用成熟的和弦/旋律处理逻辑。

界面现在支持：

- 仅 AI 扒谱（不转换）
- Basic Pitch ONNX 原生复音模式
- Basic Pitch Python 复音模式
- Piano Transcription 钢琴专用模式
- librosa pyin 主旋律模式
- 可折叠、可拖拽高度的运行日志框
- AI 模型选择框位于“下载视频”旁边

Piano Transcription 使用 `piano_transcription_inference`，模型权重约 165 MB，适合纯钢琴录音；当前通过 `.venv-ai` 的 CPU 版 PyTorch 运行。

## 其他可选扒谱模型

- `basic-pitch`：当前默认，适合钢琴、吉他、旋律和多重音高
- `piano_transcription_inference`：钢琴专用，PyTorch 环境
- `MT3` / `Omnizart`：多乐器扒谱，效果更全但安装和运行更重
- `torchcrepe` / `CREPE` / `SPICE` / `PESTO`：单音旋律音高跟踪，适合人声或独奏
- `Demucs` / `ByteSep`：人声和伴奏分离，通常先分离再送扒谱模型

别人“直接安装就能跑”通常是因为程序自带 Python 环境、固定了 Python 版本和依赖，或者使用 ONNX/PyTorch 预编译包。工作台现在也采用这种方式，用 `uv` 隔离 Python 3.11 环境，不依赖系统 Python 3.12。

## 快速使用

```powershell
# 1. 首次生成配置
python midi_to_genshin.py --init

# 2. 转换（输出到 输入名_genshin.mid）
python midi_to_genshin.py 你的歌.mid

# 3. 把生成的 *_genshin.mid 喂给你的自动演奏工具
```

## 常用参数

```powershell
python midi_to_genshin.py 你的歌.mid -o 输出.mid      # 指定输出路径
python midi_to_genshin.py 你的歌.mid --preview         # 只看处理摘要，不生成文件
python midi_to_genshin.py 你的歌.mid --track 2         # 只用第 2 条音轨（AI 扒谱多轨时常用）
python midi_to_genshin.py 你的歌.mid --tempo 1.2       # 手动放慢 1.2 倍
python midi_to_genshin.py 你的歌.mid --chord-mode top  # 和弦只留最高音（不要琶音）
python midi_to_genshin.py 你的歌.mid --no-auto-slow    # 关闭自动放慢
```

## config.json 说明

```json
{
  "range": {"low": 48, "high": 83},  // 琴音域（MIDI 音号），C3-B5
  "transpose_semitones": 0,          // 整体移调
  "chord_mode": "arpeggio",          // 和弦处理：arpeggio 琶音 / top 只留最高音
  "arpeggio_gap_ms": 25,             // 琶音间隔
  "press_ms": 120,                   // 输出音符时长
  "tempo_scale": 1.0,                // 手动放慢倍数
  "min_gap_ms": 60,                  // 最短按键间隔
  "chord_window_ms": 30,             // 和弦判定窗口
  "auto_slow": true,                 // 自动放慢
  "quantize_ms": 0                   // 时间量化（AI 扒谱抖动明显时可设 30~50）
}
```

## 常见问题

- **音域不对？** 如果游戏里用的是旧诗琴（无黑键、范围更窄），把 `config.json` 的 `range` 改小，例如 `{"low": 60, "high": 83}`（C4-B5）。
- **和弦太多太吵？** 用 `--chord-mode top`，只留最高音旋律。
- **AI 扒谱时间抖得厉害？** 在 config 里把 `quantize_ms` 设为 30~50，把音符对齐到网格。
- **有多条音轨？** 先用 `--preview` 看每轨效果，再用 `--track N` 挑主旋律那一轨。
- **转换后仍然太快？** 用 `--tempo 1.5` 或加大 config 里的 `min_gap_ms`。

## 测试

自带 `sample.mid` 可直接测试：

```powershell
python midi_to_genshin.py sample.mid --preview
```
