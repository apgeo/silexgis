// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Sends the raster library's scratch files to a chosen directory for as long as this is held.
/// </summary>
/// <remarks>
/// <para>
/// Several of the library's writers work in a scratch file and move it into place at the end, and
/// the setting that says where is one value for the whole process — the library has no notion of
/// one caller's temporary directory. That is tolerable here because of what the value is used for:
/// every writer that reads it invents its own file name inside whatever directory it names, so two
/// pieces of work pointed at different directories do not collide, and one that ran while another
/// had this held would write somewhere writable rather than somewhere wrong. What must not happen —
/// and what this exists to prevent — is the directory being left pointing at a build's workspace
/// after the build is finished and the workspace has been deleted.
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
        var previous = Gdal.GetConfigOption(Option, null);
        Gdal.SetConfigOption(Option, Path.GetFullPath(directory));
        return new GdalScratchDirectory(previous);
    }

    /// <inheritdoc />
    public void Dispose() => Gdal.SetConfigOption(Option, previous);
}
