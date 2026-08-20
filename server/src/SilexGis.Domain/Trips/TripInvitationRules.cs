// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// Who may put somebody on a trip's list, who may answer for whom, and what a note beside an
/// answer may say — as pure rules, so the answer is the same wherever it is asked. Endpoints
/// load state, ask here, and persist; nothing re-derives these answers locally.
/// </summary>
/// <remarks>
/// <para>
/// Reading is deliberately absent from this class. Who may see a trip's list is exactly who may
/// see the trip, which is a question for the trip's own access rules and must stay a single
/// question: a list that answered it separately would become a way of learning that a trip
/// exists, and which people are on it, without being allowed to open it.
/// </para>
/// <para>
/// The rest follows the shape the discussion on a document already uses. Answering asks for
/// nothing beyond being able to open the plan and being the person the answer is about — a
/// trip list only its organisers could join would not be a list of who is coming — while
/// answering <em>for somebody else</em> is a right over the trip, because it is putting words
/// in another person's mouth on a record that may be read while looking for them.
/// </para>
/// </remarks>
public static class TripInvitationRules
{
    /// <summary>
    /// Cap on the remark beside an answer. Long enough for the condition somebody's yes comes
    /// with, short enough that the list stays a list of answers rather than a second place the
    /// trip is described. The same length a roster remark gets, so the two never disagree about
    /// what fits.
    /// </summary>
    public const int MaxNoteLength = 500;

    /// <summary>
    /// The note as it should be stored: surrounding whitespace removed, everything else exactly
    /// as typed. Null when what remains is nothing at all — an answer needs no words with it.
    /// </summary>
    public static string? Normalize(string? note)
    {
        var trimmed = note?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Everything wrong with a proposed note — empty when it is acceptable. Measured after
    /// normalizing, so trailing whitespace cannot push a legitimate note past the cap.
    /// </summary>
    public static IReadOnlyList<string> ValidateNote(string? note)
    {
        var normalized = Normalize(note);
        return normalized is not null && normalized.Length > MaxNoteLength
            ? [$"a note is at most {MaxNoteLength} characters"]
            : [];
    }

    /// <summary>
    /// Whether the caller is the person an answer is about. There is no path from an account to
    /// a roster entry in the authorization context — most people on a roster have no account at
    /// all — so the subject's account, where they have one, is looked up and compared here.
    /// <paramref name="subjectAccountId"/> is null for a person with no account, and nobody is
    /// ever that person: an unattributable "I am them" is not a claim this system can check.
    /// </summary>
    public static bool AnswersForSelf(Guid callerId, Guid? subjectAccountId) =>
        callerId != Guid.Empty && subjectAccountId is { } account && account == callerId;

    /// <summary>
    /// Whether <paramref name="callerId"/> may set or change the standing answer of the person
    /// <paramref name="subjectAccountId"/> stands for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three ways in, and they are three different rights. Answering for yourself asks only for
    /// an account and for the plan being reachable at all, which is the prior question the
    /// caller has already passed to be here — the person who sees the trip is the person whose
    /// answer it is. Answering for anybody is the right to write the trip, which is what running
    /// it means: somebody has to be able to write down the answer of a member who telephoned.
    /// Nobody else may rewrite what another person said.
    /// </para>
    /// <para>
    /// A full administrator is named here rather than left to be covered by the write right,
    /// even though today an installation's full administrators hold every right over everything
    /// and the branch can never be the only one that admits them. It is stated because the rule
    /// is about administration rather than about the trip: an answer that has to be put straight
    /// after an account is gone, or on a trip nobody is left to write, is a thing an
    /// administrator does, and that must not depend on the write right continuing to reach them.
    /// </para>
    /// </remarks>
    public static bool MayAnswerFor(Guid callerId, Guid? subjectAccountId, bool mayWriteTrip, bool isFullAdmin) =>
        callerId != Guid.Empty
        && (AnswersForSelf(callerId, subjectAccountId) || mayWriteTrip || isFullAdmin);

    /// <summary>
    /// Whether <paramref name="callerId"/> may put somebody on this trip's list, or take them
    /// off it. Editing the list is running the trip, so it is the trip's write right and nothing
    /// narrower — and unlike answering there is no self branch, because inviting yourself is
    /// answering, and it has its own route.
    /// </summary>
    public static bool MayInvite(Guid callerId, bool mayWriteTrip, bool isFullAdmin) =>
        callerId != Guid.Empty && (mayWriteTrip || isFullAdmin);
}
