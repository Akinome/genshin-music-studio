using GenshinMusicStudio_WinUI.Services;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: NativeInferenceTest <audio> <raw.mid> <melody.mid>");
    return 1;
}

var model = Path.GetFullPath(Path.Combine("GenshinMusicStudio.WinUI", "Assets", "Models", "nmp.onnx"));
using var engine = new BasicPitchNative(model);
Console.WriteLine("running native ONNX inference...");
var notes = await engine.TranscribeAsync(args[0]);
Console.WriteLine($"raw notes: {notes.Count}");
if (notes.Count > 0)
{
    var duration = notes.Max(n => n.End);
    NativeMidiWriter.WriteNotes(args[1], notes, duration);
    var melody = NativeArrangement.ExtractMelody(notes, 60);
    NativeMidiWriter.WriteNotes(args[2], melody, duration);
    Console.WriteLine($"melody notes: {melody.Count}");
}
return 0;
