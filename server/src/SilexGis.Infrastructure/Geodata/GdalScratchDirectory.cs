// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Sends the raster library's scratch files to a chosen directory for as long as this is held.
/// </summary>
/// <remarks>
/// <para>
/// Several of the library's writers work in a scratch file and move it into place at the end. The
/// setting that says where is set for the calling thread only, which the library reads ahead of the
/// process-wide value: set for the whole process, every other piece of GDAL work running at the
/// same moment — a map's conversion, a derivative in another job lane — would put its scratch files
/// in this build's workspace, and lose them if the build's workspace is removed first. So it is
/// held across synchronous work only: a hold that spans an await can be released on another thread
/// and leave the first one pointing at a directory that may be deleted.
/// </para>
/// <para>
/// Restoring the previous value rather than clearing it, so that the default settled at startup
/// survives; clearing it would put back the library's own fallback, which is the current working
/// directory and is exactly the failure this pair of types was written for.
/// </para>
/// </remarks>
public readonly struct GdalScratchDirectory : IDisposable
{
    private const string Option = "CPL_TMPDIR";

    private readonly string? previous;

    private GdalScratchDirectory(string? previous) => this.previous = previous;

    /// <summary>Points the library's scratch files at <paramref name="directory"/>.</summary>
    /// <remarks>
    /// The directory must exist and be writable by this process; the library does not create it and
    /// reports the miss as a permission error against a file name the caller never chose.
    /// </remarks>
    public static GdalScratchDirectory At(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        GdalRuntime.Configure();
        var previous = Gdal.GetThreadLocalConfigOption(Option, null);
        Gdal.SetThreadLocalConfigOption(Option, Path.GetFullPath(directory));
        return new GdalScratchDirectory(previous);
    }

    /// <inheritdoc />
    public void Dispose() => Gdal.SetThreadLocalConfigOption(Option, previous);
}
