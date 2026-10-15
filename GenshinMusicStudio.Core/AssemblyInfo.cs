using System.Runtime.CompilerServices;

// Core tests exercise internal parsing helpers directly so synthetic MIDI
// edge cases do not require exposing implementation details as public API.
[assembly: InternalsVisibleTo("GenshinMusicStudio.Core.Tests")]
