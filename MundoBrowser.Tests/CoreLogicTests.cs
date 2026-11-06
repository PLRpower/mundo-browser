using System.IO;
using MundoBrowser.Helpers;
using MundoBrowser.Interfaces;
using MundoBrowser.Models;
using MundoBrowser.Services;
using Xunit;

namespace MundoBrowser.Tests;

public sealed class CoreLogicTests
{
    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public event Action<AppSettings>? SettingsChanged;

        public void Update(Action<AppSettings> update)
        {
            update(Current);
            SettingsChanged?.Invoke(Current);
        }
    }

    [Theory]
    [InlineData("https://doubleclick.net/ad.js", true, true)]
    [InlineData("https://sub.googlesyndication.com/pagead/js/adsbygoogle.js", true, true)]
    [InlineData("http://www.google-analytics.com/collect?v=1", true, true)]
    [InlineData("https://www.youtube.com/pagead/lvz", true, true)]
    [InlineData("https://www.youtube.com/api/stats/ads?ver=2", true, true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true, false)]
    [InlineData("https://cdn.cookielaw.org/scripttemplates/otSDKStub.js", true, true)]
    [InlineData("https://cdn.cookielaw.org/scripttemplates/otSDKStub.js", false, false)]
    [InlineData("https://github.com/user/repo", true, false)]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", true, false)]
    [InlineData("about:blank", true, false)]
    public void AdBlocker_ShouldBlockUrl_EvaluatesExpectedResult(string url, bool cookieBlockerActive, bool expectedBlocked)
    {
        var settings = new FakeAppSettingsService();
        var adBlocker = new AdBlockerService(settings);

        bool actual = adBlocker.ShouldBlockUrl(url, cookieBlockerActive);

        Assert.Equal(expectedBlocked, actual);
    }

    [Fact]
    public void AdBlocker_SiteWhitelist_TogglesProtectionCorrectly()
    {
        var settings = new FakeAppSettingsService();
        var adBlocker = new AdBlockerService(settings);
        const string siteUrl = "https://www.example.com/articles/123";

        Assert.Equal("example.com", adBlocker.GetSiteHost(siteUrl));
        Assert.True(adBlocker.IsAdBlockerEnabledForSite(siteUrl));
        Assert.False(adBlocker.IsProtectionDisabledForSite(siteUrl));

        adBlocker.SetProtectionDisabledForSite(siteUrl, disabled: true);

        Assert.False(adBlocker.IsAdBlockerEnabledForSite("https://sub.example.com/page"));
        Assert.False(adBlocker.IsCookieBlockerEnabledForSite("https://example.com/"));
        Assert.True(adBlocker.IsProtectionDisabledForSite(siteUrl));

        adBlocker.SetProtectionDisabledForSite(siteUrl, disabled: false);

        Assert.True(adBlocker.IsAdBlockerEnabledForSite(siteUrl));
        Assert.False(adBlocker.IsProtectionDisabledForSite(siteUrl));
    }

    [Theory]
    [InlineData("mundo browser", "google", null, "https://www.google.com/search?q=mundo%20browser")]
    [InlineData("c# 13", "duckduckgo", null, "https://duckduckgo.com/?q=c%23%2013")]
    [InlineData("test", "custom", "https://search.example.org/q?term={query}", "https://search.example.org/q?term=test")]
    [InlineData("test", "custom", "https://search.example.org/q?term={0}", "https://search.example.org/q?term=test")]
    [InlineData("test", "custom", "https://search.example.org/find", "https://search.example.org/find?q=test")]
    public void SearchEngineHelper_BuildSearchUrl_FormatsQueryCorrectly(
        string query,
        string engine,
        string? customUrl,
        string expectedUrl)
    {
        string actual = SearchEngineHelper.BuildSearchUrl(query, engine, customUrl);

        Assert.Equal(expectedUrl, actual);
    }

    [Theory]
    [InlineData("BRAVE", "brave")]
    [InlineData("  DuckDuckGo ", "duckduckgo")]
    [InlineData("unknown_engine", "google")]
    [InlineData(null, "google")]
    public void SearchEngineHelper_NormalizeSearchEngine_ReturnsCanonicalKey(string? input, string expected)
    {
        Assert.Equal(expected, SearchEngineHelper.NormalizeSearchEngine(input));
    }

    [Theory]
    [InlineData("https://chromewebstore.google.com/detail/ublock-origin/cjpalhdlnbpafiamejdnhcphjbkeiagm", "cjpalhdlnbpafiamejdnhcphjbkeiagm")]
    [InlineData("https://chrome.google.com/webstore/detail/extension-name/aaaaabbbbbcccccdddddeeeeefffffgg?hl=fr", "aaaaabbbbbcccccdddddeeeeefffffgg")]
    [InlineData("https://chromewebstore.google.com/detail/invalid-id/not32chars", null)]
    [InlineData("https://example.com/detail/cjpalhdlnbpafiamejdnhcphjbkeiagm", null)]
    public void ChromeWebStoreHelper_ExtractExtensionId_ParsesValidStoreUrls(string url, string? expectedId)
    {
        Assert.Equal(expectedId, ChromeWebStoreHelper.ExtractExtensionId(url));
    }

    [Fact]
    public async Task AtomicFileHelper_WriteAndDeserializeJson_RoundTripsCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MundoBrowserTests_" + Guid.NewGuid().ToString("N"));
        string filePath = Path.Combine(tempDir, "session.json");

        try
        {
            var session = new SessionData
            {
                SelectedTabIndex = 1,
                IsSplitViewActive = true,
                Tabs =
                [
                    new TabSessionData { Title = "GitHub", Url = "https://github.com", ZoomFactor = 1.25 }
                ]
            };

            await AtomicFileHelper.WriteJsonAtomicAsync(filePath, session, AppJsonContext.Indented.SessionData);

            Assert.True(File.Exists(filePath));
            await using var stream = File.OpenRead(filePath);
            var loaded = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.SessionData);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.SelectedTabIndex);
            Assert.True(loaded.IsSplitViewActive);
            Assert.Single(loaded.Tabs);
            Assert.Equal("https://github.com", loaded.Tabs[0].Url);
            Assert.Equal(1.25, loaded.Tabs[0].ZoomFactor);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
