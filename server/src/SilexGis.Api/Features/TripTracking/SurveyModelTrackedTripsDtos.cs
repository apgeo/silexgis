// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// One trip tracked on a survey model, as the caller may know it: the trip's watch points at the
/// model, or some of the trip's reports were recorded against it.
/// </summary>
/// <param name="TripDate">
/// Nullable on the wire although every trip carries a date today, so a reader keys its ordering
/// on whichever of the three instants is present rather than assuming this one is.
/// </param>
/// <param name="WatchesThisModel">
/// The trip's watch currently points at this model. False for a trip whose watch was re-pointed
/// at another survey after reporting on this one — its reports here still count.
/// </param>
/// <param name="ReportCount">
/// The trip's reports recorded against this model whose place is open to the caller, under the
/// per-report withholding the event log applies. A report kept back from the caller is not
/// counted and does not move the first and last instants.
/// </param>
public sealed record TrackedTripDto(
    Guid TripLogId,
    string Title,
    DateOnly? TripDate,
    DateOnly? TripDateEnd,
    TripTrackingState State,
    DateTimeOffset? ArmedAt,
    DateTimeOffset? ClosedAt,
    bool WatchesThisModel,
    int ReportCount,
    DateTimeOffset? FirstReportAt,
    DateTimeOffset? LastReportAt);
