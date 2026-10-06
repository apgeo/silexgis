// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Expeditions;

/// <summary>
/// How a write-up the application generated for a camp is told apart from one a club wrote and
/// uploaded by hand: the generated one carries the camp's mark in its name.
/// </summary>
/// <remarks>
/// <para>
/// The camp's counterpart of the mark a trip's generated write-up carries, and the same two things
/// hang on it. Regenerating a write-up replaces the earlier generated one in the camp's report
/// slot and nothing else there, so a club's own report is never swept out by somebody pressing
/// the button. And deleting the camp takes the generated one with it, because it is a derivative
/// of the camp and means nothing once the camp is gone, while a hand-written report in the same
/// slot is library material and stays. Both readings take the mark from here so they cannot
/// drift.
/// </para>
/// <para>
/// Its own word in front, never the trip's. A camp gathers trips, each of which may carry a
/// generated write-up of its own, and neither a camp's regeneration nor its delete may ever be
/// able to recognise one of those as the camp's: they are the trips', and stay with them.
/// </para>
/// </remarks>
public static class ExpeditionReportNaming
{
    /// <summary>The prefix every generated write-up of this camp is named with.</summary>
    public static string GeneratedPrefix(Guid expeditionId) =>
        $"expedition-report-{expeditionId.ToString("N")[..8]}-";
}
