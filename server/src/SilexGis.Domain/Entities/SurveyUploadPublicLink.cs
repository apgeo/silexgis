// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A public link to one survey upload's results: an address anybody holding it may open — without
/// an account, or, for a link made for the organization, with any account of this installation —
/// showing the run's progress and the files it produced — never the uploaded files,
/// the compiler's log, the party, a station name, the cave's identity beyond its name, or a
/// position.
/// </summary>
/// <remarks>
/// <para>
/// The address is a high-entropy opaque token, never the upload's id, and the row is found by
/// the token's SHA-256 only. Unlike a share link, the token is also kept encrypted under the
/// application's data-protection keys — which live with the application, not in the database —
/// so that the people who may write to the cave can be shown the address again on the status
/// page and the device's result dialog, which is what the link is for. A copy of the database on
/// its own still cannot open a live link.
/// </para>
/// <para>
/// At most one live link per upload; making a new one revokes the old, which then answers as an
/// address that never existed. Not part of the cave's timeline: the row holds a credential's
/// hash and ciphertext, and the act it records is visible on the upload itself.
/// </para>
/// </remarks>
public class SurveyUploadPublicLink : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SurveyUploadId { get; set; }

    /// <summary>SHA-256 of the token, base64url — what a request is looked up by.</summary>
    public required string TokenHash { get; set; }

    /// <summary>The token, encrypted with the application's data-protection keys, to show it again.</summary>
    public required string TokenProtected { get; set; }

    /// <summary>The account whose act made the link: the uploader at completion, or whoever made it again.</summary>
    public Guid CreatedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Who may open the link: anybody holding the address, or only a signed-in account of this installation.</summary>
    public SurveyUploadLinkAudience Audience { get; set; } = SurveyUploadLinkAudience.Public;

    /// <summary>
    /// The account that asked, after being warned, for a link although the cave's position is
    /// protected; null for an ordinary link. A link that carries this keeps answering while the
    /// cave stays protected; an ordinary link closes the moment the cave becomes protected.
    /// </summary>
    public Guid? ProtectedAcknowledgedBy { get; set; }

    /// <summary>When that acknowledgement was given; set together with <see cref="ProtectedAcknowledgedBy"/>.</summary>
    public DateTimeOffset? ProtectedAcknowledgedAt { get; set; }

    /// <summary>Whether the link was made with the explicit acknowledgement that the cave is protected.</summary>
    public bool ProtectedAcknowledged => ProtectedAcknowledgedBy is not null;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Who a survey upload's link opens for. Stored as smallint; values are part of the schema
/// contract — append, never renumber.
/// </summary>
public enum SurveyUploadLinkAudience
{
    /// <summary>Anybody holding the address, without an account.</summary>
    Public = 0,

    /// <summary>Anybody holding the address who is signed in to an account of this installation; no right on the cave is needed.</summary>
    Organization = 1,
}
