@echo off
cd /d "%~dp0.."
dotnet run --project "GenshinMusicStudio.WinUI\GenshinMusicStudio.WinUI.csproj" -c Debug
if errorlevel 1 pause
