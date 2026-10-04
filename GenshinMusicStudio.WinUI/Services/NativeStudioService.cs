namespace GenshinMusicStudio_WinUI.Services;

public static class NativeStudioService
{
    public static bool CanHandle(BackendRequest request)
    {
        var action = request.Action;
        if (action == "status") return true;
        if (action is "download_audio" or "download_video") return true;
        if (!string.IsNullOrWhiteSpace(request.Model) && request.Model != "basic_pitch_onnx") return false;
        if (action is "full_pipeline" or "transcribe_url") return true;
        var path = request.Path ?? string.Empty;
        var isMidi = path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase);
        return action is "local_full" or "transcribe" && !isMidi;
    }

    public static async Task<Dictionary<string, string?>?> RunAsync(BackendRequest request, Action<StudioEvent>? emit,
        Action<bool>? running, CancellationToken cancellationToken)
    {
        running?.Invoke(true);
        try
        {
            return request.Action switch
            {
                "status" => Status(emit),
                "download_audio" => await DownloadAsync(request, false, emit, cancellationToken),
                "download_video" => await DownloadAsync(request, true, emit, cancellationToken),
                "full_pipeline" => await FullPipelineAsync(request, emit, cancellationToken),
                "transcribe_url" => await TranscribeUrlAsync(request, emit, cancellationToken),
                "local_full" => await LocalFullAsync(request, emit, cancellationToken),
                "transcribe" => await TranscribeOnlyAsync(request, emit, cancellationToken),
                _ => throw new InvalidOperationException("原生服务不支持操作：" + request.Action),
            };
        }
        finally
        {
            running?.Invoke(false);
        }
    }

    private static Dictionary<string, string?> Status(Action<StudioEvent>? emit)
    {
        var status = new Dictionary<string, string?>
        {
            ["yt-dlp"] = NativeAudioTools.FindTool("yt-dlp"),
            ["ffmpeg"] = NativeAudioTools.FindTool("ffmpeg"),
            ["Basic Pitch ONNX"] = FindModelPath(),
            ["ONNX Runtime"] = "Microsoft.ML.OnnxRuntime",
            ["AI环境"] = FindVenvPython(),
            ["soundfile"] = FindVenvPackage("soundfile"),
            ["Basic Pitch (Python)"] = FindVenvPackage("basic_pitch", "basic-pitch"),
            ["PyTorch"] = FindVenvPackage("torch"),
            ["Piano Transcription"] = FindVenvPackage("piano_transcription_inference"),
            ["Demucs"] = FindVenvPackage("demucs"),
            ["CREPE"] = FindVenvPackage("torchcrepe"),
            ["librosa pyin"] = FindVenvPackage("librosa"),
        };
        emit?.Invoke(new StudioEvent { Type = "done", Dependencies = status });
        return status;
    }

    private static async Task<Dictionary<string, string?>?> DownloadAsync(BackendRequest request, bool video,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        var urls = request.Urls ?? new List<string>();
        var output = request.WorkDir ?? Path.Combine(StudioBackendClient.RepoRoot, "工作区");
        var files = new List<string>();
        for (var i = 0; i < urls.Count; i++)
        {
            emit?.Invoke(new StudioEvent { Type = "status", Text = $"下载 {(video ? "视频" : "音频")} {i + 1}/{urls.Count}" });
            files.AddRange(await NativeAudioTools.DownloadMediaAsync(urls[i], Path.Combine(output, "downloads"), video,
                request.CookiesFile, request.Browser, request.AllowPlaylist,
                text => emit?.Invoke(new StudioEvent { Type = "log", Text = text }), cancellationToken));
            emit?.Invoke(new StudioEvent { Type = "progress", Value = (i + 1) * 100.0 / Math.Max(1, urls.Count) });
        }
        var done = new StudioEvent { Type = "done", Output = Path.Combine(output, "downloads") };
        emit?.Invoke(done);
        return null;
    }

    private static async Task<Dictionary<string, string?>?> FullPipelineAsync(BackendRequest request,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        var urls = request.Urls ?? new List<string>();
        var work = request.WorkDir ?? Path.Combine(StudioBackendClient.RepoRoot, "工作区");
        var output = request.OutDir ?? Path.Combine(StudioBackendClient.RepoRoot, "AI扒谱输出");
        for (var i = 0; i < urls.Count; i++)
        {
            emit?.Invoke(new StudioEvent { Type = "status", Text = $"原生处理 {i + 1}/{urls.Count}" });
            var files = await NativeAudioTools.DownloadMediaAsync(urls[i], Path.Combine(work, "downloads"), false,
                request.CookiesFile, request.Browser, request.AllowPlaylist,
                text => emit?.Invoke(new StudioEvent { Type = "log", Text = text }), cancellationToken);
            foreach (var audioPath in files)
            {
                await TranscribeAndConvertAsync(audioPath, work, output, request.MinGapMs, emit, cancellationToken);
            }
            emit?.Invoke(new StudioEvent { Type = "progress", Value = (i + 1) * 100.0 / Math.Max(1, urls.Count) });
        }
        emit?.Invoke(new StudioEvent { Type = "done", Output = output });
        return null;
    }

    private static async Task<Dictionary<string, string?>?> TranscribeUrlAsync(BackendRequest request,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        var urls = request.Urls ?? new List<string>();
        var work = request.WorkDir ?? Path.Combine(StudioBackendClient.RepoRoot, "工作区");
        var outDir = request.OutDir ?? Path.Combine(StudioBackendClient.RepoRoot, "AI扒谱输出");
        Directory.CreateDirectory(outDir);
        for (var i = 0; i < urls.Count; i++)
        {
            emit?.Invoke(new StudioEvent { Type = "status", Text = $"仅 AI 扒谱 {i + 1}/{urls.Count}" });
            var files = await NativeAudioTools.DownloadMediaAsync(urls[i], Path.Combine(work, "downloads"), false,
                request.CookiesFile, request.Browser, request.AllowPlaylist,
                text => emit?.Invoke(new StudioEvent { Type = "log", Text = text }), cancellationToken);
            foreach (var audioPath in files)
            {
                using var engine = new BasicPitchNative(FindModelPath());
                var notes = await engine.TranscribeAsync(audioPath, cancellationToken: cancellationToken);
                var output = Path.Combine(outDir, Path.GetFileNameWithoutExtension(audioPath) + "_basic_pitch_native.mid");
                NativeMidiWriter.WriteNotes(output, notes, notes.Count == 0 ? 0 : notes.Max(n => n.End));
                emit?.Invoke(new StudioEvent { Type = "log", Text = $"AI 扒谱完成：{notes.Count} 个音符 -> {output}" });
            }
            emit?.Invoke(new StudioEvent { Type = "progress", Value = (i + 1) * 100.0 / Math.Max(1, urls.Count) });
        }
        emit?.Invoke(new StudioEvent { Type = "done", Output = outDir });
        return null;
    }

    private static async Task<Dictionary<string, string?>?> LocalFullAsync(BackendRequest request,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        var input = request.Path ?? throw new InvalidOperationException("缺少输入文件");
        var outDir = request.OutDir ?? Path.Combine(StudioBackendClient.RepoRoot, "本地处理输出");
        await TranscribeAndConvertAsync(input, Path.Combine(outDir, "中间文件"), outDir, request.MinGapMs, emit, cancellationToken);
        emit?.Invoke(new StudioEvent { Type = "done", Output = outDir });
        return null;
    }

    private static async Task<Dictionary<string, string?>?> TranscribeOnlyAsync(BackendRequest request,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        var input = request.Path ?? throw new InvalidOperationException("缺少输入文件");
        var outDir = request.OutDir ?? Path.Combine(StudioBackendClient.RepoRoot, "本地处理输出", "扒谱MIDI");
        Directory.CreateDirectory(outDir);
        using var engine = new BasicPitchNative(FindModelPath());
        emit?.Invoke(new StudioEvent { Type = "status", Text = "Basic Pitch ONNX 推理" });
        var notes = await engine.TranscribeAsync(input, cancellationToken: cancellationToken);
        var midiPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + "_basic_pitch_native.mid");
        NativeMidiWriter.WriteNotes(midiPath, notes, notes.Count == 0 ? 0 : notes.Max(n => n.End));
        emit?.Invoke(new StudioEvent { Type = "done", Output = outDir, Midi = midiPath });
        return null;
    }

    private static async Task TranscribeAndConvertAsync(string input, string workDir, string outDir, int minGapMs,
        Action<StudioEvent>? emit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outDir);
        using var engine = new BasicPitchNative(FindModelPath());
        emit?.Invoke(new StudioEvent { Type = "status", Text = "Basic Pitch ONNX 推理：" + Path.GetFileName(input) });
        var rawNotes = await engine.TranscribeAsync(input, cancellationToken: cancellationToken);
        if (rawNotes.Count == 0) throw new InvalidOperationException("没有识别到音符。");
        var duration = rawNotes.Max(n => n.End);
        var baseName = Path.GetFileNameWithoutExtension(input);
        var rawDir = Path.Combine(outDir, "raw");
        var optimizedDir = Path.Combine(outDir, "optimized");
        var genshinDir = Path.Combine(outDir, "genshin");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(optimizedDir);
        Directory.CreateDirectory(genshinDir);
        var rawMidi = Path.Combine(rawDir, baseName + "_raw.mid");
        NativeMidiWriter.WriteNotes(rawMidi, rawNotes, duration);
        emit?.Invoke(new StudioEvent { Type = "log", Text = $"原生扒谱完成：{rawNotes.Count} 个音符" });

        var optimizedNotes = NativeArrangement.OptimizeNotes(rawNotes);
        var optimizedMidi = Path.Combine(optimizedDir, baseName + "_optimized.mid");
        NativeMidiWriter.WriteNotes(optimizedMidi, optimizedNotes, duration);
        emit?.Invoke(new StudioEvent { Type = "log", Text = $"符号优化完成：{optimizedNotes.Count} 个音符" });

        var melody = NativeArrangement.ExtractMelody(optimizedNotes, minGapMs);
        if (melody.Count == 0) throw new InvalidOperationException("主旋律提取结果为空。");
        var outputMidi = Path.Combine(genshinDir, baseName + "_genshin.mid");
        NativeMidiWriter.WriteNotes(outputMidi, melody, duration);
        emit?.Invoke(new StudioEvent { Type = "log", Text = $"主旋律完成：{melody.Count} 个音符" });
    }

    private static string FindModelPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "nmp.onnx"),
            Path.Combine(StudioBackendClient.RepoRoot, "GenshinMusicStudio.WinUI", "Assets", "Models", "nmp.onnx"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("找不到内置 Basic Pitch ONNX 模型。");
    }

    private static string? FindVenvPython()
    {
        var candidates = new[]
        {
            Path.Combine(StudioBackendClient.RepoRoot, ".venv-ai", "Scripts", "python.exe"),
            Path.Combine(StudioBackendClient.RepoRoot, ".venv-ai", "bin", "python"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindVenvPackage(params string[] names)
    {
        var sitePackages = Path.Combine(StudioBackendClient.RepoRoot, ".venv-ai", "Lib", "site-packages");
        if (!Directory.Exists(sitePackages)) return null;
        foreach (var name in names)
        {
            var packageDir = Path.Combine(sitePackages, name);
            if (Directory.Exists(packageDir) && Directory.EnumerateFileSystemEntries(packageDir).Any()) return packageDir;
            if (File.Exists(packageDir + ".py")) return packageDir + ".py";
            var distInfo = Directory.EnumerateDirectories(sitePackages, name + "-*.dist-info").FirstOrDefault()
                ?? Directory.EnumerateDirectories(sitePackages, name + ".dist-info").FirstOrDefault();
            if (distInfo is not null) return distInfo;
        }
        return null;
    }
}
