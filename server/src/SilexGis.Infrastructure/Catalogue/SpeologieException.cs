// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Catalogue;

/// <summary>
/// A call to the cave catalogue that did not produce an answer, carrying the stable code the
/// endpoint reports.
///
/// <para>
/// The codes separate four situations that look alike in a log and are completely different to
/// whoever has to act on them: nobody configured this, the key is wrong, their service is having
/// trouble, and we asked for something their service does not understand. Collapsing them into
/// one "catalogue error" sends an administrator to check a key that is fine.
/// </para>
/// </summary>
public sealed class SpeologieException(string code, string? detail = null, Exception? inner = null)
    : Exception(detail ?? code, inner)
{
    /// <summary>The stable machine code, reported as the Problem Details <c>code</c>.</summary>
    public string Code { get; } = code;

    /// <summary>No API key is configured, so this installation cannot reach the catalogue at all.</summary>
    public const string NotConfiguredCode = "speologie.not_configured";

    /// <summary>The catalogue refused the key. An administrator has to supply a working one.</summary>
    public const string UnauthorizedCode = "speologie.unauthorized";

    /// <summary>The catalogue could not be reached, or did not answer in time, or kept failing.</summary>
    public const string UnavailableCode = "speologie.unavailable";

    /// <summary>
    /// The catalogue understood the request and rejected it. That is this application's fault or
    /// a change at the far end — either way it is a defect here, not something a caller can fix.
    /// </summary>
    public const string RejectedCode = "speologie.rejected";
}
