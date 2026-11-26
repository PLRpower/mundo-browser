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

    [Theory]
    [InlineData("chrome-extension://agcalopfgbmbfgciceeneagbcjbmhopo/popup/index.html?uilocation=popout&singleActionPopout=vault_Fido2Popout_abc#/fido2?sessionId=abc", true)]
    [InlineData("chrome-extension://agcalopfgbmbfgciceeneagbcjbmhopo/notification/bar.html?add=1", true)]
    [InlineData("chrome-extension://agcalopfgbmbfgciceeneagbcjbmhopo/popup/index.html#/tabs/vault", true)]
    [InlineData("chrome-extension://cjpalhdlnbpafiamejdnhcphjbkeiagm/dashboard.html", false)]
    [InlineData("https://clients.boursobank.com/connexion/", false)]
    [InlineData("about:blank", false)]
    public void ExtensionService_IsExtensionPopupUri_DetectsPopupsCorrectly(string uri, bool expected)
    {
        var extensions = new[]
        {
            new ExtensionInfo("agcalopfgbmbfgciceeneagbcjbmhopo", "Bitwarden", true)
            {
                PopupUrl = "chrome-extension://agcalopfgbmbfgciceeneagbcjbmhopo/popup/index.html"
            }
        };

        Assert.Equal(expected, ExtensionService.IsExtensionPopupUri(uri, extensions));
    }

    [Fact]
    public void ExtensionService_EnsureExtensionCompatibilityShim_PatchesBackgroundServiceWorkerIdempotently()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MundoBrowserExtTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string bgPath = Path.Combine(tempDir, "background.js");
            File.WriteAllText(bgPath, "console.log('worker');");

            const string manifestJson = """
            {
              "manifest_version": 3,
              "name": "Bitwarden Test",
              "version": "1.0.0",
              "background": { "service_worker": "background.js" }
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);

            bool firstPatch = ExtensionService.EnsureExtensionCompatibilityShim(tempDir, doc.RootElement);
            bool secondPatch = ExtensionService.EnsureExtensionCompatibilityShim(tempDir, doc.RootElement);

            Assert.True(firstPatch);
            Assert.False(secondPatch);
            string patchedContent = File.ReadAllText(bgPath);
            Assert.Contains(ExtensionService.WebView2ExtensionShimMarker, patchedContent);
            Assert.EndsWith("console.log('worker');", patchedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void EnsureExtensionCompatibilityShim_UpgradesLegacyV1ShimToCurrent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MundoBrowserTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string bgPath = Path.Combine(tempDir, "background.js");
            File.WriteAllText(bgPath, """
                /* MUNDO_WV2_EXTENSION_SHIM_V1 */
                (function() {
                  chrome.tabs.create = patchedTabCreate;
                })();
                console.log('original-worker');
                """);

            const string manifestJson = """
            {
              "manifest_version": 3,
              "name": "Bitwarden Test",
              "version": "1.0.0",
              "background": { "service_worker": "background.js" }
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);

            bool upgraded = ExtensionService.EnsureExtensionCompatibilityShim(tempDir, doc.RootElement);

            Assert.True(upgraded);
            string patchedContent = File.ReadAllText(bgPath);
            Assert.Contains(ExtensionService.WebView2ExtensionShimMarker, patchedContent);
            Assert.DoesNotContain("/* MUNDO_WV2_EXTENSION_SHIM_V1 */", patchedContent);
            Assert.EndsWith("console.log('original-worker');", patchedContent.Trim());

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ExtensionService.EnsureAllInstalledExtensionsPatched(Path.Combine(appData, "MundoBrowser", "Extensions"));
            ExtensionService.EnsureAllInstalledExtensionsPatched(Path.Combine(appData, "MundoBrowser.Development", "Extensions"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public void ShowInformation(string message, string title) { }
        public void ShowError(string message, string title) { }
    }

    [Fact]
    public async Task UpdateService_CheckForUpdatesInBackground_RunsStartupAndPeriodicChecksIdempotently()
    {
        var settings = new FakeAppSettingsService();
        var dialogs = new FakeDialogService();
        int checkCount = 0;
        bool sawManualCheck = false;
        var twoChecksObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var updateService = new UpdateService(
            settings,
            dialogs,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(35),
            (isManualCheck, _) =>
            {
                if (isManualCheck)
                    sawManualCheck = true;
                if (Interlocked.Increment(ref checkCount) >= 2)
                    twoChecksObserved.TrySetResult();
                return Task.CompletedTask;
            });

        updateService.CheckForUpdatesInBackground(["--arg"]);
        updateService.CheckForUpdatesInBackground(["--arg"]);

        await twoChecksObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        updateService.Dispose();
        int countAtDispose = Volatile.Read(ref checkCount);
        await Task.Delay(80);

        Assert.False(sawManualCheck);
        Assert.True(countAtDispose >= 2);
        Assert.Equal(countAtDispose, Volatile.Read(ref checkCount));
    }

    [Fact]
    public void UpdateIconGeometry_ParsesCleanSymmetricBounds()
    {
        var geometry = System.Windows.Media.Geometry.Parse(
            "M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 Z M9,12.75 L12,15.75 L15,12.75 M12,8.25 L12,15.75");

        Assert.False(geometry.IsEmpty());
        Assert.Equal(3, geometry.Bounds.Left, 3);
        Assert.Equal(3, geometry.Bounds.Top, 3);
        Assert.Equal(18, geometry.Bounds.Width, 3);
        Assert.Equal(18, geometry.Bounds.Height, 3);
    }

    [Fact]
    public void ExtensionService_MatchesManifest_MatchesLocalizedAndCaseInsensitiveMessages()
    {
        string rootTempDir = Path.Combine(Path.GetTempPath(), "MundoBrowserExtMatchTest_" + Guid.NewGuid().ToString("N"));
        string tempDir = Path.Combine(rootTempDir, "nngceckbapebfimnlniiiahkandclblb");
        Directory.CreateDirectory(Path.Combine(tempDir, "_locales", "en"));
        Directory.CreateDirectory(Path.Combine(tempDir, "_locales", "fr"));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "_locales", "en", "messages.json"),
                """{ "extname": { "message": "Password Vault" } }""");
            File.WriteAllText(
                Path.Combine(tempDir, "_locales", "fr", "messages.json"),
                """{ "extname": { "message": "Coffre de mots de passe" } }""");

            const string manifestJson = """
            {
              "manifest_version": 3,
              "name": "__MSG_extName__",
              "default_locale": "en",
              "version": "1.0.0"
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);

            Assert.True(ExtensionService.MatchesManifest(tempDir, doc.RootElement, "agcalopfgbmbfgciceeneagbcjbmhopo", "Coffre de mots de passe"));
            Assert.True(ExtensionService.MatchesManifest(tempDir, doc.RootElement, "agcalopfgbmbfgciceeneagbcjbmhopo", "Password Vault"));
            Assert.True(ExtensionService.MatchesManifest(tempDir, doc.RootElement, "nngceckbapebfimnlniiiahkandclblb", "Unrelated Name"));
            Assert.False(ExtensionService.MatchesManifest(tempDir, doc.RootElement, "agcalopfgbmbfgciceeneagbcjbmhopo", "Other Extension"));
        }
        finally
        {
            if (Directory.Exists(rootTempDir))
                Directory.Delete(rootTempDir, recursive: true);
        }
    }
}




