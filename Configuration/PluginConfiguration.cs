using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.QualityOverlay.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableVideoQualityBadge { get; set; } = true;

    public bool EnableHdrBadge { get; set; } = true;

    public bool EnableAudioCodecBadge { get; set; } = true;

    public bool EnableRatingBadge { get; set; } = true;

    public BadgePosition Position { get; set; } = BadgePosition.TopRight;

    public double BadgeScale { get; set; } = 1.0;

    public int Margin { get; set; } = 18;

    public double BackgroundOpacity { get; set; } = 0.78;

    public string BackgroundColor { get; set; } = "#070C0E";

    public string TextColor { get; set; } = "#DFF3EE";

    public string AccentColor { get; set; } = "#00D4AA";

    public string ResolutionTextColor { get; set; } = "#052520";

    public string RatingColor { get; set; } = "#FFD479";

    public bool ProcessPrimary { get; set; } = true;

    public bool ProcessThumb { get; set; }

    public bool ProcessBackdrop { get; set; }

    public int CacheExpirationHours { get; set; } = 168;
}
