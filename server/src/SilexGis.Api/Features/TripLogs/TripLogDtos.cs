// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// A person on a trip: their roster id, the name to show, their account if any, and what they
/// did there.
/// </summary>
/// <remarks>
/// Appended to, never inserted into: this record is positional and now carries two nullable
/// times side by side, which would swap silently if anything were put between them. The times
/// and the note answer to the trip's own visibility and nothing narrower — they are told to
/// whoever may read the trip, and they travel in nothing the trip itself would not travel in.
/// </remarks>
public sealed record TripParticipantDto(
    Guid CaverId,
    string Name,
    Guid? UserId,
    long RoleId,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Note);

/// <summary>
/// Someone to put on a trip: an existing roster entry, or a name to add one for. Naming a person
/// who is not in the roster yet is how a trip records the people who never sign in — the author
/// needs no roster-keeping rights for it, only the right to write the trip.
/// </summary>
/// <remarks>
/// <para>
/// <c>RoleId</c> omitted means the role of the list the entry arrived in, which is what keeps an
/// ordinary roster a list of names: most people were simply there. Naming a role puts the person
/// on the trip in that job instead, and the same person named twice in two jobs is two rows,
/// which is what the roster is unique on.
/// </para>
/// <para>
/// The times mean "the trip's, unless stated" — they are not a record of what is unknown, and
/// leaving them out is the normal case rather than an omission. Appended, never inserted, for
/// the reason the reading above gives.
/// </para>
/// </remarks>
public sealed record TripParticipantWrite(
    Guid? CaverId,
    string? NewCaverName,
    long? RoleId,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Note);

public sealed record TripLogDto(
    Guid Id,
    string Title,
    // The purpose, as a row in the trip-purpose vocabulary. Only the identity travels: a client
    // reads that vocabulary once and renders every trip's purpose from it, so a renamed row does
    // not leave old readings behind.
    long? TripTypeId,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Description,
    string? Results,
    string? WeatherConditions,
    string? LocationText,
    Guid? OrganizingCavingGroupId,
    GeoJsonGeometry? Geom,
    // Read-only, and derived: the caves any of the trip's roles names, whatever it did there,
    // with two sorts taken out — the ones this caller may not read, and the ones they may not
    // place. Both, and in that order: naming a cave is a read of the cave, and a trip's audience
    // is not the cave's, so an identifier here would be a way of asking for a cave nobody meant
    // this reader to have. Recording a cave is done through the roles themselves, so this list
    // has no counterpart on the write request — one place to write it, one reading of it here.
    IReadOnlyList<Guid> CaveIds,
    // The roster, split where the surfaces that read it split: everybody who was there in
    // whatever job, and separately whoever put the trip forward. Between them they are every row
    // — a job the vocabulary grew after this was written still arrives in the first list rather
    // than vanishing — and each row says which role it holds, so nothing has to be guessed from
    // which list it came in.
    IReadOnlyList<TripParticipantDto> Participants,
    IReadOnlyList<TripParticipantDto> Proposers,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Appended, and appended only. This record is constructed positionally and has runs of
    // members of the same type, so a value inserted in the middle is absorbed silently by the
    // neighbour it displaces. The lifecycle pair is deliberately absent from the write request
    // below: a state moves through the transition endpoint, which is the only place the legal
    // moves are checked.
    ActivityState State,
    DateTimeOffset? PublishedAt,
    // The counted facts. Metres throughout — the number travels bare and a reader formats it for
    // its locale, because a unit alongside would have to be trusted per row.
    decimal? DepthReachedM,
    decimal? LengthSurveyedM,
    int? SurveyStations,
    decimal? RopeMetres,
    // Whether something went wrong, which every caller who may read the trip is told. What
    // happened is a different question with a different audience.
    bool HadIncident,
    // The three per-purpose sections, each as the object it was stored as, and each with the
    // version of its purpose's schema it was measured against — null while it has never been
    // measured. A reader that wants to know why a value looks odd needs the version it was
    // written under, not only the schema that happens to be current.
    JsonElement FieldData,
    int? FieldDataSchemaVersion,
    JsonElement Logistics,
    int? LogisticsSchemaVersion,
    // Absent — not empty — for a caller who may read the trip but not change it. What went
    // wrong names identifiable people making mistakes, so it is told to the group already
    // trusted with what the trip says about them; that something went wrong is told to
    // everyone, above. Null and "{}" are different answers on purpose: a stored section is
    // always an object, so nothing at all can only mean "not yours to read", and a surface
    // drawing it can say so rather than show an empty section reading as "nothing happened".
    JsonElement? Safety,
    int? SafetySchemaVersion,
    // The camp this trip was gathered into, or absent when it was not gathered into one — and
    // absent, too, when the camp is one this caller may not read, so a trip never names a thing
    // its reader has no right to know exists. Read-only: membership is written through the
    // camp's own doors, which is where the rule that a trip belongs to at most one lives.
    Guid? ExpeditionId,
    // How many of the caves this trip names were left off the list above, for either reason. A
    // count and never an identifier: the number lets a surface say the list is short instead of
    // letting it read as the whole truth, and it says nothing about which caves are missing —
    // an identifier is exactly what the withholding was for. Zero for a caller shown everything.
    int CavesWithheld,
    // How many people the trip has room for, null when it states no limit — which is what a trip
    // has until somebody says otherwise. It never refuses a write: somebody saying they are coming
    // to a full trip is recorded as having said so, and who is on it and who is waiting is worked
    // out from the answers whenever it is asked rather than written down anywhere. Appended, like
    // everything before it.
    int? MaxParticipants,
    // The callout, read by everyone who may read the trip. Four values and not three: the times
    // that were arranged, where the check stands, and when the check last actually ran.
    //
    // The last of them is the one that is easy to leave out and the one the feature turns on. An
    // armed check is a promise that something is watching, and a surface that draws the promise
    // without saying when it was last kept lets a check that has not run since yesterday read
    // exactly like a check that ran a minute ago and found nothing wrong. Null means no pass has
    // ever completed, which is the strongest form of the same warning rather than a reassuring
    // absence — and only a pass that finished is counted, because one that failed checked nothing.
    DateTimeOffset? ExpectedReturnAt,
    DateTimeOffset? CalloutAlarmAt,
    TripCalloutState CalloutState,
    DateTimeOffset? CalloutLastCheckedAt,
    // Whether this caller may say the party is out. Answered here rather than worked out by
    // whatever draws the trip, because the rule is the route's and a second copy of it on a
    // surface would be free to disagree — and the disagreement that matters is the quiet one,
    // where somebody entitled to stand an alarm down is shown no way to do it. False for every
    // reader of a trip that has no live check, so nothing has to pair this with the state.
    bool CanStandDownCallout,
    // Where the party gathers, and the way in to it where a club drew one. Appended, like
    // everything before it. Told to every reader of the trip and to nobody else — the same
    // audience the sketch above has, and deliberately not a wider one: this is the position a
    // plan exists to state, so a reader who is not told the trip's caves is still told where to
    // be. What that costs is written on the column itself and said in words on the surface that
    // draws it.
    GeoJsonGeometry? MeetingGeom,
    // How much of the list this trip's purpose names has been settled. Appended, like everything
    // before it. Null on a trip whose purpose names no list and on one whose list this caller may
    // not read — the list answers to its own audience, and a reference from a trip is not consent.
    //
    // It is advisory and it decides nothing. It is not a state the trip is in, no write is refused
    // because of it, and it is never consulted when working out who may read the trip: a trip with
    // nothing settled is exactly as visible as one fully settled, to exactly the same people.
    TripChecklistReadinessDto? ChecklistReadiness);

/// <summary>
/// What a trip has settled of the list its purpose names: <c>Ticked</c> of <c>Total</c>, computed
/// from the lines and the confirmations each time it is asked for and stored nowhere.
/// </summary>
public sealed record TripChecklistReadinessDto(Guid ChecklistId, int Ticked, int Total);

/// <summary>
/// The audience a trip this caller plans would get if the request names none.
/// <paramref name="CavingGroupName"/> is set exactly when that audience is the caller's own
/// group, so a form can name it instead of reciting both halves of the rule.
/// </summary>
public sealed record TripPlanDefaultDto(
    Visibility Visibility, Guid? CavingGroupId, string? CavingGroupName);

public sealed record TripLogWriteRequest(
    string Title,
    long? TripTypeId,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Description,
    string? Results,
    string? WeatherConditions,
    string? LocationText,
    Guid? OrganizingCavingGroupId,
    GeoJsonGeometry? Geom,
    // Omitted means "not editing which caves this trip is about" — the trip's roles keep
    // whatever they name. A surface that records caves through the roles themselves leaves it
    // out rather than echoing a list back, so saving a trip from such a surface cannot disturb
    // what the roles say. Supplied, it is the plain list of caves the trip is about, reconciled
    // under the plainest role that carries that meaning.
    IReadOnlyList<Guid>? CaveIds,
    // The roster, whole: the two lists together replace every row the trip has, in every role,
    // so a job left out of them is a job withdrawn. That is what makes the lists a roster rather
    // than an edit to part of one — and it is why a surface that shows people must send back the
    // rows it did not show. Each list supplies the role its entries take when they name none.
    IReadOnlyList<TripParticipantWrite> Participants,
    IReadOnlyList<TripParticipantWrite>? Proposers,
    Guid? CavingGroupId,
    // Nullable, and the nullability is the whole point of it. Who may read a trip is a decision
    // somebody takes, so the shape has to tell "private, deliberately" from "I have not said" —
    // the first value of the vocabulary is the zero value, and a non-nullable field would read
    // every request that leaves the field out as choosing the narrowest audience there is. On
    // creation an unstated audience is answered by the rule for the door the request came
    // through; on an update it means the stored audience is not being edited.
    Visibility? Visibility,
    // Appended for the same reason the reading above is: this record is positional too, and it
    // now has a run of three nullable decimals that would absorb each other silently.
    decimal? DepthReachedM,
    decimal? LengthSurveyedM,
    int? SurveyStations,
    decimal? RopeMetres,
    // Not nullable: a trip either had an incident or it did not, and a request that says nothing
    // says it did not — the same reading the column's default gives a row nobody has touched.
    bool HadIncident,
    // An omitted section means "not editing this section", the same reading the cave list above
    // gets: a surface that draws one section must not blank the two it never showed. Supplied,
    // the object replaces what was stored whole — keys the current schema does not know about
    // survive only because the surface that sent it sent them back, which is the rule it works
    // under.
    JsonElement? FieldData,
    JsonElement? Logistics,
    JsonElement? Safety,
    // How many the trip has room for, and null means it states no limit rather than "not editing
    // it" — there is nothing else null could mean for a number whose absence is the unlimited
    // case, so clearing the field is how a limit is removed. Appended, like the run above it.
    int? MaxParticipants,
    // Where the party gathers. Written straight through and null clears it, exactly as the
    // sketch above is — which means a surface that draws the trip whole must send this back
    // whether or not it drew the field, or saving anything else on the trip erases it.
    GeoJsonGeometry? MeetingGeom);

/// <summary>
/// Arranging — or calling off — the check that notices if a party does not come back.
/// </summary>
/// <remarks>
/// <para>
/// Its own request, and deliberately not two more fields on the trip's write request. That
/// request replaces the whole trip, so a surface that never drew these fields would send them
/// absent and quietly call off a live callout — and a party would find out that nothing had been
/// watching them only by nobody coming to look. A separate door cannot be omitted by accident.
/// </para>
/// <para>
/// Where the check has got to is not on here either. It follows from the times: an alarm hour
/// where there was none, or one that differs from the hour stored, is a new arrangement and arms
/// the check; clearing the hour calls the whole thing off; and the same hour sent again leaves the
/// state exactly as it was, which is what stops a re-save re-arming a check that somebody already
/// stood down. Saying the party is out is a different act by different people and has its own
/// route.
/// </para>
/// </remarks>
public sealed record TripCalloutRequest
{
    /// <summary>When the party says they will be out, or null while they name no hour.</summary>
    public DateTimeOffset? ExpectedReturnAt { get; init; }

    /// <summary>
    /// When the alarm goes off if nobody has said the party is out. Null calls the arrangement
    /// off — there is nothing else an arrangement with no hour in it could mean.
    /// </summary>
    public DateTimeOffset? CalloutAlarmAt { get; init; }
}

public sealed class TripCalloutRequestValidator : AbstractValidator<TripCalloutRequest>
{
    public TripCalloutRequestValidator()
    {
        // The alarm is when somebody starts worrying, so it cannot fall before the hour the party
        // said they would be out: an alarm set earlier reports everyone overdue while they are
        // still walking out on schedule, and an alarm nobody believes is worse than none at all.
        // Checked only when both are given — a party may name an hour to be missed by without
        // committing to an hour of return, and the other way about.
        RuleFor(x => x.CalloutAlarmAt)
            .GreaterThanOrEqualTo(x => x.ExpectedReturnAt!.Value)
            .When(x => x.CalloutAlarmAt is not null && x.ExpectedReturnAt is not null)
            .WithMessage("The callout alarm cannot be set before the party is due back.");
    }
}

/// <summary>The state to move a trip log into.</summary>
/// <remarks>
/// Nullable, and it has to be. The state is the whole of this request, and the vocabulary's first
/// member is the zero value, so a non-nullable field would read a body that names no state at all
/// as naming the draft — and since every live state has a legal move back there, an empty body
/// would quietly take a trip's announcement back and answer 200. Nullable lets the shape tell
/// "absent" from "draft" and refuse the first.
/// </remarks>
public sealed record TripLogTransitionRequest
{
    public ActivityState? State { get; init; }
}

public sealed class TripLogWriteRequestValidator : AbstractValidator<TripLogWriteRequest>
{
    public TripLogWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        // A purpose is a row now, so whether it exists is a question for the database and is
        // asked on the write path beside the other reference checks; there is nothing about the
        // shape of the value to check here.
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.Results).MaximumLength(10000);
        RuleFor(x => x.WeatherConditions).MaximumLength(300);
        RuleFor(x => x.LocationText).MaximumLength(300);

        RuleFor(x => x.TripDateEnd)
            .GreaterThanOrEqualTo(x => x.TripDate)
            .When(x => x.TripDateEnd is not null)
            .WithMessage("Trip end date must not precede the start date.");

        // The measured facts. Two rules each, and both are load-bearing:
        //
        // Not negative, because none of these has a meaning below zero — depth is measured
        // downwards, and a trip cannot un-survey passage or carry minus rope.
        //
        // Not wider than the column, because the column is a fixed-scale numeric and a value it
        // cannot hold is refused by the database with a message about numeric overflow, several
        // layers below anything that could turn it into an answer. Stating the ceiling here is
        // what makes an impossible figure a plain bad request. The ceilings are generous by
        // design: they are the shape of the storage, not a judgement about how deep a cave gets.
        RuleFor(x => x.DepthReachedM)
            .InclusiveBetween(0m, 999_999.9m)
            .When(x => x.DepthReachedM is not null);
        RuleFor(x => x.LengthSurveyedM)
            .InclusiveBetween(0m, 99_999_999.9m)
            .When(x => x.LengthSurveyedM is not null);
        RuleFor(x => x.RopeMetres)
            .InclusiveBetween(0m, 999_999.9m)
            .When(x => x.RopeMetres is not null);
        RuleFor(x => x.SurveyStations)
            .GreaterThanOrEqualTo(0)
            .When(x => x.SurveyStations is not null);
        // A cave list is recorded as link memberships, which are bounded — every read of a link
        // resolves all of them at once. Refusing an over-long list here says so plainly, rather
        // than accepting it and spreading one trip's caves over links a reader has to reassemble.
        RuleFor(x => x.CaveIds!)
            .Must(caves => caves.Count <= ResLinkRules.MaxMembers)
            .When(x => x.CaveIds is not null)
            .WithMessage($"A trip records at most {ResLinkRules.MaxMembers} caves.");
        // A section is an object or it is nothing: the schemas describe objects, and a bare
        // array or number would be refused several layers down with a message about a schema
        // rather than about the request.
        RuleFor(x => x.FieldData).Must(BeAnObject).When(x => x.FieldData is not null)
            .WithMessage("Field data must be a JSON object.");
        RuleFor(x => x.Logistics).Must(BeAnObject).When(x => x.Logistics is not null)
            .WithMessage("Logistics must be a JSON object.");
        RuleFor(x => x.Safety).Must(BeAnObject).When(x => x.Safety is not null)
            .WithMessage("Safety must be a JSON object.");
        // A limit of nought is not a small trip, it is a refusal expressed as a number, and a
        // negative one is nothing at all. Absent is how a trip says it has no limit.
        RuleFor(x => x.MaxParticipants).GreaterThan(0)
            .When(x => x.MaxParticipants is not null)
            .WithMessage("A trip with a limit has room for at least one person.");

        RuleFor(x => x.Participants).NotNull();
        // Proposers are optional (a trip needn't record who proposed it); a null list is
        // treated as empty. Each supplied entry still follows the shared identity rules.
        RuleForEach(x => x.Participants).SetValidator(new TripParticipantValidator());
        RuleForEach(x => x.Proposers).SetValidator(new TripParticipantValidator());
    }

    private static bool BeAnObject(JsonElement? value) => value?.ValueKind == JsonValueKind.Object;
}

/// <summary>Shared identity rules for both attendee and proposer entries.</summary>
public sealed class TripParticipantValidator : AbstractValidator<TripParticipantWrite>
{
    public TripParticipantValidator()
    {
        RuleFor(p => p)
            .Must(p => (p.CaverId is not null) ^ !string.IsNullOrWhiteSpace(p.NewCaverName))
            .WithMessage("Each person is either an existing caver or a new name, not both.");
        RuleFor(p => p.NewCaverName)
            .MaximumLength(200)
            .WithMessage("Names are limited to 200 characters.");
        RuleFor(p => p.Note)
            .MaximumLength(500)
            .WithMessage("A note about one person's part in the trip is limited to 500 characters.");

        // No rule that the exit follows the entry, deliberately, and for the reason the trip's
        // own pair has none: a time carries no day, so coming out at 02:00 having gone in at
        // 21:00 is an ordinary night trip rather than a mistake. Which day either time belongs to
        // is read from the trip's date range by whoever works out how long somebody was under.
    }
}

public sealed class TripLogTransitionRequestValidator : AbstractValidator<TripLogTransitionRequest>
{
    public TripLogTransitionRequestValidator()
    {
        // That a state was named at all, and that the value is one the vocabulary has. Whether a
        // trip may hold it, and whether it may get there from where it is, are the transition
        // table's to answer — and it answers both with one refusal, so there is no second place a
        // state can be judged. The presence check cannot be left to the table: an absent field
        // arrives as the enum's zero value, which is a state the table admits.
        RuleFor(x => x.State).NotNull().IsInEnum();
    }
}
