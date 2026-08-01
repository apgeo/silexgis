// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Dashboard;

/// <summary>
/// The kind of record an activity item points at, so the client can route the link. The
/// feature members mirror the feature kinds: every one of them resolves under
/// /features/{id}, and the kinds with a typed page of their own keep their route.
/// </summary>
public enum DashboardActivityKind
{
    /// <summary>A data-driven feature — its feature type names the kind.</summary>
    Feature = 0,

    Cave = 1,

    CaveEntrance = 2,

    Centerline = 3,

    TripLog = 4,
}

/// <summary>
/// Registry sizes as the caller sees them: every count is visibility-filtered, so two users
/// legitimately see different numbers for the same installation. <paramref name="Features"/>
/// counts the data-driven kinds only — caves have their own number, and counting them twice
/// would make the two figures unreadable side by side.
/// </summary>
public sealed record DashboardCountsDto(int Caves, int Features, int TripLogs, int Geofiles);

/// <summary>
/// One recently-touched record. Deliberately carries no geometry — the dashboard never emits
/// coordinates, so location protection has no second code path to go wrong here; the linked
/// detail page applies it.
/// </summary>
public sealed record DashboardActivityItemDto(
    DashboardActivityKind Kind,
    Guid Id,
    string? Name,
    DateTimeOffset UpdatedAt);

public sealed record DashboardSummaryDto(
    DashboardCountsDto Counts,
    IReadOnlyList<DashboardActivityItemDto> RecentActivity);

/// <summary>Maps a feature's schema-level kind onto the activity kind the client routes on.</summary>
public static class DashboardActivityKinds
{
    public static DashboardActivityKind Of(FeatureKind kind) => kind switch
    {
        FeatureKind.Cave => DashboardActivityKind.Cave,
        FeatureKind.CaveEntrance => DashboardActivityKind.CaveEntrance,
        FeatureKind.Centerline => DashboardActivityKind.Centerline,
        _ => DashboardActivityKind.Feature,
    };
}
