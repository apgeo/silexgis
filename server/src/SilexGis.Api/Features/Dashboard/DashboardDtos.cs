// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Dashboard;

/// <summary>The kind of record an activity item points at, so the client can route the link.</summary>
public enum DashboardActivityKind
{
    Cave = 0,
    SurfaceFeature = 1,
    TripLog = 2,
}

/// <summary>
/// Registry sizes as the caller sees them: every count is visibility-filtered, so two users
/// legitimately see different numbers for the same installation.
/// </summary>
public sealed record DashboardCountsDto(int Caves, int SurfaceFeatures, int TripLogs, int Geofiles);

/// <summary>
/// One recently-touched record. Deliberately carries no geometry — the dashboard never emits
/// coordinates, so cave location protection has no second code path to go wrong here; the
/// linked detail page applies it.
/// </summary>
public sealed record DashboardActivityItemDto(
    DashboardActivityKind Kind,
    Guid Id,
    string? Name,
    DateTimeOffset UpdatedAt);

public sealed record DashboardSummaryDto(
    DashboardCountsDto Counts,
    IReadOnlyList<DashboardActivityItemDto> RecentActivity);
