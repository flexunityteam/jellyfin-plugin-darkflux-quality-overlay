# DarkFlux Quality Overlay

Server-side poster badge overlays for **Jellyfin 10.11+**. Badges are drawn in
memory while images are served — original image files are never modified, and
**every client** (Android TV, mobile, web, Infuse, ...) sees them.

Fork of [Quality Overlay](https://github.com/obxidion/Jellyfin-Quality-Overlay)
by **obxidion** (GPL-3.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE) for
attribution and the list of changes).

## Badges (top-right pill stack)

| Badge | Values | Source |
|---|---|---|
| Resolution | `4K` / `1080p` / `720p` / `SD` | width of widest video stream (≥3800 / ≥1900 / ≥1200 / >0) |
| HDR | `DV` / `HDR10+` / `HDR10` / `HLG` / `HDR` | best `VideoRangeType`/`VideoRange` across video streams |
| Audio | `Atmos` / `DTS:X` / `TrueHD` / `DTS-HD` | best-ranked audio stream (profile/codec) |
| Rating | `★ x.x` | `CommunityRating` (1 decimal) |

Defaults: resolution pill = solid teal `#00D4AA` with dark text; HDR/audio
pills = dark translucent with teal border; rating = amber. Position, scale,
margin, opacity, colors, per-badge toggles and processed image types
(Primary/Thumb/Backdrop) are all configurable in
**Dashboard → Plugins → DarkFlux Quality Overlay**.

Movies, episodes and series are badged. A series has no streams of its own, so
episode streams are aggregated (capped at 100 episodes) and the series poster
shows the best available quality.

## Install via plugin repository (recommended)

1. Jellyfin Dashboard → **Plugins → Repositories → Add**:
   ```
   https://raw.githubusercontent.com/flexunityteam/jellyfin-plugin-darkflux-quality-overlay/master/manifest.json
   ```
2. Dashboard → **Plugins → Catalog**, install **DarkFlux Quality Overlay**.
3. Restart Jellyfin when prompted. Future updates appear in the catalog
   automatically.

## Install manually

1. Build (below) or download `darkflux-quality-overlay_<version>.zip` from
   [Releases](../../releases) and unzip it.
2. Copy `Jellyfin.Plugin.DarkFluxQualityOverlay.dll` into
   `<jellyfin-config>/plugins/DarkFlux Quality Overlay_<version>/`.
3. Restart Jellyfin.

## Build

Requires the .NET 9.0 SDK (or Docker):

```bash
dotnet publish -c Release -o ./publish
# or
docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp \
  -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet publish -c Release -o ./publish
```

Output: `publish/Jellyfin.Plugin.DarkFluxQualityOverlay.dll`.
`Jellyfin.Controller`, `Jellyfin.Model` (10.11.0) and `SkiaSharp` (3.116.1)
are referenced with `ExcludeAssets=runtime` — all provided by the server at
runtime.

Optionally package with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager):
`jprm plugin build .` (see `build.yaml`).

## How it works

- ASP.NET Core middleware intercepts `GET /Items/{id}/Images/{type}`, buffers
  Jellyfin's own image-pipeline response (badges are drawn on the exact
  resized image each client requested), draws pills with SkiaSharp and serves
  the result with its own ETag (`Cache-Control: no-cache`, cheap 304
  revalidation).
- Processed images are cached on disk under a SHA-256 key of
  `item + image type + query string + item DateModified + resolved badge
  labels + visual config`. Media, metadata or config changes alter the key and
  force a re-render — Jellyfin's own image cache is never touched or wiped.
- Media stream data comes from `IMediaSourceManager.GetMediaStreams` with a
  6 h in-memory per-item cache (30 min negative); badge toggles are applied
  after the cache read so configuration changes take effect immediately.
- Any failure falls back to serving the original image.

## Limitations

- Badges need probed media info: items whose streams were never scanned show
  no badges (they appear after a library scan / probe).
- Series badges aggregate up to 100 episodes and refresh on the 6 h label
  cache (item edits changing `DateModified` re-render sooner).
- HDR/audio badges describe the *best available* stream (e.g. a series with
  any DV episode shows `DV`), matching the resolution badge semantics.
- Clients that cached un-badged posters pick up badged versions on their next
  ETag revalidation.

## Verified on

Jellyfin 10.11.10 (Docker): 4K Dolby Vision Atmos series poster renders
`4K / DV / Atmos / ★ 9.4`; 1080p SDR Atmos movie renders
`1080p / Atmos / ★ 6.7` (no HDR badge); backdrops untouched.

## License

GNU General Public License v3.0. Copyright of the original "Quality Overlay"
project: obxidion. Fork changes: flexunityteam. See LICENSE and NOTICE.
