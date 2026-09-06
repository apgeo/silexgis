// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// Whether a neighbouring photo library was found to be answering.
///
/// <para>
/// Three values rather than two, because "nobody asked" is a real and common answer and must not
/// be written down as "no". A library an operator has supplied no address for is not a library
/// that failed, and an installation running none of these products is a supported installation.
/// </para>
/// </summary>
public enum LibraryReach
{
    /// <summary>Nothing was asked of it, so nothing is claimed. No socket was opened.</summary>
    Unknown = 0,

    /// <summary>It answered. Whether it accepted this installation's credential is a separate question.</summary>
    Reachable = 1,

    /// <summary>It was asked and did not answer — no route, no listener, or no answer in time.</summary>
    Unreachable = 2,
}

/// <summary>
/// What one neighbouring photo library said about itself when it was last asked.
///
/// <para>
/// This exists because "configured" and "working" are different facts and an installation that
/// confuses them looks healthy until somebody opens the map and finds it empty. An address and a
/// credential in a settings file prove that an operator typed something, not that anything answers
/// on the other end, not that the credential is still accepted, and not that it carries the rights
/// this integration needs.
/// </para>
/// <para>
/// Every field is separately three-valued in effect — a library can answer while refusing the
/// credential, and can accept the credential while withholding a right — and they are kept apart
/// for the same reason the error codes are: they send whoever has to act on them to completely
/// different places.
/// </para>
/// </summary>
/// <param name="Reach">Whether it answered at all.</param>
/// <param name="Version">
/// What it says it is, where the product says so. Null where it does not, and null whenever it did
/// not answer — never a guess, because a version an operator reads is a version they will quote in
/// a bug report.
/// </param>
/// <param name="MissingPermissions">
/// The rights this integration needs that the configured credential does not carry, named the way
/// the far side names them so they can be found on its own screen. Empty when the credential is
/// sufficient <em>and</em> when the product has no way of being asked — an empty list is "nothing
/// known to be missing" rather than a promise, which is why the failure code is carried beside it.
/// </param>
/// <param name="FailureCode">
/// The stable code for what went wrong, or null when nothing did. Present alongside a reachable
/// library when it answered and refused: a credential the library will not accept is a different
/// errand from a container that is not running.
/// </param>
/// <param name="ProbedAt">
/// When this was read. Null only when nothing was asked. An operator looking at a health line is
/// entitled to know whether it describes now or describes a minute ago.
/// </param>
public sealed record LibraryHealth(
    LibraryReach Reach,
    string? Version,
    IReadOnlyList<string> MissingPermissions,
    string? FailureCode,
    DateTimeOffset? ProbedAt)
{
    /// <summary>Nothing was asked. The answer for a library no operator has configured.</summary>
    public static LibraryHealth NotAsked { get; } =
        new(LibraryReach.Unknown, Version: null, MissingPermissions: [], FailureCode: null, ProbedAt: null);

    /// <summary>Asked, and no answer came back.</summary>
    public static LibraryHealth DidNotAnswer(string failureCode) =>
        new(LibraryReach.Unreachable, Version: null, MissingPermissions: [], failureCode, DateTimeOffset.UtcNow);

    /// <summary>
    /// It answered. Whether the answer was a good one is what the other two arguments say.
    /// </summary>
    public static LibraryHealth Answered(
        string? version,
        IReadOnlyList<string>? missingPermissions = null,
        string? failureCode = null) =>
        new(LibraryReach.Reachable, version, missingPermissions ?? [], failureCode, DateTimeOffset.UtcNow);
}
