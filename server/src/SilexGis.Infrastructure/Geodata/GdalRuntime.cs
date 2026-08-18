// SPDX-License-Identifier: AGPL-3.0-or-later
using MaxRev.Gdal.Core;
using OSGeo.GDAL;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The one place the raster library is brought up, for every part of this application that uses it.
/// </summary>
/// <remarks>
/// <para>
/// Registering the drivers is idempotent and every user of the library used to do it for itself,
/// which was harmless right up until there was a second thing to say at startup. This is that
/// second thing, and it is the kind that is invisible when it is missing: a setting whose default
/// is wrong only on the machine nobody develops on.
/// </para>
/// </remarks>
public static class GdalRuntime
{
    private static readonly Lock Gate = new();
    private static bool configured;

    /// <summary>Registers the drivers and settles where the library may write while it works.</summary>
    public static void Configure()
    {
        lock (Gate)
        {
            if (configured)
            {
                return;
            }

            GdalBase.ConfigureAll();

            // Several of the library's writers do their work in a scratch file and move it into
            // place at the end — the cloud-optimised writer always does, because it cannot know the
            // overviews until it has written the full-resolution image once. Where that scratch file
            // goes is decided by this setting, and when it is unset the library falls back to the
            // process's current directory. In a container that directory is the one the application
            // was copied into, owned by root while the process runs as somebody else, so the fallback
            // is not a slower path or an untidy one — it is a refusal, reported as a permission error
            // against a file name nobody recognises, from a step that appears to be reading rather
            // than writing. Development on Windows never sees it, because TEMP is always set there.
            //
            // Anything writing something large enough to care which disk it lands on overrides this
            // for the duration; see the terrain raster preparer, whose intermediates are measured in
            // gigabytes and belong beside the build rather than on the container's own layer.
            Gdal.SetConfigOption("CPL_TMPDIR", Path.GetTempPath());

            configured = true;
        }
    }
}
