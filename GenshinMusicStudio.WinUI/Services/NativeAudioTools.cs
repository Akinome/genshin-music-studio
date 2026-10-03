using System.Diagnostics;
using System.Text;

namespace GenshinMusicStudio_WinUI.Services;

public static class NativeAudioTools
{
    public static string? FindTool(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in new[] { ".exe", ".bat", ".cmd", string.Empty })
            {
                var candidate = Path.Combine(directory.Trim('"'), name + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static async Task<float[]> DecodeToMono22050Async(string inputPath, CancellationToken cancellationToken)
    {
        var ffmpeg = FindTool("ffmpeg") ?? throw new FileNotFoundException("未找到 ffmpeg，请先安装 ffmpeg。");
        var startInfo = CreateProcessStartInfo(ffmpeg,
            $"-v error -i {Quote(inputPath)} -f f32le -acodec pcm_f32le -ac 1 -ar 22050 pipe:1");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var memory = new MemoryStream();
        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(memory, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await copyTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("ffmpeg 解码失败：" + error);
            }
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        var bytes = memory.ToArray();
        var samples = new float[bytes.Length / 4];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToSingle(bytes, i * 4);
        }
        return samples;
    }

    public static async Task<List<string>> DownloadMediaAsync(string url, string outputDirectory, bool video,
        string? cookiesFile, string? browser, bool allowPlaylist, Action<string>? log, CancellationToken cancellationToken)
    {
        var ytDlp = FindTool("yt-dlp") ?? throw new FileNotFoundException("未找到 yt-dlp，请先安装 yt-dlp。");
        var ffmpeg = FindTool("ffmpeg") ?? throw new FileNotFoundException("未找到 ffmpeg，请先安装 ffmpeg。");
        Directory.CreateDirectory(outputDirectory);
        var before = Directory.Exists(outputDirectory)
            ? Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var args = new StringBuilder();
        args.Append("--newline --no-warnings --encoding utf-8 ");
        args.Append($"-o {Quote(Path.Combine(outputDirectory, "%(title).180s.%(ext)s"))} ");
        if (!allowPlaylist) args.Append("--no-playlist ");
        if (!string.IsNullOrWhiteSpace(cookiesFile)) args.Append($"--cookies {Quote(cookiesFile)} ");
        if (!string.IsNullOrWhiteSpace(browser) && browser != "不使用") args.Append($"--cookies-from-browser {browser} ");
        if (video)
        {
            args.Append("-f bv*+ba/b --merge-output-format mp4 ");
        }
        else
        {
            args.Append("-x --audio-format wav --audio-quality 0 ");
        }
        args.Append(Quote(url));

        await RunTextProcessAsync(ytDlp, args.ToString(), log, cancellationToken);
        var extensions = video ? new[] { ".mp4", ".mkv", ".webm" } : new[] { ".wav", ".mp3", ".m4a", ".flac", ".ogg" };
        var files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !before.Contains(path) && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (files.Count == 0)
        {
            files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(1)
                .ToList();
        }
        if (files.Count == 0) throw new InvalidOperationException("下载完成但没有找到媒体文件。");
        return files;
    }

    private static async Task RunTextProcessAsync(string fileName, string arguments, Action<string>? log, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateProcessStartInfo(fileName, arguments) };
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        var stdoutTask = ReadLinesAsync(process.StandardOutput, log);
        var stderrTask = ReadLinesAsync(process.StandardError, log);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
            if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(fileName)} 退出码 {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string>? log)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line)) log?.Invoke(line);
        }
    }

    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        Environment =
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["LANG"] = "zh_CN.UTF-8",
            ["LC_ALL"] = "zh_CN.UTF-8",
        },
    };

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
