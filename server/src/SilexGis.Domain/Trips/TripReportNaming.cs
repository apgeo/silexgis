// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// How a write-up the application generated for a trip is told apart from one a club wrote and
/// uploaded by hand: the generated one is named after the trip.
/// </summary>
/// <remarks>
/// The name is the only mark the generated document carries — it is filed through the same ingest
/// as any upload, so no column says where it came from — and two things hang on it. Regenerating
/// a write-up replaces the earlier generated one in the report slot and nothing else there, so a
/// club's own report is never swept out by somebody pressing the button. And deleting the trip
/// takes the generated one with it, because it is a derivative of the trip and means nothing once
/// the trip is gone, while a hand-written report in the same slot is library material and stays.
/// Both readings take the mark from here so they cannot drift.
/// </remarks>
public static class TripReportNaming
{
    /// <summary>The prefix every generated write-up of this trip is named with.</summary>
    public static string GeneratedPrefix(Guid tripId) => $"trip-report-{tripId.ToString("N")[..8]}-";
}
