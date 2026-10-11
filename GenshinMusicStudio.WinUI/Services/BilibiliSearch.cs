using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public string Meta => $"{Author}  ·  {Duration}  ·  {FormatPlay(Play)}";

    /// <summary>与「成熟的原琴」曲库一致的命名：UP主 - 标题。</summary>
    public string SuggestedName => $"{Author} - {Title}";

    private static string FormatPlay(long play)
    {
        if (play >= 100_000_000) return (play / 100_000_000.0).ToString("0.0") + " 亿播放";
        if (play >= 10_000) return (play / 10_000.0).ToString("0.0") + " 万播放";
        return play + " 播放";
    }
}

/// <summary>
/// B 站视频搜索的原生实现，内部完成 wbi 签名（w_rid / wts），不依赖 Python 或第三方库。
/// 搜索接口需要匿名 cookie（buvid3 等），这里先访问首页自动获取，也支持传入 Netscape 格式的 cookies.txt。
/// </summary>
public static class BilibiliSearch
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    // B 站 wbi 签名固定的字符重排表。
    private static readonly int[] MixinKeyEncTab =
    {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52,
    };

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BvId = new("^BV[0-9A-Za-z]{10}$", RegexOptions.Compiled);

    /// <summary>输入是链接或 BV 号时直接返回规范化的视频地址，否则返回 null。</summary>
    public static string? TryNormalizeUrl(string input)
    {
        input = input.Trim();
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return input.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase) ||
                   input.Contains("b23.tv", StringComparison.OrdinalIgnoreCase)
                ? input
                : null;
        }

        return BvId.IsMatch(input) ? "https://www.bilibili.com/video/" + input : null;
    }

    /// <summary>按需在关键词后面补上「原琴」，关键词里已经有就不再重复添加。</summary>
    public static string BuildKeyword(string keyword, bool appendYuanQin)
    {
        keyword = keyword.Trim();
        if (!appendYuanQin || keyword.Length == 0) return keyword;
        return keyword.Contains("原琴", StringComparison.Ordinal) ? keyword : keyword + " 原琴";
    }

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

        var imgKey = ExtractWbiKey(wbi.GetProperty("img_url").GetString());
        var subKey = ExtractWbiKey(wbi.GetProperty("sub_url").GetString());
        var mixinKey = BuildMixinKey(imgKey, subKey);

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
        var query = BuildSignedQuery(parameters, mixinKey);

        var searchJson = await http.GetStringAsync(
            "https://api.bilibili.com/x/web-interface/wbi/search/type?" + query, cancellationToken);
        return ParseResults(searchJson);
    }

    private static List<BilibiliVideo> ParseResults(string json)
    {
        var list = new List<BilibiliVideo>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new InvalidOperationException($"B 站返回错误 {code.GetInt32()}：{message ?? "未知"}（可在上方提供 cookies.txt 后重试）");
        }

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("bvid", out var bvid) || bvid.ValueKind != JsonValueKind.String) continue;
            var bvidValue = bvid.GetString();
            if (string.IsNullOrWhiteSpace(bvidValue)) continue;

            var title = CleanText(GetString(item, "title"));
            if (string.IsNullOrWhiteSpace(title)) continue;

            var pic = GetString(item, "pic");
            if (pic.StartsWith("//", StringComparison.Ordinal)) pic = "https:" + pic;

            list.Add(new BilibiliVideo
            {
                Bvid = bvidValue,
                Title = title,
                Author = CleanText(GetString(item, "author")),
                Duration = GetString(item, "duration"),
                Play = GetLong(item, "play"),
                Pic = pic,
            });
        }

        return list;
    }

    private static string ExtractWbiKey(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var name = url[(url.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string BuildMixinKey(string imgKey, string subKey)
    {
        var raw = imgKey + subKey;
        var builder = new StringBuilder(32);
        foreach (var index in MixinKeyEncTab)
        {
            if (index >= raw.Length) continue;
            builder.Append(raw[index]);
            if (builder.Length == 32) break;
        }
        if (builder.Length < 32) throw new InvalidOperationException("wbi 密钥长度不足，无法生成签名。");
        return builder.ToString();
    }

    private static string BuildSignedQuery(IEnumerable<KeyValuePair<string, string>> parameters, string mixinKey)
    {
        var pairs = parameters
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(FilterSignValue(kv.Value)));
        var query = string.Join("&", pairs);
        return query + "&w_rid=" + Md5Hex(query + mixinKey);
    }

    // JS encodeURIComponent 不会转义这些字符，签名前要先剔除，否则 w_rid 对不上。
    private static string FilterSignValue(string value) =>
        value.Replace("!", string.Empty)
             .Replace("'", string.Empty)
             .Replace("(", string.Empty)
             .Replace(")", string.Empty)
             .Replace("*", string.Empty);

    private static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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

    private static string CleanText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return WebUtility.HtmlDecode(HtmlTag.Replace(value, string.Empty)).Trim();
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty,
        };
    }

    private static long GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            _ => 0,
        };
    }
}
