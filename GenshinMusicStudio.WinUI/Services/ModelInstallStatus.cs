namespace GenshinMusicStudio_WinUI.Services;

public static class ModelInstallStatus
{
    public static string[] RequiredKeys(string? modelTag) => modelTag switch
    {
        "basic_pitch_python" => new[] { "Basic Pitch (Python)" },
        "piano_transcription" => new[] { "PyTorch", "Piano Transcription" },
        "demucs_vocals_basic" => new[] { "PyTorch", "Demucs", "Basic Pitch (Python)" },
        "demucs_accompaniment_piano" => new[] { "PyTorch", "Demucs", "Piano Transcription" },
        "demucs_vocals_crepe" => new[] { "PyTorch", "Demucs", "CREPE" },
        "crepe" => new[] { "PyTorch", "CREPE" },
        "librosa_pyin" => new[] { "librosa pyin" },
        _ => new[] { "Basic Pitch ONNX" },
    };

    public static (bool Installed, string Text) Evaluate(string? modelTag, Dictionary<string, string?>? status)
    {
        if (status is null) return (true, "状态未知");
        var missing = RequiredKeys(modelTag)
            .Where(key => string.IsNullOrWhiteSpace(status.GetValueOrDefault(key)))
            .ToList();
        return missing.Count == 0
            ? (true, "已安装")
            : (false, "缺少: " + string.Join("、", missing));
    }
}
