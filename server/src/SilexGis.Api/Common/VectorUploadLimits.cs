// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Common;

/// <summary>
/// The fixed ceiling on a vector file uploaded for import (GPX, KML, GeoJSON, a zipped shapefile,
/// a delimited table), and the request limit derived from it.
/// </summary>
/// <remarks>
/// The route once carried no request limit of its own and so stopped at the web server's built-in
/// 30 MB, refusing anything larger with a bare 413 that named no limit. 150 MB is five times that.
/// It is a ceiling rather than a setting because the importer reads the whole file into memory
/// before writing its features: a few hundred megabytes of GeoJSON parse into several gigabytes,
/// which a small server does not have to spare beside everything else it runs.
/// </remarks>
public static class VectorUploadLimits
{
    /// <summary>The largest vector file a single upload may carry.</summary>
    public const long MaxVectorBytes = 150L * 1024 * 1024;

    /// <summary>The request body limit on the route that accepts a file of up to <see cref="MaxVectorBytes"/>.</summary>
    public const long MaxRequestBytes = MaxVectorBytes + FilesOptions.MultipartEnvelopeBytes;
}
