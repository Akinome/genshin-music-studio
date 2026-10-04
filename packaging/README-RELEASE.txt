Genshin Music Studio - Windows x64
==================================

启动方式：
1. 双击“Start.bat”
2. 或直接运行 GenshinMusicStudio.WinUI.exe

基础功能：
- WinUI 3 原生界面
- Basic Pitch ONNX 原生扒谱
- raw / optimized / genshin 三份 MIDI 输出
- 模型评估

可选外部工具：
- yt-dlp：视频和音频下载
- ffmpeg：音频解码和格式转换

高级模型：
双击“install_ai_env_uv.bat”，通过 uv 安装 Python 3.11 环境。
该步骤会下载 PyTorch、Piano Transcription、Demucs 和 CREPE，体积较大。

高级模型包括：
- Piano Transcription 钢琴专用模型
- Demucs 人声/伴奏分离
- CREPE 单音主旋律
- librosa pyin 主旋律回退

许可证：
GPL-3.0。第三方说明见仓库 THIRD_PARTY_NOTICES.md。
