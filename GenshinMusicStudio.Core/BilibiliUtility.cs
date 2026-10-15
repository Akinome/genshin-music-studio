using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenshinMusicStudio.Core;

/// <summary>One Bilibili search result independent of the WinUI model layer.</summary>
public sealed record BilibiliResult(
    string Bvid,
    string Title,
    string Author,
    string Duration,
    long Play,
    string Pic);

/// <summary>Pure parsing, formatting and wbi-signing helpers for Bilibili search.</summary>
public static class BilibiliUtility
{
    private static readonly int[] MixinKeyEncTab =
    {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
        22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52,
    };

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BvId = new("^BV[0-9A-Za-z]{10}$", RegexOptions.Compiled);

    /// <summary>Normalizes a Bilibili URL or BV identifier, or returns null for other input.</summary>
    /// <param name="input">Raw user input.</param>
    /// <returns>A canonical Bilibili URL or null.</returns>
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

    /// <summary>Builds a search keyword and optionally appends the original-lyre hint.</summary>
    /// <param name="keyword">User-provided keyword.</param>
    /// <param name="appendYuanQin">True to append <c>原琴</c> when absent.</param>
    /// <returns>The normalized keyword.</returns>
    public static string BuildKeyword(string keyword, bool appendYuanQin)
    {
        keyword = keyword.Trim();
        if (!appendYuanQin || keyword.Length == 0) return keyword;
        return keyword.Contains("原琴", StringComparison.Ordinal) ? keyword : keyword + " 原琴";
    }

    /// <summary>Formats a play count using Chinese units.</summary>
    /// <param name="play">Raw play count.</param>
    /// <returns>A compact display string.</returns>
    public static string FormatPlay(long play)
    {
        if (play >= 100_000_000) return (play / 100_000_000.0).ToString("0.0") + " 亿播放";
        if (play >= 10_000) return (play / 10_000.0).ToString("0.0") + " 万播放";
        return play + " 播放";
    }

    /// <summary>Strips Bilibili's search-result HTML and decodes entities.</summary>
    /// <param name="value">Raw title or author text.</param>
    /// <returns>Plain display text.</returns>
    public static string CleanText(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return WebUtility.HtmlDecode(HtmlTag.Replace(value, string.Empty)).Trim();
    }

    /// <summary>Parses a Bilibili search response into lightweight result records.</summary>
    /// <param name="json">Raw Bilibili JSON response.</param>
    /// <returns>Valid video results.</returns>
    public static List<BilibiliResult> ParseResults(string json)
    {
        var list = new List<BilibiliResult>();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            throw new InvalidOperationException(
                $"B 站返回错误 {code.GetInt32()}：{message ?? "未知"}（可在上方提供 cookies.txt 后重试）");
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

            list.Add(new BilibiliResult(
                bvidValue,
                title,
                CleanText(GetString(item, "author")),
                GetString(item, "duration"),
                GetLong(item, "play"),
                pic));
        }

        return list;
    }

    /// <summary>Extracts the file-name portion of a wbi image or sub key URL.</summary>
    /// <param name="url">URL from the Bilibili nav response.</param>
    /// <returns>The key without its extension.</returns>
    public static string ExtractWbiKey(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        var name = url[(url.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>Builds the 32-character mixin key used for wbi signatures.</summary>
    /// <param name="imgKey">Image key from the nav response.</param>
    /// <param name="subKey">Sub key from the nav response.</param>
    /// <returns>The mixin key.</returns>
    public static string BuildMixinKey(string imgKey, string subKey)
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

    /// <summary>Builds a sorted, URL-encoded query string and appends its wbi signature.</summary>
    /// <param name="parameters">Unsigned parameters.</param>
    /// <param name="mixinKey">Mixin key from <see cref="BuildMixinKey"/>.</param>
    /// <returns>The signed query string.</returns>
    public static string BuildSignedQuery(IEnumerable<KeyValuePair<string, string>> parameters, string mixinKey)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(mixinKey);

        var pairs = parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(FilterSignValue(pair.Value)));
        var query = string.Join("&", pairs);
        return query + "&w_rid=" + Md5Hex(query + mixinKey);
    }

    internal static string FilterSignValue(string value) =>
        value.Replace("!", string.Empty)
             .Replace("'", string.Empty)
             .Replace("(", string.Empty)
             .Replace(")", string.Empty)
             .Replace("*", string.Empty);

    internal static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

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
