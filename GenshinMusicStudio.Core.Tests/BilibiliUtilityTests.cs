using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class BilibiliUtilityTests
{
    [Fact]
    public void TryNormalizeUrl_AcceptsBvAndBilibiliLinks()
    {
        Assert.Equal(
            "https://www.bilibili.com/video/BV1xx411c7mD",
            BilibiliUtility.TryNormalizeUrl("  BV1xx411c7mD  "));
        Assert.Equal(
            "https://www.bilibili.com/video/BV1xx411c7mD",
            BilibiliUtility.TryNormalizeUrl("https://www.bilibili.com/video/BV1xx411c7mD"));
        Assert.Equal(
            "https://b23.tv/abc",
            BilibiliUtility.TryNormalizeUrl("https://b23.tv/abc"));
        Assert.Null(BilibiliUtility.TryNormalizeUrl("https://example.com/video"));
        Assert.Null(BilibiliUtility.TryNormalizeUrl("not-a-bv"));
    }

    [Theory]
    [InlineData("稻香", true, "稻香 原琴")]
    [InlineData("稻香 原琴", true, "稻香 原琴")]
    [InlineData("稻香", false, "稻香")]
    [InlineData("", true, "")]
    public void BuildKeyword_AddsTheLyreHintOnlyWhenNeeded(string keyword, bool append, string expected)
    {
        Assert.Equal(expected, BilibiliUtility.BuildKeyword(keyword, append));
    }

    [Theory]
    [InlineData(0, "0 播放")]
    [InlineData(9999, "9999 播放")]
    [InlineData(10000, "1.0 万播放")]
    [InlineData(100000000, "1.0 亿播放")]
    public void FormatPlay_UsesChineseUnits(long play, string expected)
    {
        Assert.Equal(expected, BilibiliUtility.FormatPlay(play));
    }

    [Fact]
    public void CleanText_RemovesMarkupAndDecodesEntities()
    {
        Assert.Equal("A & B", BilibiliUtility.CleanText("<em>A</em> &amp; <b>B</b>"));
        Assert.Equal("", BilibiliUtility.CleanText(""));
    }

    [Theory]
    [InlineData("https://i0.hdslb.com/bfs/wbi/abc.png", "abc")]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    public void ExtractWbiKey_RemovesDirectoryAndExtension(string url, string expected)
    {
        Assert.Equal(expected, BilibiliUtility.ExtractWbiKey(url));
    }

    [Fact]
    public void BuildMixinKey_ReturnsThirtyTwoCharactersAndRejectsShortInput()
    {
        var key = BilibiliUtility.BuildMixinKey(new string('a', 32), new string('b', 32));

        Assert.Equal(32, key.Length);
        Assert.Throws<InvalidOperationException>(() => BilibiliUtility.BuildMixinKey("a", "b"));
    }

    [Fact]
    public void BuildSignedQuery_SortsValuesAndAppendsSignature()
    {
        var parameters = new Dictionary<string, string>
        {
            ["keyword"] = "a b",
            ["page"] = "1",
            ["unsafe"] = "a!b",
        };

        var query = BilibiliUtility.BuildSignedQuery(parameters, new string('k', 32));

        Assert.StartsWith("keyword=a%20b&page=1&unsafe=ab&w_rid=", query);
        Assert.Equal(32, query[(query.LastIndexOf("w_rid=", StringComparison.Ordinal) + 6)..].Length);
    }

    [Fact]
    public void BuildSignedQuery_RejectsMissingMixinKey()
    {
        Assert.Throws<ArgumentNullException>(() => BilibiliUtility.BuildSignedQuery(null!, "key"));
        Assert.Throws<ArgumentException>(() =>
            BilibiliUtility.BuildSignedQuery(Array.Empty<KeyValuePair<string, string>>(), ""));
    }

    [Fact]
    public void InternalSigningHelpers_AreDeterministic()
    {
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", BilibiliUtility.Md5Hex("abc"));
        Assert.Equal("ab", BilibiliUtility.FilterSignValue("a!'()*b"));
    }

    [Fact]
    public void ParseResults_MapsValidItemsAndSkipsInvalidOnes()
    {
        const string json = """
            {
              "code": 0,
              "data": {
                "result": [
                  {
                    "bvid": "BV1xx411c7mD",
                    "title": "<em>歌曲</em>&amp;测试",
                    "author": "<b>UP</b>",
                    "duration": "3:21",
                    "play": 12345,
                    "pic": "//i0.hdslb.com/cover.jpg"
                  },
                  { "bvid": "", "title": "missing id" },
                  { "bvid": "BV0000000001", "title": "" },
                  { "bvid": "BV0000000002", "title": "numeric play", "play": "42" }
                ]
              }
            }
            """;

        var results = BilibiliUtility.ParseResults(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("歌曲&测试", results[0].Title);
        Assert.Equal("UP", results[0].Author);
        Assert.Equal("https://i0.hdslb.com/cover.jpg", results[0].Pic);
        Assert.Equal(12345, results[0].Play);
        Assert.Equal(42, results[1].Play);
    }

    [Fact]
    public void ParseResults_ReturnsEmptyWhenDataShapeIsMissing()
    {
        Assert.Empty(BilibiliUtility.ParseResults("""{"code":0}"""));
        Assert.Empty(BilibiliUtility.ParseResults("""{"code":0,"data":{"result":{}}}"""));
    }

    [Fact]
    public void ParseResults_ThrowsForBilibiliError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BilibiliUtility.ParseResults("""{"code":-412,"message":"blocked"}"""));

        Assert.Contains("-412", exception.Message);
        Assert.Contains("blocked", exception.Message);
    }
}
