namespace GenshinMusicStudio.Core;

/// <summary>Represents one note produced by the native transcription pipeline.</summary>
/// <param name="Start">Start time in seconds.</param>
/// <param name="End">End time in seconds.</param>
/// <param name="Pitch">MIDI pitch.</param>
/// <param name="Amplitude">Normalized note strength between zero and one.</param>
public sealed record NativeNote(double Start, double End, int Pitch, double Amplitude);
