// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Which tool a survey came from. Stored as smallint; values are part of the schema contract —
/// append, never renumber.
/// </summary>
public enum SurveyOrigin : short
{
    /// <summary>A TopoDroid survey sent from the phone it was measured on.</summary>
    /// <remarks>
    /// Written as the tool's own lower-case name, the same word the upload routes advertise and
    /// accept, so a client can compare a listed survey's tool with what it was offered.
    /// </remarks>
    [JsonStringEnumMemberName("topodroid")]
    TopoDroid = 0,

    /// <summary>A Therion project.</summary>
    Therion = 1,

    /// <summary>Anything else.</summary>
    Other = 2,
}

/// <summary>
/// One survey of a cave: the thread the uploads of that survey hang on, and the heading its
/// models, sources and documents are grouped under on the cave page.
/// </summary>
/// <remarks>
/// <para>
/// A cave may hold any number of these and none of them is the cave's "default" survey: a survey
/// often covers only part of a cave, and assembling a whole cave out of several is not a job this
/// application takes on. Readers that need one model already fall back to the newest finished one.
/// </para>
/// <para>
/// Every upload is a version of exactly one survey, ordered by when it arrived. A re-upload finds
/// its survey by the id the server answered for it the first time, else by
/// (<see cref="CaveFeatureId"/>, <see cref="Source"/>, <see cref="ExternalKey"/>) — the database
/// holds that triple unique, so two uploads arriving at once cannot each start a survey of their own.
/// </para>
/// <para>
/// The row carries no rights of its own. It belongs to a cave and the cave answers for it on the
/// same terms as the cave's survey sources: what it groups can name fixed coordinates, so it is
/// withheld entirely from a caller without exact-location access to the cave.
/// </para>
/// </remarks>
public class CaveSurvey : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning cave's feature id (FK to the cave subtype row).</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>The heading the survey's artefacts are grouped under — the survey's own title.</summary>
    public required string Title { get; set; }

    public SurveyOrigin Source { get; set; }

    /// <summary>
    /// The tool's own name for the survey — what a re-upload of the same survey is recognised by
    /// when it does not carry this row's id. Null when the tool gave none; such a survey is never
    /// matched by name.
    /// </summary>
    public string? ExternalKey { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Surveys surface in their cave's timeline (features audit as "Feature").
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}
