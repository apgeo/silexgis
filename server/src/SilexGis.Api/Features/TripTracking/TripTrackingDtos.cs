// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

// ---- reads ----

public sealed record TrackingTeamDto(Guid Id, string Title);

/// <summary>
/// One participant's current tracking state, folded from every report about them.
/// Position fields are null both when nothing places the caver yet and when the caller may
/// not learn the position — a reader without exact-location rights cannot tell the two
/// apart, which is the point.
/// </summary>
/// <param name="LastKind">
/// The kind of the latest report of <em>any</em> kind. It describes that report and nothing
/// else: it is not the kind the station came from, and standing must not be re-derived from it
/// — <paramref name="In"/> and <paramref name="Out"/> are the answer to that.
/// </param>
/// <param name="LastRecordedAt">
/// When anything was last heard about them, whatever it said. A note and a "come out" are
/// reports too, so this moves on word that carries no position at all.
/// </param>
/// <param name="PositionRecordedAt">
/// When the report that <em>placed</em> them was made — the recorded time of the very row
/// <paramref name="StationName"/> and <paramref name="DepthM"/> were read off.
/// <para>
/// <b>There are two times here because the position and the last word are routinely two
/// different reports.</b> The displayed position stays the latest report that actually claimed a
/// place, while <paramref name="LastRecordedAt"/> follows every report — so one note later, a
/// station heard four hours ago sits beside a timestamp eight minutes old. Anything that ages a
/// position, sorts the party by how fresh their places are, or tells a coordinator how stale a
/// station is must read this one; <paramref name="LastRecordedAt"/> answers only "when was
/// anything last said about this person".
/// </para>
/// <para>
/// Null on exactly the branch that nulls the station, so that nothing downstream can put an age
/// on a place it was refused. <b>Read that as consistency, not as confidentiality</b>, and do not
/// cite it as a protection: <paramref name="LastRecordedAt"/> beside it is unconditional, and
/// whenever the report that placed them is also the latest report — the ordinary case on a live
/// watch — it carries the very same instant, so this null keeps back nothing its sibling has not
/// already given. That is deliberate rather than an oversight. Where this project settles what a
/// tracking report discloses to a reader who may not learn positions, it drops the station, the
/// depth and the model as location vocabulary and keeps the recorded time, because "somebody was
/// heard from eight minutes ago" is exactly what such a reader is meant to keep. Null here
/// therefore means both "nothing has placed them" and "the place may not be told to this caller",
/// the same deliberate ambiguity the position fields carry — it does not additionally mean that
/// the hour is a secret.
/// </para>
/// </param>
/// <param name="In">
/// The last report that <em>stated</em> a standing put them inside the cave, or nothing has
/// stated one and a report has placed them inside it. False together with <paramref name="Out"/>
/// is the third state and a real answer — nobody has said yet that they went in, came out, or
/// were anywhere — and folding that into "not underground" would draw a party who have not set
/// off as one that is already back. Folded in Domain; never re-derive it from
/// <paramref name="LastKind"/>, which is what the two hand-written copies of this used to do.
/// </param>
/// <param name="Out">
/// The last report that stated a standing was that they are out. A later note, and a later report
/// of a place, both leave it standing: an exit ends the watch for a person, and only a recorded
/// entry starts it again.
/// </param>
public sealed record TrackingParticipantDto(
    Guid CaverId,
    Guid? TeamId,
    TripPositionEventKind? LastKind,
    DateTimeOffset? LastRecordedAt,
    DateTimeOffset? PositionRecordedAt,
    string? StationName,
    decimal? DepthM,
    /// <summary>
    /// The survey model the placing report was recorded against, or null where there is no
    /// position to speak of and on exactly the branch that withholds one.
    /// <para>
    /// <b>Carried because a station name alone does not say where somebody is.</b> A station path
    /// is a name inside one survey; the same path in a re-survey of the same cave may be a
    /// different place, or no place at all. A watch can be re-pointed at another model while the
    /// party is underground — a corrected survey mid-trip is a real thing a coordinator does — and
    /// every report already on the log keeps naming the model it was made against. Without this
    /// field the read hands over those names indistinguishable from names measured in the model
    /// now in use, and the surface that draws them puts the party on stations nobody reported.
    /// </para>
    /// <para>
    /// So whoever draws a marker compares this against the model they are drawing, and where the
    /// two differ says the position was recorded elsewhere rather than drawing it or, worse,
    /// leaving the person looking unreported. <see cref="StationName"/> itself is <em>not</em>
    /// blanked for that case: the name is a true record of a report and the log that shows the
    /// coordinator their own history goes on showing it. What changes is only what may be drawn.
    /// </para>
    /// </summary>
    Guid? PositionSurveyModelId,
    bool In,
    bool Out,
    /// <summary>
    /// The caption an administrator chose for this person on the trip's published page, or null
    /// where nobody chose one — which is the ordinary state.
    /// <para>
    /// Null does not say what the page will call them: that is the installation's setting, and it
    /// is answered once for the whole trip by <c>publishesRealNames</c> rather than repeated on
    /// every row. A caption is what the page shows whichever way that setting is set.
    /// </para>
    /// </summary>
    string? Label,
    /// <summary>
    /// The string a published page would print for this person right now, or null where it would
    /// print none and call them by their place in the party.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server's own answer rather than something a surface derives.</b> A caver whose real
    /// name is on a public page has to be able to find out, and the only honest way to tell them is
    /// to run the very rule the published page runs — the caption first, then the roster's own
    /// name where this installation publishes names, then nothing — against the very rows it reads.
    /// A second implementation on the client would be free to disagree with the page it is
    /// describing, and the one direction it must never be wrong in is saying "a place in the party"
    /// about somebody the page names.
    /// </para>
    /// <para>
    /// It is what the page <em>would</em> print, whether or not a link exists. Said unconditionally
    /// because the question is asked before anybody publishes: whoever is about to press the button
    /// has to read what it will disclose, and somebody already on a published page has to be able
    /// to read what it is disclosing. Whether a page exists is <c>publishedAt</c> on the trip.
    /// </para>
    /// <para>
    /// Deliberately the roster's name and not this account's display name, which is what every
    /// other signed-in surface calls the same person. The two part company the moment a member
    /// chooses a display name, and this field's whole job is to answer "what will a follow link
    /// print" — so it answers with the published string even where that differs from the one beside
    /// it on the screen.
    /// </para>
    /// <para>
    /// <b>That difference is not a widening, and the reason is worth stating rather than assuming.</b>
    /// A caver's roster name is readable by any signed-in caller by design — it is the label every
    /// attribution row needs, and the roster's own protection rule says so; what the shared resolver
    /// does with an account's chosen label is prefer it so that one person is not shown under two
    /// names on one page, which is a consistency rule and never a protection. So this field
    /// discloses nothing a signed-in caller could not already read, and it is sent to a narrower
    /// set than that: only to somebody who may read this trip.
    /// </para>
    /// </remarks>
    string? PublishedAs,
    /// <summary>
    /// Whether the trip still names this person. False for somebody who is listed here only
    /// because the watch's log holds reports about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The party is everybody the trip names <em>and</em> everybody its log speaks of. The two part
    /// company when somebody is taken off a finished trip's roster after having been reported on:
    /// their reports stay in the log, and a table that listed only the roster would count one
    /// person fewer than the log printed beneath it accounts for.
    /// </para>
    /// <para>
    /// <b>What false changes.</b> No report and no caption is taken for somebody the trip does not
    /// name, so a surface offers neither. <see cref="Label"/> and <see cref="PublishedAs"/> are null
    /// for them whatever is stored: a published page counts its party from the roster and shows
    /// this person no longer, so a field saying what it would call them would describe nothing.
    /// </para>
    /// </remarks>
    bool OnRoster,
    /// <summary>
    /// The person's name, sent only for somebody the trip no longer names
    /// (<see cref="OnRoster"/> false); null for everybody on the roster.
    /// </summary>
    /// <remarks>
    /// Everybody on the roster is named by the trip itself, which every reader of this watch has
    /// already read, so repeating those names here would be a second copy that could disagree with
    /// the first. Somebody off the roster is named by nothing the reader holds, and a row that
    /// could only say "somebody" beside a place in a cave is the least useful thing this read could
    /// send. It is the label every signed-in surface shows that person under, resolved by the same
    /// rule and for the same caller, so it tells nobody a name they could not already read.
    /// </remarks>
    string? Name,
    /// <summary>
    /// True when this person is underground by the log and nothing at all has been heard about
    /// them for longer than the installation's threshold, on a watch that is running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A mark for whoever is reading this, and nothing else.</b> It is worked out at the moment
    /// of the read against the server's own clock and stored nowhere. Nothing is sent, raised or
    /// stood down because of it; the trip's overdue callout neither reads it nor is read by it.
    /// </para>
    /// <para>
    /// Always false for somebody who is out, for somebody never heard from, on a watch that is not
    /// running, and on an installation that has switched the mark off. It says nothing of where
    /// anybody is — it is a reading of <see cref="LastRecordedAt"/>, which every reader of the
    /// trip is sent whether or not the place beside it is withheld — so it is answered the same
    /// for every reader.
    /// </para>
    /// </remarks>
    bool Quiet,
    /// <summary>
    /// True when the place this person was last reported at is known to lie outside the parts of
    /// the cave the watch declared — somewhere the party did not say it was going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declaration is the watch's own list of survey parts (<c>depthFilter</c> on the state),
    /// and the comparison is the one a reported depth is resolved by. False where nothing was
    /// declared, where the place was measured on a survey the watch has since left, and where the
    /// survey no longer holds that station: the mark says "known to be elsewhere" and is not
    /// raised on a guess.
    /// </para>
    /// <para>
    /// <b>Said only beside a place that is being told.</b> It is false whenever
    /// <see cref="StationName"/> is withheld, and whenever the declaration itself is — either
    /// would otherwise tell a reader one fact about station names they were refused. It is a word
    /// on a row for the people running the watch: nothing is sent or raised because of it, and no
    /// published page carries it.
    /// </para>
    /// </remarks>
    bool OutsideDeclaredParts,
    /// <summary>
    /// The number this person holds in the trip's party — the "Caver 3" a published page prints
    /// for somebody it does not name — or null for a person the trip never listed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Given once, when the trip first names somebody, and never changed: a change of job moves
    /// nobody, and a person taken off the trip leaves a gap rather than renumbering the people
    /// after them, so the numbers of a party need not run 1, 2, 3 without a break. Somebody since
    /// taken off the roster still carries the number they held, which they get back if the trip
    /// names them again.
    /// </para>
    /// <para>
    /// It identifies nobody outside this trip and is the same number every published read of the
    /// trip uses, which is what lets a coordinator on the telephone and a follower on the page
    /// mean the same person by it.
    /// </para>
    /// </remarks>
    int? Ordinal);

public sealed record TrackingStateDto(
    TripTrackingState State,
    Guid? SurveyModelId,
    /// <summary>
    /// True when the watch names a survey model this server no longer holds — somebody deleted it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A watch in that condition is the one this flag exists for: still <c>Armed</c>, still
    /// accepting notes and entries and exits, and unable to place anybody — the station control
    /// and the party on the model simply are not there, and nothing on the screen says why. Left
    /// to infer it, a surface has only a model id that resolves to nothing, which is also what a
    /// model still being read, a model this caller may not open, and a plain network failure look
    /// like. This is the server saying which of them it is.
    /// </para>
    /// <para>
    /// False whenever the configuration is being withheld from this caller: they are sent
    /// <see cref="SurveyModelId"/> as null, and "the model that watch is on was deleted" is a fact
    /// about a cave they may not be told about. Fail closed, as the rest of this read does.
    /// </para>
    /// </remarks>
    bool SurveyModelMissing,
    string? ReferenceStationName,
    IReadOnlyList<string> DepthFilter,
    /// <summary>
    /// When the watch was last started. A watch closed and started again carries the later moment
    /// here; <see cref="FirstArmedAt"/> keeps the earlier one.
    /// </summary>
    DateTimeOffset? ArmedAt,
    /// <summary>
    /// When the watch was started for the very first time, or null when it never has been — which
    /// includes a watch an import wrote already closed. Stamped once; starting a closed watch
    /// again does not move it.
    /// </summary>
    DateTimeOffset? FirstArmedAt,
    DateTimeOffset? ClosedAt,
    /// <summary>True when at least one position existed but was withheld from this caller.</summary>
    bool PositionsWithheld,
    /// <summary>
    /// Whether publishing this trip would put the party's real names on the page. This is the
    /// installation's setting, not a fact about this trip or this caller.
    /// <para>
    /// Carried on the trip's own read because the panel that mints a follow link is drawn on the
    /// same page, and whoever presses that button has to be told what the page will show
    /// <em>before</em> there is a link to hand out. What it discloses is one boolean about how this
    /// server is configured, to a caller who can already read the trip — no name, and nothing about
    /// any person. Somebody an administrator has named as a place in the party is still shown that
    /// way whatever this says.
    /// </para>
    /// </summary>
    bool PublishesRealNames,
    /// <summary>
    /// When this trip was first published by a link that still opens the page, or null when no link
    /// does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fact a member could not previously learn at all.</b> The list of follow links takes
    /// write access, so somebody who is merely on the trip could not see that one had been minted;
    /// the tracking state said nothing about it; and nothing told them. Their real name could be on
    /// a page on the internet, by the installation's default, with no surface anywhere admitting
    /// it. This is that surface, and it follows the trip's own readability and nothing else — a
    /// caller who may read the trip may know whether the trip is published.
    /// </para>
    /// <para>
    /// <b>It carries no token and no link count, and neither is an oversight.</b> A token is the
    /// whole of a follower's claim and exists in exactly one response; a page that showed one to
    /// every reader of the trip would be handing out the capability rather than reporting it. What
    /// a person needs to know is that a page exists, since when, and until when — not how to open
    /// it.
    /// </para>
    /// <para>
    /// Computed from the same rules the published read answers with, asked at this instant rather
    /// than stored: a link whose watch has closed or whose window has passed is not a publication
    /// any more, and reporting it as one would have this surface disagree with the page itself.
    /// <b>The cave's refusal is one of those rules, not a separate question</b> — a trip whose cave
    /// has since been position-protected, or whose watch has lost the cave it was anchored to, is
    /// refused to every follower while its links sit unrevoked and inside their window, so a
    /// reading that consulted the window alone would report a publication that opens nothing.
    /// </para>
    /// </remarks>
    DateTimeOffset? PublishedAt,
    /// <summary>
    /// When the last still-open link lapses, or null when none is open.
    /// </summary>
    /// <remarks>
    /// The other half of the answer, and the one that makes the first half actionable: "this trip
    /// is published" is a different thing to be told depending on whether it stops on Thursday or
    /// in two weeks. The latest of the open links, because that is when the trip actually stops
    /// being published — an earlier one lapsing changes nothing while another is live. It is an
    /// outer bound rather than a promise: closing the watch ends the publication sooner, and
    /// revoking ends it at once.
    /// </remarks>
    DateTimeOffset? PublishedUntil,
    IReadOnlyList<TrackingTeamDto> Teams,
    IReadOnlyList<TrackingParticipantDto> Participants,
    /// <summary>
    /// After how many seconds without a report somebody underground is marked as not heard from,
    /// or null when nobody can be: the watch is not running, or the installation has switched the
    /// mark off.
    /// </summary>
    /// <remarks>
    /// Sent so that a surface can say what its mark means ("no word for over 3 h") in the
    /// installation's own number rather than a guessed one, and can leave the whole subject out
    /// where it does not apply. Which people are marked is <c>quiet</c> on each of them, decided
    /// here; a surface does not work it out again from this number and its own clock.
    /// </remarks>
    int? QuietAfterSeconds);

/// <summary>One report of the log, or the answer to recording or correcting one.</summary>
/// <param name="DepthPlacement">
/// How a reported depth became the station it names — what the cave declared, or the nearest
/// station measured — on the answer to a record or a correction. Null where the report named no
/// depth, and on a read of the log, which does not keep how a station was arrived at. It is there
/// because a declaration whose station the model lacks is passed over and measured instead, and a
/// person who picked a declared place would otherwise never learn that the log holds something else.
/// </param>
/// <param name="Corrected">
/// Whether the report has been changed since it was first written down — corrected in place,
/// replaced by a sheet, or moved to another roster entry. A yes or a no: when and by whom is not
/// said here. It is on the signed-in log only; nothing a visitor without an account reads carries
/// it. It says nothing of where anybody was, so it is answered the same whether or not the place
/// beside it is withheld.
/// </param>
/// <param name="OutsideDeclaredParts">
/// Whether this report's station is known to lie outside the parts of the cave the watch declared.
/// False where nothing is declared, where the report was measured on a survey the watch has since
/// left, and wherever the place or the declaration is withheld from the caller — it rides the
/// place's own branch. On the sheet import's "what is there now" it is not worked out and reads
/// false. Signed-in log only.
/// </param>
public sealed record TrackingEventDto(
    Guid Id,
    Guid CaverId,
    Guid? TeamId,
    TripPositionEventKind Kind,
    Guid? SurveyModelId,
    string? StationName,
    decimal? DepthEnteredM,
    string? Note,
    DateTimeOffset RecordedAt,
    bool Corrected,
    bool OutsideDeclaredParts,
    TrackingDepthPlacementOutcome? DepthPlacement = null);

/// <summary>
/// A report that was taken off the log and is still kept: what it said, read under the same
/// withholding as the log, and when it was taken off.
/// </summary>
/// <remarks>
/// Who took it off is on the trip's history with every other act on the log, and is not repeated
/// here. Listed only for those who may write the log; nothing a visitor without an account reads
/// carries a removed report in any form.
/// </remarks>
public sealed record TrackingRemovedEventDto(TrackingEventDto Report, DateTimeOffset RemovedAt);


/// <summary>
/// One place the watch's cave has declared: what it is called, which station it is, how deep.
/// </summary>
/// <remarks>
/// Its own shape rather than the cave page's, because these two surfaces are not the same list
/// seen twice: the cave page edits declarations and needs each row's identity to change or
/// withdraw it, while a report names a place and needs only what the place is. Sending an id here
/// would be handing a surface a handle it has no business using.
/// </remarks>
/// <param name="StationInModel">
/// Whether the station this declaration names is one the watch's model has. A declaration written
/// against a survey that has since been replaced can name a station nothing resolves; a report at
/// its depth is then measured instead, and honouring it would place a marker nobody ever sees. The
/// place is still listed, so that whoever offers it can say it needs fixing on the cave rather than
/// quietly offering a place that records somewhere else.
/// </param>
public sealed record TrackingPlaceDto(
    decimal DepthM,
    string StationName,
    string? PlaceLabel,
    bool StationInModel);

/// <summary>One station a depth could mean, with how far off it is.</summary>
/// <param name="Declared">
/// True for the station the cave declared this depth to be, which recording the depth will write
/// down whatever the measurement says; it is listed first. False for a station found by measuring.
/// </param>
public sealed record TrackingDepthCandidateDto(
    string StationName,
    string? SurveyName,
    double DepthM,
    double DeltaM,
    bool Declared);

// ---- writes ----

/// <summary>
/// Config write, merge semantics: an absent field keeps what is stored, so closing tracking
/// cannot silently rewrite the configuration earlier reports were resolved under. Clearing
/// is explicit — an empty string clears the reference station, an empty list the filter.
/// State is always stated.
/// </summary>
public sealed record TrackingConfigRequest(
    TripTrackingState? State,
    Guid? SurveyModelId,
    string? ReferenceStationName,
    IReadOnlyList<string>? DepthFilter);

public sealed class TrackingConfigRequestValidator : AbstractValidator<TrackingConfigRequest>
{
    public TrackingConfigRequestValidator()
    {
        // Nullable so an absent field cannot silently mean the enum's zero value.
        RuleFor(x => x.State).NotNull().IsInEnum();
        RuleFor(x => x.ReferenceStationName).MaximumLength(TripTrackingRules.MaxStationNameLength);
        RuleFor(x => x.DepthFilter!.Count).LessThanOrEqualTo(TripTrackingRules.MaxDepthFilterEntries)
            .When(x => x.DepthFilter is not null);
        RuleForEach(x => x.DepthFilter).NotEmpty().MaximumLength(TripTrackingRules.MaxStationNameLength)
            .When(x => x.DepthFilter is not null);
    }
}

/// <summary>
/// The name a published page gives one participant. An absent, empty or blank label clears the
/// choice and returns them to the non-identifying default — there is no separate route for
/// that, because "call them nothing in particular" is a value this field can hold.
/// </summary>
public sealed record TrackingParticipantLabelRequest(string? Label);

public sealed class TrackingParticipantLabelRequestValidator : AbstractValidator<TrackingParticipantLabelRequest>
{
    public TrackingParticipantLabelRequestValidator()
    {
        RuleFor(x => x.Label).MaximumLength(TripTrackingRules.MaxLabelLength);
    }
}

/// <summary>The label as stored after the write; null when the choice was cleared.</summary>
public sealed record TrackingParticipantLabelDto(Guid CaverId, string? Label);

public sealed record TrackingTeamRequest(string? Title);

public sealed class TrackingTeamRequestValidator : AbstractValidator<TrackingTeamRequest>
{
    public TrackingTeamRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TripTrackingRules.MaxTitleLength);
    }
}

/// <summary>
/// What every report says about a place and a moment, whether it is being written for the first
/// time or corrected.
/// </summary>
/// <remarks>
/// Declared as a shape both requests carry so the rules over it have one home. The two differ in
/// exactly one thing — a new report names who it is about and a correction cannot, a report about
/// somebody else being a different report — and everything else they say is the same, so a second
/// copy of "a station name belongs to a station report" is a second chance for the two surfaces to
/// disagree about what a valid report is.
/// </remarks>
public interface ITrackingReportFields
{
    TripPositionEventKind? Kind { get; }

    string? StationName { get; }

    decimal? DepthM { get; }

    Guid? TeamId { get; }

    string? Note { get; }

    DateTimeOffset? RecordedAt { get; }
}

/// <summary>
/// One act of reporting: who it is about, what it says, and — optionally — the key of the act.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key is what lets a send be repeated safely.</b> A sender that never heard the answer
/// cannot know whether its report was written; it mints <paramref name="ClientKey"/> once for the
/// act and sends the same value again. The first send to arrive is written; every later one under
/// the same key is answered with the reports of that act still on the log and writes nothing —
/// success, not a refusal, because from where the sender stands the report it wanted is there.
/// </para>
/// <para>
/// The key names the act, not its content: what a later send says is not compared with what the
/// first one wrote, and two acts with the same content under different keys are two reports.
/// Without a key every send is an act of its own, as it has always been. The key is taken in and
/// never given out — no read of a report carries it.
/// </para>
/// </remarks>
public sealed record TrackingEventRequest(
    IReadOnlyList<Guid>? CaverIds,
    TripPositionEventKind? Kind,
    string? StationName,
    decimal? DepthM,
    Guid? TeamId,
    string? Note,
    DateTimeOffset? RecordedAt,
    Guid? ClientKey) : ITrackingReportFields;

/// <summary>
/// A correction to one report already on the log.
/// </summary>
/// <remarks>
/// <b>It names no caver, deliberately.</b> A report about a different person is a different report:
/// what somebody means by changing its subject is that this one should not exist and another should,
/// which is a deletion and a new report rather than an edit. Carrying a caver here would be a field
/// the route is obliged to ignore, and a request shape that demands what it discards is a contract
/// nobody can read.
/// </remarks>
public sealed record TrackingEventEditRequest(
    TripPositionEventKind? Kind,
    string? StationName,
    decimal? DepthM,
    Guid? TeamId,
    string? Note,
    DateTimeOffset? RecordedAt) : ITrackingReportFields;

/// <summary>
/// The rules over what a report says, applied to whichever request is carrying it.
/// </summary>
/// <remarks>
/// A static over the validator rather than a validator of its own, because FluentValidation's own
/// composition works within one type and these are two: what is shared is the rules, not a shape
/// either request could be converted to.
/// </remarks>
internal static class TrackingReportFieldRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : ITrackingReportFields
    {
        validator.RuleFor(x => x.Kind).NotNull().IsInEnum();
        validator.RuleFor(x => x.Note).MaximumLength(TripTrackingRules.MaxNoteLength);

        // A station name belongs to a station report and a depth to a depth report — a request
        // carrying the wrong one is a confused caller, not a permissive default.
        validator.RuleFor(x => x.StationName).NotEmpty().MaximumLength(TripTrackingRules.MaxStationNameLength)
            .When(x => x.Kind == TripPositionEventKind.AtStation);
        validator.RuleFor(x => x.StationName).Null()
            .When(x => x.Kind is not null && x.Kind != TripPositionEventKind.AtStation);
        validator.RuleFor(x => x.DepthM).NotNull()
            .When(x => x.Kind == TripPositionEventKind.AtDepth);
        validator.RuleFor(x => x.DepthM!.Value).InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);
        validator.RuleFor(x => x.DepthM).Null()
            .When(x => x.Kind is not null && x.Kind != TripPositionEventKind.AtDepth);
    }
}

public sealed class TrackingEventEditRequestValidator : AbstractValidator<TrackingEventEditRequest>
{
    public TrackingEventEditRequestValidator() => TrackingReportFieldRules.Apply(this);
}

public sealed class TrackingEventRequestValidator : AbstractValidator<TrackingEventRequest>
{
    public TrackingEventRequestValidator()
    {
        // Its own, and the only thing a correction does not say: who the report is about.
        RuleFor(x => x.CaverIds).NotEmpty();
        RuleFor(x => x.CaverIds!.Count).LessThanOrEqualTo(TripTrackingRules.MaxCaversPerWrite)
            .When(x => x.CaverIds is not null);
        // No key is the ordinary send. The all-zero key is what a sender that forgot to mint one
        // serialises, and every such sender would share it: taken as a key it would answer one
        // person's report with another's and write nothing, so it is refused instead.
        RuleFor(x => x.ClientKey).NotEqual((Guid?)Guid.Empty)
            .WithMessage("The key of a report cannot be the empty key; leave it out or send a fresh one.");

        TrackingReportFieldRules.Apply(this);
    }
}

public sealed record TrackingResolveDepthRequest(decimal? DepthM, int? Take);

public sealed class TrackingResolveDepthRequestValidator : AbstractValidator<TrackingResolveDepthRequest>
{
    public TrackingResolveDepthRequestValidator()
    {
        RuleFor(x => x.DepthM).NotNull();
        RuleFor(x => x.DepthM!.Value).InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);
        RuleFor(x => x.Take!.Value).InclusiveBetween(1, 25).When(x => x.Take is not null);
    }
}
