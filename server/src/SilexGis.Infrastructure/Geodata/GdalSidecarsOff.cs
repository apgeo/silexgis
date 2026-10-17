// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Stops the raster library writing anything beside the files it opens, on this thread, for as
/// long as this is held.
/// </summary>
/// <remarks>
/// <para>
/// The library keeps what it learns about a raster that the format cannot hold — statistics,
/// histograms, metadata set on a dataset opened read-only — in an <c>.aux.xml</c> file it writes next
/// to the raster when the dataset is closed. "Read-only" in its vocabulary is about the raster's own
/// bytes, not about the directory. That is harmless in a directory this application owns and wrong
/// in one it does not: a terrain build reads an operator's directory of rasters where it lies, and
/// that directory must come back exactly as it was, not with a sidecar beside every file a build
/// happened to open.
/// </para>
/// <para>
/// Thread-local rather than process-wide, because the same library serves page rendering, uploads
/// and map footprints on other threads, and a sidecar is what lets some of those remember a
/// computed statistic. Held for the whole of the work that opens and closes the operator's rasters:
/// the decision is taken when a dataset is first opened, and acted on when it is closed.
/// </para>
/// </remarks>
public readonly struct GdalSidecarsOff : IDisposable
{
    private const string Option = "GDAL_PAM_ENABLED";

    private readonly string? previous;

    private GdalSidecarsOff(string? previous) => this.previous = previous;

    /// <summary>Switches the library's sidecar files off on this thread.</summary>
    public static GdalSidecarsOff OnThisThread()
    {
        GdalRuntime.Configure();
        var previous = Gdal.GetThreadLocalConfigOption(Option, null);
        Gdal.SetThreadLocalConfigOption(Option, "NO");
        return new GdalSidecarsOff(previous);
    }

    /// <inheritdoc />
    public void Dispose() => Gdal.SetThreadLocalConfigOption(Option, previous);
}
