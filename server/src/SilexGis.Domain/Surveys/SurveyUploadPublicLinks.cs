// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The rules of a survey upload's public link: whether one is made, how its token is made and
/// stored, and the stage list its page shows.
/// </summary>
public static class SurveyUploadPublicLinks
{
    /// <summary>The installation does not make public links (its switch is off).</summary>
    public const string InstallationOff = "installation-off";

    /// <summary>The uploader's account preference says not to make one.</summary>
    public const string AccountOff = "account-off";

    /// <summary>The account that completed the upload may not hand the cave to people outside the installation.</summary>
    public const string NoShareRight = "no-share-right";

    /// <summary>
    /// The cave's position is protected — the cave itself or anything above it. A public page of a
    /// survey's outputs is the measured drawing of the cave over a grid; there is no version of it
    /// that withholds the position it exists to show.
    /// </summary>
    public const string ProtectedCave = "protected-cave";

    /// <summary>A link was made and somebody who may write to the cave withdrew it.</summary>
    public const string Revoked = "revoked";

    /// <summary>The upload itself asked for no link, whatever the account's preference says.</summary>
    public const string NotWanted = "not-wanted";

    /// <summary>The longest token a request may present; anything longer is not looked up at all.</summary>
    public const int MaxTokenLength = 100;

    /// <summary>
    /// Why a link is not made, or null when it is. Checked in this order so the reason given is the
    /// one a person could act on first: an installation that makes no links at all, an account that
    /// asked for none, a caller without the right, and only then the cave. A protected cave is
    /// refused unless the person making the link acknowledged, explicitly and for this link, that
    /// its results show where the cave's passages are — no switch or preference stands in for that.
    /// </summary>
    public static string? Refusal(
        bool installationOn, bool accountOn, bool mayShare, bool caveProtected, bool protectedAcknowledged = false) =>
        !installationOn ? InstallationOff
        : !accountOn ? AccountOff
        : !mayShare ? NoShareRight
        : caveProtected && !protectedAcknowledged ? ProtectedCave
        : null;

    /// <summary>
    /// Whether, and how, a completed upload gets its link. The upload's own choice comes first: it
    /// may refuse a link the account's preference would have made, or ask for one the preference
    /// would not; when it says nothing the preference decides. The audience is the upload's, else
    /// the account's default. The acknowledgement for a protected cave is the upload's only.
    /// </summary>
    public static SurveyUploadLinkDecision AtCompletion(
        bool installationOn,
        bool accountOn,
        SurveyUploadLinkAudience accountAudience,
        bool? wanted,
        SurveyUploadLinkAudience? audience,
        bool allowProtected,
        bool mayShare,
        bool caveProtected)
    {
        var refusal = installationOn && wanted == false
            ? NotWanted
            : Refusal(installationOn, wanted ?? accountOn, mayShare, caveProtected, allowProtected);
        return new SurveyUploadLinkDecision(
            refusal,
            audience ?? accountAudience,
            ProtectedAcknowledged: refusal is null && caveProtected && allowProtected);
    }

    /// <summary>
    /// Whether a live link still opens. Asked again on every read: an ordinary link closes the
    /// moment its cave becomes protected, because protection is switched on by somebody who has just
    /// decided the cave needs it; a link made with the explicit acknowledgement that the cave is
    /// protected was made knowing that, and keeps answering while the cave stays protected.
    /// </summary>
    public static bool Opens(bool caveProtected, bool protectedAcknowledged) =>
        !caveProtected || protectedAcknowledged;

    /// <summary>
    /// Whether a link's audience admits the caller. A public link admits anybody; an organization
    /// link admits any account signed in to this installation and asks nothing more of it — no right
    /// on the cave — because holding the address is what the link grants.
    /// </summary>
    public static bool Admits(SurveyUploadLinkAudience audience, bool signedIn) =>
        audience == SurveyUploadLinkAudience.Public || signedIn;

    /// <summary>
    /// A fresh token. The same kind, made the same way, as every other link this application hands
    /// to people without an account — 32 random bytes, base64url — so there is one token rule.
    /// </summary>
    public static string NewToken() => TripTrackingRules.NewShareToken();

    /// <summary>What a token is looked up by: SHA-256 of its characters, base64url.</summary>
    public static string Hash(string token) => TripTrackingRules.ShareTokenHash(token);

    /// <summary>Whether a presented token is worth looking up at all.</summary>
    public static bool IsPlausible(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= MaxTokenLength;

    /// <summary>The order the stages of a run go in.</summary>
    public static IReadOnlyList<SurveyUploadStage> StageOrder => Order;

    private static readonly SurveyUploadStage[] Order =
    [
        SurveyUploadStage.Archive,
        SurveyUploadStage.Unpack,
        SurveyUploadStage.Scaffold,
        SurveyUploadStage.Compile,
        SurveyUploadStage.Store,
        SurveyUploadStage.Read,
    ];

    /// <summary>
    /// Each stage's state for a page that shows the run as a list of steps, worked out from where
    /// the upload stands, the stage it is in or reached last, and whether it was asked to compile.
    /// The same rule the signed-in page applies, so the two pages never disagree.
    /// </summary>
    public static IReadOnlyList<(SurveyUploadStage Stage, SurveyUploadStageState State)> StageStates(
        SurveyUploadStatus status, SurveyUploadStage? stage, bool compile)
    {
        var index = stage is { } s ? Array.IndexOf(Order, s) : -1;
        return [.. Order.Select((st, i) => (st, StateOf(status, index, i, compile, st)))];
    }

    private static SurveyUploadStageState StateOf(
        SurveyUploadStatus status, int index, int i, bool compile, SurveyUploadStage stage)
    {
        if (!compile && stage is SurveyUploadStage.Scaffold or SurveyUploadStage.Compile)
        {
            return SurveyUploadStageState.Skipped;
        }

        return status switch
        {
            SurveyUploadStatus.Running when index < 0 => i == 0 ? SurveyUploadStageState.Active : SurveyUploadStageState.Pending,
            SurveyUploadStatus.Running => i < index ? SurveyUploadStageState.Done
                : i == index ? SurveyUploadStageState.Active : SurveyUploadStageState.Pending,
            SurveyUploadStatus.Failed when index < 0 => i == 0 ? SurveyUploadStageState.Failed : SurveyUploadStageState.Pending,
            SurveyUploadStatus.Failed => i < index ? SurveyUploadStageState.Done
                : i == index ? SurveyUploadStageState.Failed : SurveyUploadStageState.Pending,
            SurveyUploadStatus.Succeeded when index < 0 => SurveyUploadStageState.Done,
            SurveyUploadStatus.Succeeded => i <= index ? SurveyUploadStageState.Done : SurveyUploadStageState.Skipped,
            _ => SurveyUploadStageState.Pending,
        };
    }
}

/// <summary>
/// What a completed upload's link comes to: the refusal (null when a link is made), the audience it
/// opens for, and whether it is made with the acknowledgement that the cave is protected — true only
/// when the cave is protected and the upload asked for the link anyway.
/// </summary>
public sealed record SurveyUploadLinkDecision(string? Refusal, SurveyUploadLinkAudience Audience, bool ProtectedAcknowledged);

/// <summary>How far one stage of a run has got, for a page that lists them.</summary>
public enum SurveyUploadStageState
{
    Pending = 0,
    Active = 1,
    Done = 2,
    Failed = 3,
    Skipped = 4,
}
