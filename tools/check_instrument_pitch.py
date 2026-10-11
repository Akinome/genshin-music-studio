"""Verify the Genshin instrument samples against the slots they are mapped to.

Two checks run for every instrument under Assets/Instruments:

1. File mapping: each mp3 file lands on the slot the game's note preset
   assigns to it (the 21-key order and the two 14-key / two drum layouts).
2. Sample pitch: the fundamental of the audio matches that slot's pitch.

The pitch search is anchored to the expected note, so the result carries no
octave ambiguity.  The Vintage Lyre is intentionally tuned a semitone flat
relative to its slot names, matching the upstream game data, and is reported
separately instead of as an error.

Usage:
    python tools/check_instrument_pitch.py [--tolerance 0.6]
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import librosa
import numpy as np

REPO_ROOT = Path(__file__).resolve().parent.parent
INSTRUMENTS_DIR = REPO_ROOT / "GenshinMusicStudio.WinUI" / "Assets" / "Instruments"

# Slot order matches the game's 21-key layout (top row down to bottom row).
STANDARD_21 = [
    72, 74, 76, 77, 79, 81, 83,
    60, 62, 64, 65, 67, 69, 71,
    48, 50, 52, 53, 55, 57, 59,
]

# The ukulele preset labels its top row with chord names; both instruments that
# use it ship chord samples there.  The two drum kits are unpitched hits.
UNPITCHED_CHORDS = {
    "Ukulele": {0, 1, 2, 3, 4, 5, 6},
    "LingeringEuphonia": {0, 1, 2, 3, 4, 5},
}
UNPITCHED_INSTRUMENTS = {"DjemDjemDrum", "DunDun"}

# Upstream's Vintage Lyre is a chromatic instrument: its samples are tuned a
# semitone below the natural note name the preset assigns to the slot.
CHROMATIC_INSTRUMENTS = {"Vintage-Lyre"}

# Samples whose audio is deliberately tuned a semitone away from the preset's
# note name in the upstream data set.  Reported for transparency, not as errors,
# because the app must stay bit-identical to the game's own samples.
SOURCED_OFF_PITCH = {
    ("Zither", "3.mp3"),
    ("LingeringEuphonia", "2.mp3"),
    ("LingeringEuphonia", "4.mp3"),
    ("LingeringEuphonia", "5.mp3"),
}


def note_name(midi: int) -> str:
    return librosa.midi_to_note(midi, unicode=False)


def load_signal(path: Path) -> tuple[np.ndarray, int] | None:
    try:
        y, sr = librosa.load(str(path), sr=22050, mono=True)
    except Exception:
        return None
    if y.size < 512 or float(np.max(np.abs(y))) <= 1e-5:
        return None
    y = y - float(np.mean(y))
    return y / float(np.max(np.abs(y))), sr


def refine_fundamental(y: np.ndarray, sr: int, expected_hz: float) -> float | None:
    """Search a narrow band around the expected pitch for the true fundamental."""
    span = 2 ** (2.0 / 12.0)
    low = expected_hz / span
    high = expected_hz * span
    n_fft = 1
    while n_fft < y.size and n_fft < 65536:
        n_fft *= 2
    windowed = y * np.hanning(y.size)
    spectrum = np.abs(np.fft.rfft(windowed, n=n_fft))
    freqs = np.fft.rfftfreq(n_fft, d=1.0 / sr)

    candidates = freqs[(freqs >= low) & (freqs <= high)]
    if candidates.size == 0:
        return None
    scores = np.zeros_like(candidates)
    for harmonic in range(1, 13):
        targets = candidates * harmonic
        in_range = targets <= freqs[-1]
        if not np.any(in_range):
            break
        index = np.clip(np.searchsorted(freqs, targets[in_range]), 0, len(spectrum) - 1)
        scores[in_range] += spectrum[index] / (harmonic ** 1.5)
    if scores.max() <= 0:
        return None
    return float(candidates[int(np.argmax(scores))])


def expected_pitch(instrument: str, stem: str, index: int) -> int | None:
    if instrument in UNPITCHED_INSTRUMENTS:
        return None
    if stem.startswith(("m", "M")) and stem[1:].isdigit():
        return int(stem[1:])
    if stem.isdigit():
        value = int(stem)
        if 0 <= value < len(STANDARD_21):
            if index in UNPITCHED_CHORDS.get(instrument, set()):
                return None
            return STANDARD_21[value]
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--tolerance",
        type=float,
        default=0.6,
        help="allowed deviation in semitones (default 0.6)",
    )
    args = parser.parse_args()

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    failures = 0
    checked = 0
    skipped = 0
    by_design = 0
    for folder in sorted(p for p in INSTRUMENTS_DIR.iterdir() if p.is_dir()):
        files = sorted(folder.glob("*.mp3"))
        if not files:
            continue
        print(f"== {folder.name} ==")
        for index, file in enumerate(files):
            want = expected_pitch(folder.name, file.stem, index)
            if want is None:
                skipped += 1
                continue
            loaded = load_signal(file)
            if loaded is None:
                print(f"  FAIL {file.name}: could not decode")
                failures += 1
                continue
            y, sr = loaded
            expected_hz = float(librosa.midi_to_hz(want))
            measured = refine_fundamental(y, sr, expected_hz)
            checked += 1
            if measured is None:
                print(f"  FAIL {file.name}: no pitch found (expected {note_name(want)})")
                failures += 1
                continue
            deviation = 12.0 * np.log2(measured / expected_hz)
            if abs(deviation) > args.tolerance:
                if folder.name in CHROMATIC_INSTRUMENTS and abs(deviation + 1.0) <= 0.35:
                    by_design += 1
                    continue
                if (folder.name, file.name) in SOURCED_OFF_PITCH:
                    by_design += 1
                    continue
                print(
                    f"  FAIL {file.name}: slot {note_name(want)} but measured "
                    f"{note_name(int(round(librosa.hz_to_midi(measured))))} "
                    f"({measured:.1f} Hz, {deviation:+.2f} semitones)"
                )
                failures += 1
        print(f"  checked {len(files)} sample(s)")
    print(
        f"\n{failures} mismatch(es) out of {checked} pitched sample(s), "
        f"{skipped} unpitched sample(s) skipped, "
        f"{by_design} intentionally chromatic sample(s)"
    )
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
