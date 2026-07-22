using Jellyfin.Plugin.QualityOverlay.Configuration;
using Jellyfin.Plugin.QualityOverlay.Detection;
using SkiaSharp;

namespace Jellyfin.Plugin.QualityOverlay.Drawing;

public class BadgeRenderer
{
    private const byte ResFillAlpha = 235; // 0.92
    private const byte BorderAlpha = 89; // 0.35
    private const byte RatingBorderAlpha = 76; // 0.30

    public byte[]? Render(byte[] source, string contentType, IReadOnlyList<BadgeLabel> labels, BadgePosition position, PluginConfiguration config)
    {
        if (labels.Count == 0)
        {
            return null;
        }

        using var bitmap = SKBitmap.Decode(source);
        if (bitmap is null)
        {
            return null;
        }

        using var canvas = new SKCanvas(bitmap);

        var shortEdge = Math.Min(bitmap.Width, bitmap.Height);
        var fontSize = Clamp((float)(shortEdge * 0.052 * config.BadgeScale), 13f, 64f);
        var paddingX = fontSize * 0.62f;
        var paddingY = fontSize * 0.34f;
        var gap = fontSize * 0.42f;
        var margin = (float)config.Margin;
        var borderWidth = Math.Max(1.5f, fontSize * 0.09f);

        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold)
            ?? SKTypeface.Default;
        using var font = new SKFont(typeface, fontSize) { Edging = SKFontEdging.SubpixelAntialias };

        // The default typeface may lack the ★ glyph; fall back to any system
        // font that has it for rating badges.
        using SKFont? starFont = !HasGlyph(typeface, '★') && SKFontManager.Default.MatchCharacter('★') is { } starTypeface
            ? new SKFont(starTypeface, fontSize) { Edging = SKFontEdging.SubpixelAntialias }
            : null;

        var accent = ParseColor(config.AccentColor, new SKColor(0x00, 0xD4, 0xAA));
        var resTextColor = ParseColor(config.ResolutionTextColor, new SKColor(0x05, 0x25, 0x20));
        var textColor = ParseColor(config.TextColor, new SKColor(0xDF, 0xF3, 0xEE));
        var ratingColor = ParseColor(config.RatingColor, new SKColor(0xFF, 0xD4, 0x79));
        var background = ParseColor(config.BackgroundColor, new SKColor(0x07, 0x0C, 0x0E))
            .WithAlpha((byte)(Clamp((float)config.BackgroundOpacity, 0f, 1f) * 255));

        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { IsAntialias = true };
        using var borderPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = borderWidth };

        var badges = new List<RenderedBadge>(labels.Count);
        foreach (var label in labels)
        {
            var badgeFont = font;
            var text = label.Text;
            if (label.Kind == BadgeKind.Rating && !HasGlyph(font.Typeface ?? typeface, '★'))
            {
                if (starFont is not null)
                {
                    badgeFont = starFont;
                }
                else if (text.StartsWith("★ ", StringComparison.Ordinal))
                {
                    text = text[2..];
                }
            }

            var textWidth = badgeFont.MeasureText(text);
            badges.Add(new RenderedBadge(text, textWidth + (paddingX * 2), label.Kind, badgeFont));
        }

        var fontMetrics = font.Metrics;
        var textHeight = fontMetrics.Descent - fontMetrics.Ascent;
        var badgeHeight = textHeight + (paddingY * 2);
        var totalHeight = (badgeHeight * badges.Count) + (gap * (badges.Count - 1));
        var isBottom = position is BadgePosition.BottomLeft or BadgePosition.BottomRight;
        var isRight = position is BadgePosition.TopRight or BadgePosition.BottomRight;

        var currentY = isBottom
            ? bitmap.Height - margin - totalHeight
            : margin;

        var radius = badgeHeight / 2f;

        foreach (var badge in badges)
        {
            var x = isRight ? bitmap.Width - margin - badge.Width : margin;
            var rect = new SKRect(x, currentY, x + badge.Width, currentY + badgeHeight);

            fillPaint.Color = badge.Kind == BadgeKind.Video
                ? accent.WithAlpha(ResFillAlpha)
                : background;
            canvas.DrawRoundRect(rect, radius, radius, fillPaint);

            if (badge.Kind != BadgeKind.Video)
            {
                borderPaint.Color = badge.Kind == BadgeKind.Rating
                    ? ratingColor.WithAlpha(RatingBorderAlpha)
                    : accent.WithAlpha(BorderAlpha);
                var inset = borderWidth / 2f;
                var borderRect = new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
                canvas.DrawRoundRect(borderRect, radius, radius, borderPaint);
            }

            textPaint.Color = badge.Kind switch
            {
                BadgeKind.Video => resTextColor,
                BadgeKind.Rating => ratingColor,
                _ => textColor
            };

            var metrics = badge.Font.Metrics;
            var baseline = rect.MidY - ((metrics.Ascent + metrics.Descent) / 2f);
            canvas.DrawText(badge.Text, rect.Left + paddingX, baseline, SKTextAlign.Left, badge.Font, textPaint);

            currentY += badgeHeight + gap;
        }

        canvas.Flush();

        var format = contentType.Contains("png", StringComparison.OrdinalIgnoreCase)
            ? SKEncodedImageFormat.Png
            : SKEncodedImageFormat.Jpeg;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 92);
        return data?.ToArray();
    }

    private static bool HasGlyph(SKTypeface typeface, char character)
    {
        var glyphs = typeface.GetGlyphs(character.ToString());
        return glyphs.Length > 0 && glyphs[0] != 0;
    }

    private static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);

    private static SKColor ParseColor(string value, SKColor fallback)
    {
        return SKColor.TryParse(value, out var color) ? color : fallback;
    }

    private readonly record struct RenderedBadge(string Text, float Width, BadgeKind Kind, SKFont Font);
}
