using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.QualityOverlay.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.QualityOverlay.Detection;

public class MediaInfoResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(30);
    private const int MaxSeriesEpisodes = 100;

    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();

    public MediaInfoResolver(IMediaSourceManager mediaSourceManager, ILibraryManager libraryManager)
    {
        _mediaSourceManager = mediaSourceManager;
        _libraryManager = libraryManager;
    }

    public IReadOnlyList<BadgeLabel> GetLabels(BaseItem item, PluginConfiguration config)
    {
        var all = GetAllLabels(item);
        if (all.Count == 0)
        {
            return all;
        }

        // Badge toggles are applied after the in-memory cache read so that
        // configuration changes take effect immediately.
        var filtered = new List<BadgeLabel>(all.Count);
        foreach (var label in all)
        {
            var enabled = label.Kind switch
            {
                BadgeKind.Video => config.EnableVideoQualityBadge,
                BadgeKind.Hdr => config.EnableHdrBadge,
                BadgeKind.Audio => config.EnableAudioCodecBadge,
                BadgeKind.Rating => config.EnableRatingBadge,
                _ => false
            };

            if (enabled)
            {
                filtered.Add(label);
            }
        }

        return filtered;
    }

    private IReadOnlyList<BadgeLabel> GetAllLabels(BaseItem item)
    {
        if (item is not (Movie or Episode or Series))
        {
            return Array.Empty<BadgeLabel>();
        }

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(item.Id, out var entry) && entry.ExpiresUtc > now)
        {
            return entry.Labels;
        }

        var streams = CollectStreams(item);
        var labels = ComputeLabels(item, streams);
        var ttl = labels.Count == 0 ? NegativeCacheTtl : CacheTtl;
        _cache[item.Id] = new CacheEntry(labels, now.Add(ttl));
        return labels;
    }

    private IReadOnlyList<MediaStream> CollectStreams(BaseItem item)
    {
        if (item is Series)
        {
            // A series has no media streams of its own; aggregate the streams
            // of its episodes so series posters show the best available quality.
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = item.Id,
                Recursive = true,
                IncludeItemTypes = new[] { BaseItemKind.Episode }
            });

            var merged = new List<MediaStream>();
            foreach (var episode in episodes.Take(MaxSeriesEpisodes))
            {
                var streams = _mediaSourceManager.GetMediaStreams(episode.Id);
                if (streams is not null)
                {
                    merged.AddRange(streams);
                }
            }

            return merged;
        }

        return _mediaSourceManager.GetMediaStreams(item.Id)
            ?? (IReadOnlyList<MediaStream>)Array.Empty<MediaStream>();
    }

    private static IReadOnlyList<BadgeLabel> ComputeLabels(BaseItem item, IReadOnlyList<MediaStream> streams)
    {
        var labels = new List<BadgeLabel>(4);

        var video = streams
            .Where(s => s.Type == MediaStreamType.Video && !s.IsExternal)
            .OrderByDescending(s => s.Width ?? 0)
            .FirstOrDefault();

        if (video is not null)
        {
            var resolution = ResolutionLabel(video.Width ?? 0);
            if (resolution is not null)
            {
                labels.Add(new BadgeLabel(resolution, BadgeKind.Video));
            }

            // HDR reflects the best available video stream (like resolution and
            // audio), so mixed libraries (e.g. some SDR and some DV episodes of
            // the same series) still show the HDR badge.
            var hdr = HdrLabel(streams);
            if (hdr is not null)
            {
                labels.Add(new BadgeLabel(hdr, BadgeKind.Hdr));
            }

            var audio = AudioLabel(streams);
            if (audio is not null)
            {
                labels.Add(new BadgeLabel(audio, BadgeKind.Audio));
            }
        }

        if (item.CommunityRating is > 0)
        {
            labels.Add(new BadgeLabel(
                "★ " + item.CommunityRating.Value.ToString("0.0", CultureInfo.InvariantCulture),
                BadgeKind.Rating));
        }

        return labels;
    }

    private static string? ResolutionLabel(int width)
    {
        if (width >= 3800)
        {
            return "4K";
        }

        if (width >= 1900)
        {
            return "1080p";
        }

        if (width >= 1200)
        {
            return "720p";
        }

        return width > 0 ? "SD" : null;
    }

    private static string? HdrLabel(IReadOnlyList<MediaStream> streams)
    {
        string? best = null;
        var bestRank = 0;

        foreach (var video in streams)
        {
            if (video.Type != MediaStreamType.Video || video.IsExternal)
            {
                continue;
            }

            var (label, rank) = HdrRank(video);
            if (rank > bestRank)
            {
                best = label;
                bestRank = rank;
            }
        }

        return best;
    }

    private static (string? Label, int Rank) HdrRank(MediaStream video)
    {
        var type = video.VideoRangeType.ToString();
        var range = video.VideoRange.ToString();

        if (type.StartsWith("DOVI", StringComparison.OrdinalIgnoreCase))
        {
            return ("DV", 5);
        }

        if (type.StartsWith("HDR10Plus", StringComparison.OrdinalIgnoreCase))
        {
            return ("HDR10+", 4);
        }

        if (type.StartsWith("HDR10", StringComparison.OrdinalIgnoreCase))
        {
            return ("HDR10", 3);
        }

        if (type.StartsWith("HLG", StringComparison.OrdinalIgnoreCase))
        {
            return ("HLG", 2);
        }

        if (range.StartsWith("HDR", StringComparison.OrdinalIgnoreCase))
        {
            return ("HDR", 1);
        }

        return (null, 0);
    }

    private static string? AudioLabel(IReadOnlyList<MediaStream> streams)
    {
        string? best = null;
        var bestRank = 0;

        foreach (var stream in streams)
        {
            if (stream.Type != MediaStreamType.Audio)
            {
                continue;
            }

            var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
            var profile = (stream.Profile ?? string.Empty).ToLowerInvariant();

            string? label = null;
            var rank = 0;
            if (profile.Contains("atmos", StringComparison.Ordinal))
            {
                label = "Atmos";
                rank = 4;
            }
            else if (profile.Contains("dts:x", StringComparison.Ordinal) || profile.Contains("dts-x", StringComparison.Ordinal))
            {
                label = "DTS:X";
                rank = 3;
            }
            else if (codec == "truehd")
            {
                label = "TrueHD";
                rank = 2;
            }
            else if (profile.Contains("dts-hd", StringComparison.Ordinal) || codec is "dts-hd" or "dtshd")
            {
                label = "DTS-HD";
                rank = 1;
            }

            if (rank > bestRank)
            {
                best = label;
                bestRank = rank;
            }
        }

        return best;
    }

    private readonly record struct CacheEntry(IReadOnlyList<BadgeLabel> Labels, DateTime ExpiresUtc);
}
