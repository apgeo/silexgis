// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Scans;

/// <summary>
/// Which upload origins make a scan: a LAS or LAZ point cloud sent as it is, and a raw scanner
/// recording turned into one. Both are measured under the scans' storage cap, run on the
/// long-work lane, keep a recipe as their run options and show a point cloud as their result.
/// </summary>
/// <remarks>A query that cannot call this names both origins itself, as the storage figures do.</remarks>
public static class ScanOrigins
{
    /// <summary>Whether an upload of this origin makes a scan.</summary>
    public static bool IsScan(SurveyOrigin origin) => origin is SurveyOrigin.PointCloud or SurveyOrigin.Mandeye;
}
