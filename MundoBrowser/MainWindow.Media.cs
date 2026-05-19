using System.Text.Json;
using MundoBrowser.ViewModels;

namespace MundoBrowser;

public partial class MainWindow
{
    private async void UpdateActiveMediaInfo(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.ActiveMediaTab == null) return;

        var webView = _webViewService.GetWebViewForTab(vm.ActiveMediaTab);
        if (webView?.CoreWebView2 == null) return;

        try
        {
            const string script = @"
                (function() {
                    const media = document.querySelector('video, audio');
                    if (!media) return { hasMedia: false };
                    
                    let title = document.title;
                    let artist = '';
                    
                    if (navigator.mediaSession && navigator.mediaSession.metadata) {
                        title = navigator.mediaSession.metadata.title || title;
                        artist = navigator.mediaSession.metadata.artist || '';
                    }

                    return {
                        hasMedia: true,
                        title: title,
                        artist: artist,
                        position: media.currentTime,
                        duration: media.duration,
                        paused: media.paused,
                        muted: media.muted
                    };
                })()";

            string json = await webView.CoreWebView2.ExecuteScriptAsync(script);
            if (string.IsNullOrEmpty(json) || json == "null") return;

            var data = JsonSerializer.Deserialize<MediaData>(json);
            if (data != null && data.hasMedia)
            {
                var tab = vm.ActiveMediaTab;
                if (!tab.IsSeeking) tab.MediaPosition = data.position;
                tab.MediaDuration = data.duration;
                tab.MediaTitle = data.title;
                tab.MediaArtist = data.artist;
                tab.IsMediaPaused = data.paused;
                tab.IsMediaMuted = data.muted;
            }
        }
        catch { }
    }

    private async void OnMediaActionRequested(object? sender, string action)
    {
        if (DataContext is not MainViewModel vm || vm.ActiveMediaTab == null) return;
        
        var webView = _webViewService.GetWebViewForTab(vm.ActiveMediaTab);
        if (webView?.CoreWebView2 == null) return;

        string script = "";
        if (action.StartsWith("seek:"))
        {
            var parts = action.Split(':');
            if (parts.Length == 2 && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double percent))
            {
                script = $@"
                    (function() {{
                        const media = document.querySelector('video, audio');
                        if (media && media.duration > 0 && media.duration !== Infinity) {{
                            media.currentTime = media.duration * ({percent.ToString(System.Globalization.CultureInfo.InvariantCulture)} / 100);
                        }}
                    }})()";
            }
        }
        else
        {
            script = action switch
            {
                "playPause" => @"
                    (function() {
                        const video = document.querySelector('video, audio');
                        if (video) {
                            if (video.paused) video.play();
                            else video.pause();
                        } else {
                            const btn = document.querySelector('#play-pause-button') || document.querySelector('.play-pause-button') || document.querySelector('.ytp-play-button');
                            if (btn) btn.click();
                        }
                    })()",
                "next" => @"
                    (function() {
                        const btn = document.querySelector('.next-button') || document.querySelector('[aria-label=""Suivant""]') || document.querySelector('[aria-label=""Next""]') || document.querySelector('.ytp-next-button');
                        if (btn) btn.click();
                    })()",
                "previous" => @"
                    (function() {
                        const btn = document.querySelector('.previous-button') || document.querySelector('[aria-label=""Précédent""]') || document.querySelector('[aria-label=""Previous""]') || document.querySelector('.ytp-prev-button');
                        if (btn) btn.click();
                    })()",
                "volume" => @"
                    (function() {
                        const video = document.querySelector('video, audio');
                        if (video) video.muted = !video.muted;
                        else {
                            const btn = document.querySelector('.volume-button') || document.querySelector('.ytp-mute-button') || document.querySelector('[aria-label=""Mute""]') || document.querySelector('[aria-label=""Désactiver le son""]');
                            if (btn) btn.click();
                        }
                    })()",
                _ => ""
            };
        }

        if (!string.IsNullOrEmpty(script))
        {
            try { await webView.CoreWebView2.ExecuteScriptAsync(script); } catch { }
        }
    }

    public class MediaData
    {
        public string title { get; set; } = "";
        public string artist { get; set; } = "";
        public double position { get; set; }
        public double duration { get; set; }
        public bool muted { get; set; }
        public bool paused { get; set; }
        public bool hasMedia { get; set; }
    }
}