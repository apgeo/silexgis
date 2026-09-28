// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A depth in one cave, the station that depth means, and what people call the place.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Word out of a cave arrives as a depth or as a name — "he's at minus four
/// hundred", "they're at the bivouac" — and almost never as a station of the survey. Turning a
/// depth into a station can be done by measurement, and the watch already does that: the nearest
/// station to the datum under the trip's filter. But measurement answers the wrong question
/// wherever the cave's own shape disagrees with its arithmetic — parallel shafts share a depth, a
/// meander wanders up and down, and the station nearest minus four hundred may be somewhere nobody
/// goes. So a cave may declare the answers outright: the depths people actually report, the station
/// each of them means, and the name they say instead of either.
/// </para>
/// <para>
/// <b>The depth is the key, one row per depth per cave</b>, because that is how the declaration is
/// read: a report of a depth asks this table what it means before falling back to measuring. Two
/// rows for one depth would be a cave that answers its own question twice.
/// </para>
/// <para>
/// <b>Kept on the cave and not on a survey model</b>, holding the station as a name. That is the
/// same shape a report already takes — a station reference is the name in the viewer's spelling,
/// never a coordinate — and it is what lets a declaration outlive the survey it was written
/// against: a re-exported model with the same station names keeps working, and one that renamed
/// them makes a declaration stop resolving, which is visible and fixable. Anchoring to a model
/// would instead make every declaration vanish the day a cave gained a corrected survey.
/// </para>
/// <para>
/// <b>The place label is a third fact, not a decoration.</b> It is what a caver says on the phone,
/// and it is what a screen can offer instead of asking somebody to pick a depth or type a station:
/// one list, ordered by depth, in the words the club uses. A row may be declared without one — a
/// depth that matters with no name people use for it is an ordinary thing.
/// </para>
/// </remarks>
public class CaveDepthPlace : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The cave this declaration belongs to. Always a cave, never a place inside one.</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>
    /// Metres below the cave's entrance datum, positive downwards — the same sense a reported depth
    /// is given in, so that the two can be compared without anybody remembering a sign.
    /// </summary>
    public decimal DepthM { get; set; }

    /// <summary>
    /// The station this depth means, in the survey viewer's own spelling.
    /// </summary>
    /// <remarks>
    /// The viewer's spelling rather than the survey table's, for the reason a recorded position
    /// carries the same: it is read by the surface that draws a marker on the model, and a name in
    /// the other spelling is a marker that silently never appears.
    /// </remarks>
    public required string ViewerStationName { get; set; }

    /// <summary>
    /// What people call this place, or null where there is no such word.
    /// </summary>
    public string? PlaceLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    /// <summary>
    /// A declaration is a fact about the cave, so it surfaces on the cave's own timeline: somebody
    /// asking why a reported depth started meaning a different station is asking about the cave.
    /// </summary>
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}
