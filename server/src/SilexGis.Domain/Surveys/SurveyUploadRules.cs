// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Surveys;

/// <summary>One of a cave's surveys, as far as matching an upload to it needs.</summary>
public sealed record CaveSurveyKey(Guid Id, SurveyOrigin Source, string? ExternalKey);

/// <summary>How an upload found the survey it is a version of.</summary>
public enum CaveSurveyMatchKind
{
    /// <summary>The upload named the survey by the id the server answered for it before.</summary>
    ById = 0,

    /// <summary>The cave already has a survey from the same tool under the same name.</summary>
    ByExternalKey = 1,

    /// <summary>Nothing matched; the upload starts a new survey.</summary>
    New = 2,

    /// <summary>The upload named a survey id the cave does not have.</summary>
    NotFound = 3,
}

/// <summary>The survey an upload belongs to: an existing one (with its id), a new one, or none at all.</summary>
public sealed record CaveSurveyMatch(CaveSurveyMatchKind Kind, Guid? SurveyId = null);

/// <summary>Which of a cave's surveys an upload is a version of.</summary>
/// <remarks>
/// <para>
/// In order: the survey id the device sent — a device remembers the id the server answered for
/// each of its surveys, and that id is the one thing that cannot be mistaken; else the survey of
/// the same cave from the same tool under the same tool-given name; else a new survey. The name is
/// compared exactly, as the tool wrote it: tools name surveys the way they name files, and two
/// surveys whose names differ only in case are two surveys.
/// </para>
/// <para>
/// A survey with no tool-given name is never matched by name, because "no name" is not a name two
/// uploads share on purpose.
/// </para>
/// </remarks>
public static class CaveSurveyMatching
{
    /// <summary>The longest tool-given survey name kept.</summary>
    public const int MaxExternalKeyLength = 255;

    /// <summary>The form an external key is compared and stored in: trimmed, and null when blank.</summary>
    public static string? KeyOf(string? surveyName) =>
        string.IsNullOrWhiteSpace(surveyName) ? null : surveyName.Trim();

    /// <param name="requestedSurveyId">The survey id the upload named, if any.</param>
    /// <param name="source">The tool the upload came from.</param>
    /// <param name="surveyName">The tool's own name for the survey, as sent.</param>
    /// <param name="surveysOfCave">Every survey of the cave the upload goes to.</param>
    public static CaveSurveyMatch Match(
        Guid? requestedSurveyId,
        SurveyOrigin source,
        string? surveyName,
        IReadOnlyCollection<CaveSurveyKey> surveysOfCave)
    {
        if (requestedSurveyId is { } id)
        {
            return surveysOfCave.Any(s => s.Id == id)
                ? new CaveSurveyMatch(CaveSurveyMatchKind.ById, id)
                : new CaveSurveyMatch(CaveSurveyMatchKind.NotFound);
        }

        var key = KeyOf(surveyName);
        if (key is not null)
        {
            var same = surveysOfCave.FirstOrDefault(s =>
                s.Source == source && string.Equals(s.ExternalKey, key, StringComparison.Ordinal));
            if (same is not null)
            {
                return new CaveSurveyMatch(CaveSurveyMatchKind.ByExternalKey, same.Id);
            }
        }

        return new CaveSurveyMatch(CaveSurveyMatchKind.New);
    }
}

/// <summary>Who a cave made from a device upload is open to, and which caving group it is bound to.</summary>
/// <param name="Visibility">The cave's visibility.</param>
/// <param name="CavingGroupId">The group the cave is bound to, or null for none.</param>
/// <param name="RefusalCode">Set, with the other two meaningless, when the choice cannot be honoured.</param>
public sealed record SurveyCaveAudience(Visibility Visibility, Guid? CavingGroupId, string? RefusalCode = null)
{
    public bool Refused => RefusalCode is not null;
}

/// <summary>
/// The visibility and caving group a cave made from a device upload gets.
/// </summary>
/// <remarks>
/// <para>
/// Any of the four visibilities may be asked for — public included, which is the uploader's own
/// choice to make. Visibility decides who reads the cave's record; whether its position is
/// disclosed stays the business of its protection, exactly as for a cave made on the web.
/// </para>
/// <para>
/// With no visibility asked for, the cave is open to the uploader's club when there is one to
/// name, and private otherwise. The club is the account's own club when the account is a member of
/// it, else its only group; an account in several groups and with no club of its own gets a
/// private cave rather than a group picked for it.
/// </para>
/// <para>
/// This decides only which group is meant. Whether the uploader may bind a cave to that group is
/// the binding rule's question, asked by the caller afterwards like any other cave creation.
/// </para>
/// </remarks>
public static class SurveyCaveVisibility
{
    /// <summary>A group-visible cave was asked for and the account is in no group to open it to.</summary>
    public const string InvalidCode = "survey_upload.visibility_invalid";

    /// <summary>A group-visible cave was asked for, the account is in several, and none was named.</summary>
    public const string GroupRequiredCode = "survey_upload.caving_group_required";

    /// <summary>The group a cave is bound to when the request names none, or null when none can be told.</summary>
    public static Guid? DefaultGroup(IReadOnlyCollection<Guid> memberships, Guid? ownClubId)
    {
        if (ownClubId is { } club && memberships.Contains(club))
        {
            return club;
        }

        return memberships.Count == 1 ? memberships.First() : null;
    }

    /// <param name="requested">The visibility asked for, or null.</param>
    /// <param name="requestedGroupId">The group asked for, or null.</param>
    /// <param name="memberships">The caving groups the uploader is a member of.</param>
    /// <param name="ownClubId">The uploader's own club as their profile states it, if any.</param>
    public static SurveyCaveAudience Resolve(
        Visibility? requested,
        Guid? requestedGroupId,
        IReadOnlyCollection<Guid> memberships,
        Guid? ownClubId)
    {
        var group = requestedGroupId ?? DefaultGroup(memberships, ownClubId);

        if (requested is null)
        {
            return group is null
                ? new SurveyCaveAudience(Visibility.Private, null)
                : new SurveyCaveAudience(Visibility.CavingGroup, group);
        }

        if (requested == Visibility.CavingGroup && group is null)
        {
            return new SurveyCaveAudience(
                Visibility.CavingGroup, null, memberships.Count > 1 ? GroupRequiredCode : InvalidCode);
        }

        // A group named outright is bound whatever the visibility, as on the web's cave form; one
        // only defaulted to is bound only when the cave is opened to it.
        var bound = requestedGroupId ?? (requested == Visibility.CavingGroup ? group : null);
        return new SurveyCaveAudience(requested.Value, bound);
    }
}

/// <summary>
/// Who an exported file without a survey format of its own — a drawing, a track, a table — is open
/// to once a device upload attaches it to the cave, and which caving group it is bound to.
/// </summary>
/// <remarks>
/// <para>
/// Such a file becomes an ordinary document, so it is created on the same two questions any other
/// document is: may the uploader create a document at all (bound to the group, where that is what
/// lets a club's ruleset admit its members), and may they bind one to that group. Holding write on
/// the cave answers neither — binding content to a club hands the club's rulesets over it, and a
/// writer of the club's cave who is not in the club must not be able to move rights that way.
/// </para>
/// <para>
/// On a location-protected cave the file stays the uploader's own and unbound: a plan or a track
/// names positions as surely as a survey source does. Elsewhere it is bound to the cave's group when
/// the uploader may bind there, and left unbound otherwise. A group-visible document with no group
/// would be readable by nobody but its owner, so: asked for outright, that is refused with the
/// reason; only inherited from the cave, it falls back to private.
/// </para>
/// </remarks>
public static class SurveyDocumentAudience
{
    /// <param name="ctx">The uploader.</param>
    /// <param name="caveProtected">Whether the cave's position is protected, by itself or an ancestor.</param>
    /// <param name="caveVisibility">The cave's own visibility, which the document takes when none was asked for.</param>
    /// <param name="caveGroupId">The caving group the cave is bound to, if any.</param>
    /// <param name="requested">The documents' visibility the upload asked for, or null.</param>
    public static SurveyCaveAudience Resolve(
        AccessContext ctx,
        bool caveProtected,
        Visibility caveVisibility,
        Guid? caveGroupId,
        Visibility? requested)
    {
        if (caveProtected)
        {
            return CreateRules.MayCreate(ctx, AccessDomain.Documents, (Guid?)null)
                ? new SurveyCaveAudience(Visibility.Private, null)
                : new SurveyCaveAudience(Visibility.Private, null, CreateRules.ForbiddenCode);
        }

        var visibility = requested ?? caveVisibility;
        if (caveGroupId is { } group
            && CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, group)
            && CreateRules.MayCreate(ctx, AccessDomain.Documents, group))
        {
            return new SurveyCaveAudience(visibility, group);
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, (Guid?)null))
        {
            return new SurveyCaveAudience(visibility, null, CreateRules.ForbiddenCode);
        }

        if (visibility == Visibility.CavingGroup)
        {
            return requested is null
                ? new SurveyCaveAudience(Visibility.Private, null)
                : new SurveyCaveAudience(visibility, null, SurveyCaveVisibility.InvalidCode);
        }

        return new SurveyCaveAudience(visibility, null);
    }
}
