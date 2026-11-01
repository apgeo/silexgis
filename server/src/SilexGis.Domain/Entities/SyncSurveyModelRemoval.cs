// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The record that a survey model once existed and was removed — kept so that a phone holding the
/// model is told it has gone. Written by the database, never by the application: a trigger on the
/// survey models table adds one row for every model row deleted, by whichever path deleted it.
/// </summary>
/// <remarks>
/// Models are deleted outright, not marked: by a person in the browser, and by the cascade when
/// their cave is removed for good. A record written by each path would be one the next path
/// forgets; the trigger sees them all. The cave is copied from the deleted row because nothing
/// else will still name it.
/// </remarks>
public class SyncSurveyModelRemoval
{
    /// <summary>Insertion order. Not an identity the outside world sees.</summary>
    public long Id { get; set; }

    /// <summary>The removed model's own identifier — what a phone holds it under.</summary>
    public Guid SurveyModelId { get; set; }

    /// <summary>The cave the model belonged to, as it was when the row went.</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>When the row went — the database's clock, at the deleting statement.</summary>
    public DateTimeOffset RemovedAt { get; set; }
}
