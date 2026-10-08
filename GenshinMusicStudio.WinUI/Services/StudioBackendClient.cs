using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenshinMusicStudio_WinUI.Services;

public sealed class BackendRequest
{
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
    [JsonPropertyName("urls")] public List<string>? Urls { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("reference_path")] public string? ReferencePath { get; set; }
    [JsonPropertyName("prediction_path")] public string? PredictionPath { get; set; }
    [JsonPropertyName("work_dir")] public string? WorkDir { get; set; }
    [JsonPropertyName("out_dir")] public string? OutDir { get; set; }
    [JsonPropertyName("cookies_file")] public string? CookiesFile { get; set; }
    [JsonPropertyName("browser")] public string? Browser { get; set; }
    [JsonPropertyName("allow_playlist")] public bool AllowPlaylist { get; set; }
    [JsonPropertyName("min_gap_ms")] public int MinGapMs { get; set; } = 60;
    [JsonPropertyName("melody_only")] public bool MelodyOnly { get; set; } = false;
    [JsonPropertyName("preserve_duration")] public bool PreserveDuration { get; set; } = true;
    [JsonPropertyName("model")] public string? Model { get; set; } = "basic_pitch_onnx";
    [JsonPropertyName("install_targets")] public List<string>? InstallTargets { get; set; }
}

public sealed class StudioEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("midi")] public string? Midi { get; set; }
    [JsonPropertyName("dependencies")] public Dictionary<string, string?>? Dependencies { get; set; }
}

public sealed class StudioBackendClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object gate = new();
    private Process? process;
    private bool cancelled;
    private CancellationTokenSource? nativeCancellation;

    public event Action<StudioEvent>? EventReceived;
    public event Action<bool>? RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return process is { HasExited: false } || nativeCancellation is not null;
            }
        }
    }

    public static string RepoRoot
    {
        get
        {
            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "backend", "studio_backend.py")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }
    }

    private static string PythonExecutable
    {
        get
        {
            var localPython = Path.Combine(RepoRoot, ".venv-ai", "Scripts", "python.exe");
            return File.Exists(localPython) ? localPython : "python";
        }
    }

    public async Task<Dictionary<string, string?>?> RunAsync(BackendRequest request)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("已有任务正在运行");
        }

        if (NativeStudioService.CanHandle(request))
        {
            nativeCancellation = new CancellationTokenSource();
            try
            {
                return await NativeStudioService.RunAsync(
                    request,
                    evt => EventReceived?.Invoke(evt),
                    running => RunningChanged?.Invoke(running),
                    nativeCancellation.Token);
            }
            finally
            {
                nativeCancellation.Dispose();
                nativeCancellation = null;
            }
        }

        cancelled = false;
        var script = Path.Combine(RepoRoot, "backend", "studio_backend.py");
        if (!File.Exists(script))
        {
            throw new FileNotFoundException("找不到 Python 后端脚本", script);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = PythonExecutable,
            Arguments = Quote(script),
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["TF_CPP_MIN_LOG_LEVEL"] = "2";

        var running = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        lock (gate)
        {
            process = running;
        }

        try
        {
            running.Start();
            RunningChanged?.Invoke(true);
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            await running.StandardInput.WriteLineAsync(payload);
            running.StandardInput.Close();

            var resultSource = new TaskCompletionSource<Dictionary<string, string?>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stdoutTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await running.StandardOutput.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<StudioEvent>(line, JsonOptions);
                        if (evt is null) continue;
                        if (!string.IsNullOrWhiteSpace(evt.Text) && evt.Type != "error")
                        {
                            EventReceived?.Invoke(new StudioEvent { Type = "log", Text = evt.Text });
                        }
                        if (evt.Type == "error")
                        {
                            resultSource.TrySetException(new InvalidOperationException(evt.Text ?? "后端执行失败"));
                        }
                        else if (evt.Type == "done")
                        {
                            resultSource.TrySetResult(evt.Dependencies);
                        }
                        EventReceived?.Invoke(evt);
                    }
                    catch (JsonException)
                    {
                        EventReceived?.Invoke(new StudioEvent { Type = "log", Text = line });
                    }
                }
            });

            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await running.StandardError.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        EventReceived?.Invoke(new StudioEvent { Type = "log", Text = line });
                    }
                }
            });

            await running.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            if (cancelled)
            {
                throw new OperationCanceledException("任务已停止");
            }
            if (running.ExitCode != 0 && !resultSource.Task.IsCompleted)
            {
                throw new InvalidOperationException($"Python 后端退出码 {running.ExitCode}");
            }
            return await resultSource.Task;
        }
        finally
        {
            lock (gate)
            {
                process = null;
            }
            RunningChanged?.Invoke(false);
            running.Dispose();
        }
    }

    public void Cancel()
    {
        cancelled = true;
        nativeCancellation?.Cancel();
        Process? running;
        lock (gate)
        {
            running = process;
        }
        if (running is { HasExited: false })
        {
            try
            {
                running.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may have exited between the check and Kill.
            }
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
