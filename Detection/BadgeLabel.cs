namespace Jellyfin.Plugin.QualityOverlay.Detection;

public enum BadgeKind
{
    Video,
    Hdr,
    Audio,
    Rating
}

public readonly record struct BadgeLabel(string Text, BadgeKind Kind);
