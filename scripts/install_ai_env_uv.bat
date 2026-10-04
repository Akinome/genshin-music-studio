@echo off
cd /d "%~dp0.."
uv venv --python 3.11 .venv-ai
uv pip install --python .venv-ai\Scripts\python.exe torch torchaudio --index-url https://download.pytorch.org/whl/cpu
uv pip install --python .venv-ai\Scripts\python.exe -U basic-pitch yt-dlp librosa soundfile "setuptools<81"
uv pip install --python .venv-ai\Scripts\python.exe piano_transcription_inference demucs torchcrepe
pause
