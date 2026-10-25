// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One account's own choices for the surveys it uploads from a device. An account with no row has
/// the defaults: nothing added to what each upload asks for, and public result links on.
/// </summary>
/// <remarks>
/// Typed columns rather than members of the account's interface-preference document, because the
/// server acts on these — the processing run reads the outputs, the completion reads the link
/// switch — and that document is deliberately one the server stores without reading.
/// </remarks>
public class SurveyUploadPreference : ITimestamped
{
    /// <summary>The account, and the key: an account has one set of these choices.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Outputs every upload of this account produces on top of what it asked for, by their names in
    /// the output catalogue. A name the catalogue no longer has is ignored, not refused, so a
    /// change of catalogue never breaks an account's uploads.
    /// </summary>
    public string[] AlwaysOutputs { get; set; } = [];

    /// <summary>
    /// Whether this account's uploads get a public link to their results — when the installation
    /// allows them and the cave is not location-protected.
    /// </summary>
    public bool PublicLinks { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
