using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace GenshinMusicStudio_WinUI.Services;

/// <summary>一条 B 站视频搜索结果。</summary>
public sealed class BilibiliVideo : INotifyPropertyChanged
{
    public string Bvid { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public long Play { get; init; }
    public string Pic { get; init; } = string.Empty;

    private ImageSource? cover;
    public ImageSource? Cover
    {
        get => cover;
        set
        {
            if (ReferenceEquals(cover, value)) return;
            cover = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Url => string.IsNullOrEmpty(Bvid) ? string.Empty : "https://www.bilibili.com/video/" + Bvid;

    public string Meta => $"{Author}  ·  {Duration}  ·  {BilibiliUtility.FormatPlay(Play)}";

    /// <summary>与「成熟的原琴」曲库一致的命名：UP主 - 标题。</summary>
    public string SuggestedName => $"{Author} - {Title}";
}

/// <summary>
/// B 站视频搜索的原生实现，内部完成 wbi 签名（w_rid / wts），不依赖 Python 或第三方库。
/// 搜索接口需要匿名 cookie（buvid3 等），这里先访问首页自动获取，也支持传入 Netscape 格式的 cookies.txt。
/// </summary>
public static class BilibiliSearch
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>输入是链接或 BV 号时直接返回规范化的视频地址，否则返回 null。</summary>
    public static string? TryNormalizeUrl(string input)
        => BilibiliUtility.TryNormalizeUrl(input);

    /// <summary>按需在关键词后面补上「原琴」，关键词里已经有就不再重复添加。</summary>
    public static string BuildKeyword(string keyword, bool appendYuanQin)
        => BilibiliUtility.BuildKeyword(keyword, appendYuanQin);

    /// <summary>
    /// 下载封面并转成 BitmapImage。B 站图床有防盗链，必须带 bilibili 的 Referer，否则会返回 403。
    /// </summary>
    public static async Task<ImageSource?> LoadCoverAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");

        var bytes = await http.GetByteArrayAsync(url, cancellationToken);
        if (bytes.Length == 0) return null;

        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    public static async Task<List<BilibiliVideo>> SearchAsync(string keyword, int page = 1,
        string? cookiesFile = null, CancellationToken cancellationToken = default)
    {
        var container = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = container,
            AutomaticDecompression = DecompressionMethods.All,
        };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");

        // 1) 先访问一次首页，拿到 buvid3 等匿名 cookie，否则搜索接口容易返回 412。
        try
        {
            using var home = await http.GetAsync("https://www.bilibili.com/", cancellationToken);
        }
        catch
        {
            // 首页拿不到也没关系，后面还有 cookies 文件兜底。
        }
        LoadNetscapeCookies(container, cookiesFile);

        // 2) nav 接口提供 wbi 的 img_key / sub_key。
        var navJson = await http.GetStringAsync("https://api.bilibili.com/x/web-interface/nav", cancellationToken);
        using var nav = JsonDocument.Parse(navJson);
        var navData = nav.RootElement.GetProperty("data");
        if (!navData.TryGetProperty("wbi_img", out var wbi))
        {
            throw new InvalidOperationException("B 站未返回 wbi 密钥，请稍后重试，或在上面配置一个 cookies.txt。");
        }

        var imgKey = BilibiliUtility.ExtractWbiKey(wbi.GetProperty("img_url").GetString());
        var subKey = BilibiliUtility.ExtractWbiKey(wbi.GetProperty("sub_url").GetString());
        var mixinKey = BilibiliUtility.BuildMixinKey(imgKey, subKey);

        // 3) 组装参数并生成 wbi 签名。
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = keyword,
            ["order"] = "totalrank",
            ["page"] = page.ToString(),
            ["page_size"] = "20",
            ["platform"] = "pc",
            ["search_type"] = "video",
            ["web_location"] = "1430650",
            ["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        };
        var query = BilibiliUtility.BuildSignedQuery(parameters, mixinKey);

        var searchJson = await http.GetStringAsync(
            "https://api.bilibili.com/x/web-interface/wbi/search/type?" + query, cancellationToken);
        return MapResults(BilibiliUtility.ParseResults(searchJson));
    }

    private static List<BilibiliVideo> MapResults(IEnumerable<BilibiliResult> results)
    {
        var list = new List<BilibiliVideo>();
        foreach (var item in results)
        {
            list.Add(new BilibiliVideo
            {
                Bvid = item.Bvid,
                Title = item.Title,
                Author = item.Author,
                Duration = item.Duration,
                Play = item.Play,
                Pic = item.Pic,
            });
        }

        return list;
    }

    private static void LoadNetscapeCookies(CookieContainer container, string? cookiesFile)
    {
        if (string.IsNullOrWhiteSpace(cookiesFile) || !File.Exists(cookiesFile)) return;
        foreach (var line in File.ReadLines(cookiesFile))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length < 7) continue;
            try
            {
                container.Add(new Cookie(parts[5], parts[6], parts[2], parts[0].TrimStart('.')));
            }
            catch
            {
                // 单条 cookie 格式异常时忽略，不中断整个搜索。
            }
        }
    }

}
