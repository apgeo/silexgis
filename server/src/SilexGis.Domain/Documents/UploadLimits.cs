// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>
/// Everything an installation will refuse an upload for, gathered into one value so the
/// answer can be given before the transfer starts as well as after it arrives.
///
/// <para>
/// Both askings matter, and they are not the same asking. Told up front, the limits are
/// advice — the client shows what is allowed and how much room is left, and never starts a
/// transfer it knows will be refused. Asked again when the bytes land, they are the rule.
/// A client that skipped the advice, or one whose advice went stale while a slow upload was
/// in flight, is refused by the same function that drew the notice.
/// </para>
/// </summary>
/// <param name="MaxUploadBytes">Largest single file accepted.</param>
/// <param name="UserQuotaBytes">
/// How much this person may hold in total, or null for no personal limit. Counted against
/// the documents they own rather than the files they happened to send: content that has been
/// handed over to a club is that club's to account for, and a person who uploads a survey and
/// then transfers it should not still be paying for it.
/// </param>
/// <param name="UserUsedBytes">How much they already hold.</param>
/// <param name="StoreQuotaBytes">
/// How much the whole installation may hold, or null for no limit. Separate from the personal
/// one and checked as well as it: a club with ten members and 200 GB of disk needs a ceiling
/// that does not depend on nobody using their full share.
/// </param>
/// <param name="StoreUsedBytes">How much it already holds.</param>
/// <param name="AcceptedExtensions">
/// Extensions this installation takes, lower-case and with the leading dot. Empty means
/// everything is taken, which is the default and the sane one for an archive whose whole
/// purpose is holding whatever a caving club has accumulated.
/// </param>
/// <param name="RefusedExtensions">
/// Extensions this installation will not take, whatever <paramref name="AcceptedExtensions"/>
/// says. Empty by default. A refusal list beats an acceptance list where both name the same
/// extension, because the operator who wrote the refusal was being specific.
/// </param>
public sealed record UploadAllowance(
    long MaxUploadBytes,
    long? UserQuotaBytes,
    long UserUsedBytes,
    long? StoreQuotaBytes,
    long StoreUsedBytes,
    IReadOnlyList<string> AcceptedExtensions,
    IReadOnlyList<string> RefusedExtensions)
{
    /// <summary>
    /// Room left for this person: the smaller of what their own quota leaves and what the
    /// installation's leaves, or null when neither limit exists. Never negative — a quota
    /// lowered below what somebody already holds leaves them no room rather than a debt.
    /// </summary>
    public long? RemainingBytes
    {
        get
        {
            var mine = UserQuotaBytes is { } quota ? Math.Max(0, quota - UserUsedBytes) : (long?)null;
            var installation = StoreQuotaBytes is { } cap ? Math.Max(0, cap - StoreUsedBytes) : (long?)null;
            return (mine, installation) switch
            {
                (null, null) => null,
                ({ } m, null) => m,
                (null, { } i) => i,
                ({ } m, { } i) => Math.Min(m, i),
            };
        }
    }
}

/// <summary>Whether an upload may proceed, and the stable code to say why when it may not.</summary>
public static class UploadLimits
{
    /// <summary>
    /// Why this upload must be refused, or null when it may proceed.
    /// </summary>
    /// <param name="fileName">
    /// The name the upload carries. Only its extension is read; the name itself never
    /// reaches storage, which is server-generated.
    /// </param>
    /// <param name="sizeBytes">How large the file is, or claims to be.</param>
    /// <remarks>
    /// The order the checks run in is the order a person would want to be told about them.
    /// Emptiness first, because a zero-byte file is a mistake rather than a limit; then the
    /// per-file size, which is a fact about this file; then the type, which is a fact about
    /// the installation; then the quota, which is a fact about how full things are and the
    /// only one of the four the uploader can fix by deleting something.
    /// </remarks>
    public static string? Refuse(UploadAllowance allowance, string? fileName, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        if (sizeBytes <= 0)
        {
            return UploadItemReasons.Empty;
        }

        if (sizeBytes > allowance.MaxUploadBytes)
        {
            return UploadItemReasons.TooLarge;
        }

        if (!IsExtensionAccepted(allowance, fileName))
        {
            return UploadItemReasons.TypeNotAccepted;
        }

        // Compared against room rather than against the quota, so that the two ceilings are
        // one answer and neither can be passed by an upload the other one happened to allow.
        if (allowance.RemainingBytes is { } remaining && sizeBytes > remaining)
        {
            return UploadItemReasons.QuotaExceeded;
        }

        return null;
    }

    /// <summary>
    /// Whether this installation takes files of this name's type. A name with no extension at
    /// all is taken whenever there is no acceptance list — the archive holds whatever a club
    /// has, and a survey exported without an extension is still a survey — and refused when
    /// there is one, since it matches nothing on it.
    /// </summary>
    public static bool IsExtensionAccepted(UploadAllowance allowance, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(allowance);

        var extension = ExtensionOf(fileName);

        if (allowance.RefusedExtensions.Any(e => Same(e, extension)))
        {
            return false;
        }

        return allowance.AcceptedExtensions.Count == 0
            || allowance.AcceptedExtensions.Any(e => Same(e, extension));
    }

    /// <summary>
    /// The lower-case extension of a name, dot included, or an empty string when it has none.
    /// </summary>
    /// <remarks>
    /// Read from the last dot rather than by any path parsing, because this is handed names
    /// from archives and from browsers as well as from the disk, and a file called
    /// <c>survey.2019.tif</c> has one extension whatever produced it. A name that is nothing
    /// but a dotted prefix — <c>.gitignore</c> — has no extension by this reading, which is
    /// the same answer the platform gives and the same answer an operator writing a list
    /// would expect.
    /// </remarks>
    public static string ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var lastDot = fileName.LastIndexOf('.');
        return lastDot <= 0 || lastDot == fileName.Length - 1
            ? string.Empty
            : fileName[lastDot..].ToLowerInvariant();
    }

    /// <summary>
    /// Whether a configured extension names the same type as an observed one. Configuration is
    /// written by hand, so both spellings — with the dot and without — mean the same thing.
    /// </summary>
    private static bool Same(string configured, string extension)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var normalized = configured.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = '.' + normalized;
        }

        return string.Equals(normalized, extension, StringComparison.Ordinal);
    }
}
