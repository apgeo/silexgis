// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A camp or a project that gathers many trips into one thing with one report — a fortnight
/// underground written up as one record rather than twenty disconnected logs.
/// </summary>
/// <remarks>
/// <para>
/// It carries the owner/caving-group/visibility trio, so it is governed exactly like the trips
/// inside it and is the object a partner club is invited across in one act.
/// </para>
/// <para>
/// A camp exists before it happens, and on a longer horizon than a trip: people book leave for
/// it, a club commits money to it and a partner is invited to it months ahead. It shares the
/// planning states with a trip while keeping its own table of the moves between them, because two
/// kinds that agree today are still two decisions.
/// </para>
/// </remarks>
public class Expedition : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>What the camp is for and what it is about — the narrative, free text.</summary>
    public string? Description { get; set; }

    /// <summary>The first day of the camp.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// The last day, or null when the camp lasts a single day. An end equal to the start is
    /// stored as null so that one day never reads as a range of itself, and every reader can
    /// ask "did this run on past its first day" by testing one column for null.
    /// </summary>
    /// <remarks>
    /// <see cref="DayRange.EndForStorage"/> is where that normalisation is applied; the database
    /// holds the same rule as a constraint so a writer that skips the helper fails loudly rather
    /// than producing a row every reader has to second-guess.
    /// </remarks>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Roughly where the camp works — a massif, a valley, a permit area. Not a position anybody
    /// navigates by and not derived from the caves the trips reach: it is the area drawn on the
    /// plan before the first trip exists, and it stays what it was drawn as afterwards.
    /// </summary>
    public Geometry? Geom { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>
    /// Where the camp has got to, from somebody's idea through to the write-up being announced.
    /// A new expedition starts as a draft: it is being written and nobody has been told.
    /// </summary>
    /// <remarks>
    /// Never consulted when deciding who may read the row. Visibility and the access entries
    /// answer that on their own, and a second rule saying who may read a row is how the two come
    /// to disagree — a draft with public visibility is public, and that is correct.
    /// </remarks>
    public ActivityState State { get; set; } = ActivityState.Draft;

    /// <summary>
    /// When the expedition was first announced, or null while it never has been. Stamped once
    /// and never cleared, so de-announcing and announcing again does not move it.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
