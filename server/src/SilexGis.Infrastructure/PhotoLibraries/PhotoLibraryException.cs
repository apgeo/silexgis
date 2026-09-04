// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// A call to a neighbouring photo library that did not produce an answer, carrying the stable code
/// the endpoint reports.
///
/// <para>
/// The codes are kept apart because they look alike in a log and send whoever has to act on them to
/// completely different places: nobody configured this, the token is wrong, the library is having
/// trouble, we asked something it does not understand, we asked for more than it will give, and —
/// the one that is not like the others — it answered a picture request without a picture, which
/// means its own originals may be out of reach.
/// </para>
/// </summary>
public sealed class PhotoLibraryException(string code, string? detail = null, Exception? inner = null)
    : Exception(detail ?? code, inner)
{
    /// <summary>The stable machine code, reported as the Problem Details <c>code</c>.</summary>
    public string Code { get; } = code;

    /// <summary>No address or token is configured, so this installation cannot reach the library at all.</summary>
    public const string NotConfiguredCode = "photo_library.not_configured";

    /// <summary>The library refused the credential. An administrator has to supply a working one.</summary>
    public const string UnauthorizedCode = "photo_library.unauthorized";

    /// <summary>The library could not be reached, or did not answer in time, or kept failing.</summary>
    public const string UnavailableCode = "photo_library.unavailable";

    /// <summary>
    /// The library understood the request and would not serve it, or answered something this
    /// application cannot read. That is a defect on this side or a change on the far side — either
    /// way it is not something a caller can fix.
    /// </summary>
    public const string RejectedCode = "photo_library.rejected";

    /// <summary>More was asked for than this installation is willing to ask a library for.</summary>
    public const string TooLargeCode = "photo_library.too_large";

    /// <summary>
    /// A picture request came back as something that was not a picture, which is how a library
    /// whose originals are out of reach answers. Picture requests to it are stopped until an
    /// operator rechecks, because against a detached disk a retry is a deletion.
    /// </summary>
    public const string OriginalsUnavailableCode = "photo_library.originals_unavailable";
}
