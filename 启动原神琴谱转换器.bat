@echo off
cd /d "%~dp0"
python genshin_midi_gui.py
if errorlevel 1 pause
