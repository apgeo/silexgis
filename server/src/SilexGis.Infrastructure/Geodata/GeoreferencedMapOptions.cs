// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// How map sheets in portable documents are drawn, placed and published. Every number has a
/// default chosen for cave surveys on an ordinary server; an installation changes them through
/// <c>SILEXGIS__GeoreferencedMaps__*</c>.
/// </summary>
public sealed class GeoreferencedMapOptions
{
    public const string SectionName = "GeoreferencedMaps";

    public const string Webp = "webp";
    public const string Deflate = "deflate";

    /// <summary>
    /// How the published GeoTIFF's tiles are compressed: <c>webp</c> (small; lossy at any quality
    /// below 100) or <c>deflate</c> (lossless, several times larger).
    /// </summary>
    public string CogCompression { get; set; } = Webp;

    /// <summary>WebP quality, 1–100. 85 keeps thin survey lines crisp at a fraction of the size.</summary>
    public int CogWebpQuality { get; set; } = 85;

    /// <summary>Deflate level, 1–9.</summary>
    public int CogDeflateLevel { get; set; } = 6;

    /// <summary>The resolution a sheet with no georeferencing is drawn at when the upload names none.</summary>
    public double DefaultDpiUnplaced { get; set; } = 300;

    /// <summary>
    /// The ground size of one pixel a georeferenced sheet is drawn for, in metres; the resolution
    /// is worked out from the sheet's own scale.
    /// </summary>
    public double TargetGroundResolutionMetres { get; set; } = 0.05;

    public double MinDpi { get; set; } = 72;

    public double MaxDpi { get; set; } = 600;

    /// <summary>The largest drawing made of one page, in millions of pixels; beyond it the resolution is lowered.</summary>
    public int PixelBudgetMegapixels { get; set; } = 600;

    /// <summary>
    /// The most memory one read of a page may take, in megabytes. A larger drawing is made in
    /// horizontal strips, each of which reads the page again.
    /// </summary>
    public int RenderBudgetMegabytes { get; set; } = 1024;

    /// <summary>How long one drawing may take before the job gives up.</summary>
    public int RenderTimeLimitMinutes { get; set; } = 30;

    /// <summary>The longer side of the picture the placement editor is shown, in pixels.</summary>
    public int PreviewMaxPixels { get; set; } = 1024;

    /// <summary>How many placement previews may be worked out at once.</summary>
    public int PreviewConcurrency { get; set; } = 4;

    /// <summary>The most control points a placement may hold.</summary>
    public int MaxControlPoints { get; set; } = 200;

    /// <summary>
    /// Whether drawing and placing sheets runs on the heavy job lane (beside terrain builds) instead
    /// of the general one. Off by default: a sheet takes seconds to a minute, a terrain build hours.
    /// Read when the workers start.
    /// </summary>
    public bool SheetJobsOnHeavyLane { get; set; }

    /// <summary>
    /// The highest resolution the server chooses by itself for a georeferenced document, in dots per
    /// inch. The ground-resolution target asks for far more on a small-scale map (a 1:24 000 sheet
    /// would want 12 000 dpi); this keeps such a sheet at a resolution that reads well instead of at
    /// the absolute maximum. A resolution somebody asks for is still allowed up to <see cref="MaxDpi"/>.
    /// </summary>
    public double MaxAutoDpi { get; set; } = 300;

    /// <summary>
    /// Coordinate systems, besides longitude and latitude and the installation's working system,
    /// in which placement coordinates may be typed — typically the national grid printed on the
    /// sheets. EPSG codes.
    /// </summary>
    public IList<int> PlacementCrsCodes { get; set; } = [3844];

    /// <summary>The long side of the stored picture of a drawn page, in pixels.</summary>
    public const int StoredPreviewPixels = 2400;

    public bool UsesWebp => string.Equals(CogCompression, Webp, StringComparison.OrdinalIgnoreCase);

    /// <summary>The COG writer's creation options for the chosen compression.</summary>
    public IReadOnlyList<string> CogCreationOptions() => UsesWebp
        ? ["COMPRESS=WEBP", $"QUALITY={CogWebpQuality}"]
        : ["COMPRESS=DEFLATE", $"LEVEL={CogDeflateLevel}", "PREDICTOR=YES"];
}

/// <summary>Refuses start-up on a value no drawing could be made with, naming the setting.</summary>
public sealed class GeoreferencedMapOptionsValidator : IValidateOptions<GeoreferencedMapOptions>
{
    public ValidateOptionsResult Validate(string? name, GeoreferencedMapOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var failures = new List<string>();
        const string P = "GeoreferencedMaps:";

        if (!string.Equals(o.CogCompression, GeoreferencedMapOptions.Webp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(o.CogCompression, GeoreferencedMapOptions.Deflate, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{P}CogCompression must be 'webp' or 'deflate'.");
        }

        Range(o.CogWebpQuality, 1, 100, "CogWebpQuality");
        Range(o.CogDeflateLevel, 1, 9, "CogDeflateLevel");
        Range(o.MinDpi, 36, 2400, "MinDpi");
        Range(o.MaxDpi, 36, 2400, "MaxDpi");
        if (o.MinDpi > o.MaxDpi)
        {
            failures.Add($"{P}MinDpi must not be above MaxDpi.");
        }

        Range(o.DefaultDpiUnplaced, 72, 1200, "DefaultDpiUnplaced");
        if (!(o.TargetGroundResolutionMetres > 0 && o.TargetGroundResolutionMetres <= 100))
        {
            failures.Add($"{P}TargetGroundResolutionMetres must be above 0 and at most 100.");
        }

        Range(o.PixelBudgetMegapixels, 10, 4000, "PixelBudgetMegapixels");
        Range(o.RenderBudgetMegabytes, 128, 16384, "RenderBudgetMegabytes");
        Range(o.RenderTimeLimitMinutes, 1, 720, "RenderTimeLimitMinutes");
        Range(o.PreviewMaxPixels, 256, 4096, "PreviewMaxPixels");
        Range(o.PreviewConcurrency, 1, 32, "PreviewConcurrency");
        Range(o.MaxControlPoints, 4, 2000, "MaxControlPoints");
        Range(o.MaxAutoDpi, 36, 2400, "MaxAutoDpi");
        if (o.PlacementCrsCodes.Any(c => c <= 0))
        {
            failures.Add($"{P}PlacementCrsCodes must hold positive EPSG codes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Range(double value, double min, double max, string setting)
        {
            if (!(value >= min && value <= max))
            {
                failures.Add($"{P}{setting} must be from {min} to {max}.");
            }
        }
    }
}
