@echo off
cd /d "%~dp0"
python genshin_music_studio.py
if errorlevel 1 pause
