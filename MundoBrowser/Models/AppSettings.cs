namespace MundoBrowser.Models;

public sealed class AppSettings
{
    public string StartPage { get; set; } = "https://www.google.com";
    public string SearchEngine { get; set; } = "google";
    public string CustomSearchUrl { get; set; } = "https://www.google.com/search?q={query}";
    public bool UseSearchEngineAsStartPage { get; set; } = false;
    public bool EcoModeEnabled { get; set; } = true;
    public int EcoModeMinutes { get; set; } = 10;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool IsSidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 250;
    public bool IsAdBlockerEnabled { get; set; } = true;
    public bool IsCookieBlockerEnabled { get; set; } = true;
    public List<string> ProtectionDisabledSites { get; set; } = [];
    public List<string> AdBlockDisabledSites { get; set; } = [];
    public List<string> CookieBlockDisabledSites { get; set; } = [];
    public bool IsTrackingPreventionEnabled { get; set; } = true;
    public bool IsPasswordAutosaveEnabled { get; set; } = false;
    public bool IsGeneralAutofillEnabled { get; set; } = true;
    public bool IsBetaChannelEnabled { get; set; } = false;
}
