// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Who may look at and decide what accounts under review uploaded, and how much of it they are
/// shown.
/// </summary>
/// <remarks>
/// <para>
/// Both rights are installation-wide: a held batch belongs to no club until it is written, so the
/// question is asked of the domain and never of a row.
/// </para>
/// <para>
/// Holding the right to read the queue means seeing every held line whole — the rows it names and
/// edits, the true position of a protected cave or entrance, a waiting picture of a cave the
/// reviewer could not otherwise read. A reviewer has to see what they are asked to judge, and a
/// line shown in part cannot be judged. This is a deliberate exception to location protection, and
/// it is this one rule: it reaches held lines on the review routes and nothing else. Outside review
/// the same account reads by its ordinary rights, and accepting still writes with the uploader's
/// rights, never the reviewer's. Anybody else reading a held batch — its uploader, reading their
/// own — is shown it under the ordinary read and position rules.
/// </para>
/// </remarks>
public static class UploadReviewRules
{
    /// <summary>Whether the caller may read the review queue and every held batch in it.</summary>
    public static bool MayRead(AccessContext? ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.UploadReview, AccessAction.Read, null).Allowed;

    /// <summary>Whether the caller may accept or reject held lines.</summary>
    public static bool MayDecide(AccessContext? ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.UploadReview, AccessAction.Write, null).Allowed;

    /// <summary>
    /// Whether held lines are shown to the caller whole, with no part of them withheld by the
    /// caller's read rights or by location protection: exactly when the caller may read the queue.
    /// </summary>
    public static bool SeesHeldWhole(AccessContext? ctx) => MayRead(ctx);
}
