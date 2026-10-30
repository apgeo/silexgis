// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Common;

/// <summary>
/// The one fixed ceiling the raster uploads share — elevation rasters for a terrain build and
/// scanned map sheets — and the request limits derived from it.
/// </summary>
/// <remarks>
/// A raster travels inside a multipart form, so the request that carries a legal raster is the
/// raster plus the form's own boundaries and headers. A request limit set to the raster ceiling
/// itself refuses a raster of exactly the advertised size with a bare 413 that names no limit.
/// The process-wide multipart ceiling has to clear the same figure, or lowering the configurable
/// file limit would cut these routes off at a size no message names. Kept here, in one place,
/// so raising the raster ceiling raises every limit in front of it together.
/// </remarks>
public static class RasterUploadLimits
{
    /// <summary>The largest raster a single upload may carry.</summary>
    public const long MaxRasterBytes = 512L * 1024 * 1024;

    /// <summary>The request body limit on a route that accepts a raster of up to <see cref="MaxRasterBytes"/>.</summary>
    public const long MaxRequestBytes = MaxRasterBytes + FilesOptions.MultipartEnvelopeBytes;
}
