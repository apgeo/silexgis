// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// The sizes the trip listing's bounded answers are cut at. Settings rather than constants so an
/// installation can move them and a test can lower them to where they bite; each is a backstop on
/// an answer that says when it was cut short, never a page size.
/// </summary>
public sealed class TripListOptions
{
    public const string SectionName = "TripList";

    /// <summary>
    /// How many trips one exported file holds. A file that stopped at a limit and a file that
    /// ended look identical once it is saved, so the bound is stated inside the file rather than
    /// left to be inferred from a round number of rows.
    /// </summary>
    public int MaxExportedTrips { get; set; } = 2000;

    /// <summary>
    /// How many trips a grouping slices. A grouping is a shape somebody reads at a glance, so it is
    /// bounded rather than streamed: past this the answer says it was cut short and the reader
    /// narrows the filter, which is the honest response to a question too big to answer.
    /// </summary>
    public int MaxGroupedTrips { get; set; } = 2000;

    /// <summary>
    /// How many values the two open-ended facets — the people and the areas — hand back. A club's
    /// roster runs to hundreds and a panel listing all of them is the wall of names the panel
    /// exists to replace; the ones worth offering are the ones the current filter actually reaches,
    /// longest count first. Whatever the caller has already chosen is offered on top of the cap, so
    /// a shared link naming somebody far down the roster still opens onto a control that says who
    /// it is, and the answer says when values past the cap were left out. Somebody past the cap and
    /// not already chosen is reached by narrowing the rest of the filter until they surface.
    /// </summary>
    public int MaxOpenFacetValues { get; set; } = 50;
}
