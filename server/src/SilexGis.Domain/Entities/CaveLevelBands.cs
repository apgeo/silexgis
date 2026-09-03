// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The levels somebody decided a cave was cut at.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic proposes levels from the heights the survey measured; this is the row that says a
/// person looked at that proposal and agreed with it, or edited it, or wrote a different one. The
/// two are deliberately separate things: the proposal is recomputed from the line work every time
/// it is asked for and changes when the survey does, while this does not change until somebody
/// changes it. Storing the proposal instead would freeze a computed figure and then quietly
/// disagree with the survey it was computed from.
/// </para>
/// <para>
/// <b>The bands are elevations, and an elevation is a coordinate.</b> Reading this row is gated on
/// being able to place the cave exactly, exactly as the histogram it was confirmed from is —
/// otherwise the saved record becomes the disclosure the histogram refused.
/// </para>
/// <para>
/// <b>Superseded rather than overwritten.</b> A confirmation is somebody's reading of a cave and a
/// later one does not make the earlier one never have happened; the row stays, stamped, and the
/// live one is the one that is not. Which row is live is settled by a filtered unique index in the
/// database rather than by the handler that writes it, because a rule enforced only in the write
/// path holds until two people press save at the same moment.
/// </para>
/// </remarks>
public class CaveLevelBands : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The cave whose levels these are (feature id of the cave).</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>Who confirmed them.</summary>
    public Guid ConfirmedBy { get; set; }

    /// <summary>
    /// The bands themselves, as a JSON array of <c>{"fromM":…,"toM":…,"label":…}</c> ascending by
    /// lower edge. Held as a document rather than as child rows because nothing queries across
    /// them: they are read back whole, for one cave, and written whole.
    /// </summary>
    public string Bands { get; set; } = "[]";

    /// <summary>What the person wanted to say about the reading, if anything.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// When this confirmation stopped being the current one. Null on the live row, and the column
    /// the uniqueness of "current" is filtered on. Stamped explicitly by the write path rather than
    /// by the timestamp interceptor, which owns only the created and updated columns.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
