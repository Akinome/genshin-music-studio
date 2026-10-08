# Third-Party Notices

This project uses or references the following open-source projects and assets.

## Basic Pitch

- Project: Spotify Basic Pitch
- License: Apache License 2.0
- Usage: The ONNX model `GenshinMusicStudio.WinUI/Assets/Models/nmp.onnx` is redistributed for local transcription.
- Upstream: https://github.com/spotify/basic-pitch

## BetterGI

- Project: BetterGI / Better Genshin Impact
- License: GNU General Public License v3.0
- Usage: UI structure and visual style were used as a design reference. No BetterGI source files are redistributed in this repository.
- Upstream: https://github.com/babalae/better-genshin-impact

## genshin.music

- Project: genshin.music (Specy)
- License: MIT
- Usage: The Genshin instrument samples under `GenshinMusicStudio.WinUI/Assets/Instruments/`
  (per-key game audio captures and `meta.json` voicing configs) come from this project, and its
  key layout, approach-ring playback visualization, and sustain/release audio behavior are used
  as a design reference.
- Upstream: https://github.com/Specy/genshin-music

## NAudio

- Project: NAudio
- License: MIT
- Usage: Runtime dependency for polyphonic sample playback (instrument audio mixing).

## Optional Runtime Dependencies

The following packages are installed separately at runtime and are not bundled in this repository:

- yt-dlp
- ffmpeg
- basic-pitch
- piano_transcription_inference
- Demucs
- torchcrepe
- librosa
- NAudio

Each dependency remains subject to its own license and distribution terms.
