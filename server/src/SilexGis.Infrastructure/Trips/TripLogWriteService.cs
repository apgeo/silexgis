// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// The one place a trip log is created or edited: audience defaulting, the create decision,
/// reference checks, the field copy, section validation, cave-link reconciliation, roster
/// reconciliation and the decision to tell the people newly named.
/// </summary>
/// <remarks>
/// <para>
/// It exists so that loading trips in bulk is a bulk create rather than a second creation path.
/// Everything a trip write has to get right — a cave the caller was never shown put back rather
/// than dropped, a roster diffed rather than recreated, a section measured against the schema its
/// purpose carries — is a rule that has to hold for a spreadsheet of a club's old trips exactly
/// as it holds for a form somebody filled in, and a rule stated twice is a rule that will
/// eventually be stated differently.
/// </para>
/// <para>
/// What stays with the caller: how a request arrived and how a refusal is rendered, the
/// concurrency check, the response projection — and the save. Nothing here calls
/// <c>SaveChangesAsync</c>, because the trip row, its links, the roster rows it invents and the
/// messages it queues are one unit of work and the caller decides where that unit ends. A caller
/// writing many trips may commit them together; a request writing one commits at the end of it.
/// </para>
/// </remarks>
public sealed class TripLogWriteService(
    SilexGisDbContext db,
    FeatureProtection protection,
    TripSectionWriter sections,
    ITripRosterAnnouncer announcer)
{
    /// <summary>
    /// The role a bare list of caves is written under. The list says the trip is about those
    /// caves and nothing finer, so it is recorded as the plainest of the roles that carries
    /// that meaning; a trip that did something more particular there says so through the role
    /// it was recorded under, and this path never overwrites that.
    /// </summary>
    private const string CaveListRole = "trip-visited";

    /// <summary>
    /// Creates a trip, whichever door it came through. The two doors differ in one thing and it
    /// is decided here, before anything is checked against it: the audience a request that names
    /// none falls back to. Everything after that point is identical.
    /// </summary>
    /// <remarks>
    /// The trip is added to the change tracker and returned unsaved; its identity is set on
    /// construction, so the links and roster rows written against it are already correct.
    /// </remarks>
    /// <exception cref="TripWriteException">
    /// The caller may not create a trip in the audience that resulted, may not bind to the group
    /// named, or the request names something that does not exist or does not fit its section.
    /// </exception>
    public async Task<TripLog> CreateAsync(
        TripCreationIntent intent,
        TripWriteInput input,
        AccessContext ctx,
        Guid ownerUserId,
        TripWriteNotice notice = TripWriteNotice.Announce,
        CancellationToken ct = default)
    {
        // Who may read the trip is one answer in two values, so the default decides it only when
        // the request answers neither of them. A request that names either half has taken the
        // decision itself and both halves are read as it sent them — a stated audience with no
        // group binding is somebody saying "not the club", and quietly supplying one would widen
        // what they asked for. A request that names only a group is still naming a group: the
        // audience falls back, but dropping the binding would take the row out of every rule
        // written about that group's content, a refusal aimed at the group among them.
        //
        // Settled before the create check and the reference checks below, so a binding this rule
        // supplies is guarded exactly like one the caller typed rather than slipping in behind
        // them.
        var fallback = TripAudienceRules.DefaultAudience(intent, ctx.CavingGroupIds);
        var (visibility, cavingGroupId) = input.Visibility is null && input.CavingGroupId is null
            ? fallback
            : (input.Visibility ?? fallback.Visibility, input.CavingGroupId);

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, cavingGroupId))
        {
            throw new TripWriteException(
                CreateRules.ForbiddenCode, "That trip may not be created.", denied: true);
        }

        await ValidateReferencesAsync(ctx, input with { CavingGroupId = cavingGroupId }, ct);

        var trip = new TripLog { Title = input.Title, OwnerUserId = ownerUserId };
        Apply(trip, input);
        trip.Visibility = visibility;
        trip.CavingGroupId = cavingGroupId;
        // A new row has nothing stored, so every section is a first write and is measured
        // against the purpose's schemas as they stand.
        await sections.ApplyAsync(trip, input.Sections, typeChanged: true, ct);

        db.TripLogs.Add(trip);
        // No existing children on create, so the reconcile helpers reduce to pure inserts.
        if (input.CaveIds is { } caveIds)
        {
            await ReconcileCaveLinksAsync(ctx, trip.Id, caveIds, ct);
        }

        var added = await ReconcileRosterAsync(trip.Id, input, ct);
        await AnnounceAsync(trip, added, notice, ct);
        return trip;
    }

    /// <summary>
    /// Brings a trip already loaded and already decided upon in line with what was asked for.
    /// The trip must be tracked; nothing here decides whether this caller may edit it, which is
    /// a question about the trip as it stands and is answered before it is loaded.
    /// </summary>
    /// <exception cref="TripWriteException">
    /// The caller may not bind to the group named, or the request names something that does not
    /// exist or does not fit its section.
    /// </exception>
    public async Task<TripWriteOutcome> UpdateAsync(
        TripLog trip,
        TripWriteInput input,
        AccessContext ctx,
        TripWriteNotice notice = TripWriteNotice.Announce,
        CancellationToken ct = default)
    {
        await ValidateReferencesAsync(ctx, input, ct);

        // Read before Apply overwrites it: moving a trip to another purpose re-measures all
        // three sections, because the schemas they answer to are not the ones they were
        // measured against any more.
        var typeChanged = trip.TripTypeId != input.TripTypeId;
        Apply(trip, input);
        await sections.ApplyAsync(trip, input.Sections, typeChanged, ct);

        var addedCaves = input.CaveIds is { } caveIds
            ? await ReconcileCaveLinksAsync(ctx, trip.Id, caveIds, ct)
            : [];

        var added = await ReconcileRosterAsync(trip.Id, input, ct);

        // Asked before the first message is queued, because queueing one is itself a change and
        // would answer this question for it. A request that carries back exactly what it was
        // given — a form saved without an edit, a version restored onto the version already
        // loaded — moves no column, and a notice announcing a change that provably did not happen
        // is how a category earns being muted along with the messages that matter.
        var somethingChanged = db.ChangeTracker.HasChanges();

        await AnnounceAsync(trip, added, notice, ct);
        return new TripWriteOutcome(added, addedCaves, somethingChanged);
    }

    /// <summary>
    /// The two roles a trip names by itself, wherever it writes its list of people: everyone a
    /// trip records was either simply there or put it forward, and both are shipped rows
    /// precisely so this can rely on them existing. Missing means the vocabulary was never
    /// seeded, and a write that stored nobody while answering 200 is worse than one that fails.
    /// </summary>
    public static async Task<(long Participant, long Proposer)> ShippedRosterRolesAsync(
        SilexGisDbContext db, CancellationToken ct)
    {
        var ids = await db.TripParticipantRoles.AsNoTracking()
            .Where(r => r.Code == TripParticipantRoleSeeds.ParticipantCode
                || r.Code == TripParticipantRoleSeeds.ProposerCode)
            .ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        if (!ids.TryGetValue(TripParticipantRoleSeeds.ParticipantCode, out var participant)
            || !ids.TryGetValue(TripParticipantRoleSeeds.ProposerCode, out var proposer))
        {
            throw new InvalidOperationException("The shipped participant roles are not seeded.");
        }

        return (participant, proposer);
    }

    private Task AnnounceAsync(
        TripLog trip, IReadOnlyList<Guid> added, TripWriteNotice notice, CancellationToken ct) =>
        notice is TripWriteNotice.Silent
            ? Task.CompletedTask
            : announcer.AnnounceAsync(trip, added, ct);

    private static void Apply(TripLog trip, TripWriteInput input)
    {
        trip.Title = input.Title;
        trip.TripTypeId = input.TripTypeId;
        trip.TripDate = input.TripDate;
        trip.TripDateEnd = input.TripDateEnd;
        trip.EntryTime = input.EntryTime;
        trip.ExitTime = input.ExitTime;
        trip.Description = input.Description;
        trip.Results = input.Results;
        trip.WeatherConditions = input.WeatherConditions;
        trip.LocationText = input.LocationText;
        trip.DepthReachedM = input.DepthReachedM;
        trip.LengthSurveyedM = input.LengthSurveyedM;
        trip.SurveyStations = input.SurveyStations;
        trip.RopeMetres = input.RopeMetres;
        trip.HadIncident = input.HadIncident;
        // Written straight through, and null clears it: a trip with no stated limit is the ordinary
        // case, so there is nothing else an absent number could be asking for. It is never checked
        // against how many people have said they are coming — lowering a limit below the answers
        // already given moves people to waiting, which is what a limit is for, and refusing the
        // edit would leave whoever runs the trip unable to say how many places there really are.
        trip.MaxParticipants = input.MaxParticipants;
        trip.OrganizingCavingGroupId = input.OrganizingCavingGroupId;
        trip.Geom = input.Geom;
        trip.MeetingGeom = input.MeetingGeom;
        // An audience the request does not name is left exactly as it stands. The only place a
        // trip's audience is decided for it is the moment it is created, and it is decided there
        // before this runs — so a null arriving here can only mean "not editing who may read it",
        // and a save from a surface that never drew the field cannot quietly narrow or widen one.
        //
        // The group binding moves with it rather than on its own, because the two are one answer:
        // a group-visible trip whose binding is cleared names no group and is therefore readable
        // by nobody but its owner. Writing the binding unconditionally would do exactly that to
        // every save from a surface that drew neither field — the case the nullability above
        // exists to protect — and it would do it silently, with the stored audience still
        // reading "the caving group".
        if (input.Visibility is { } visibility)
        {
            trip.Visibility = visibility;
            trip.CavingGroupId = input.CavingGroupId;
        }
    }

    // Reconcile with a diff (add/remove only what changed) rather than delete-all +
    // recreate-all: a full recreate logs a "created" event for every unchanged child on every
    // save, so the diff keeps the timeline honest. Also preserves caves the caller was never
    // shown, for either of the two reasons a cave is kept off the list they edited: treating a
    // list handed over short as the whole truth would silently drop them.
    //
    // That last guard is why a list is only reconciled when one is actually supplied. Naming a
    // cave one at a time — which is how it is done now — cannot express "forget everything not
    // in this list", so the hazard simply does not arise there; it arises only here, where an
    // absence has to be read as an instruction, and here it is guarded.
    //
    // Reads over every role, writes under one, and that asymmetry is chosen rather than
    // tolerated. A cave the trip already names — whatever it did there — is left exactly as it is
    // rather than named a second time, and a cave dropped from the list is unnamed only from the
    // role this path writes: a list with no roles in it is not an instruction to forget that the
    // trip surveyed somewhere, and one coarse list must not be able to erase a finer statement
    // somebody made on purpose elsewhere. The consequence, accepted with the rule: dropping a
    // cave the trip holds only under some other role does nothing. Nothing is hidden by that —
    // this write answers with the trip read afresh, whose list still names that cave — and the
    // way to take such a cave off a trip is through the role that put it there.
    /// <returns>
    /// The caves this write newly named on the trip. A cave put back because the caller was never
    /// shown it is not among them: nothing about it changed, and it is the caves that arrive that
    /// somebody asked on the trip may turn out not to be able to open.
    /// </returns>
    private async Task<List<Guid>> ReconcileCaveLinksAsync(
        AccessContext ctx, Guid tripId, IReadOnlyList<Guid> requestedCaveIds, CancellationToken ct)
    {
        // Read through exactly the narrowing the caller was answered through, caves only. A role
        // names any linkable target, and a spring or a shaft named under one of them can never
        // appear in a list of caves — so a view any wider here would read those as absences and
        // unname them, on a request that never mentioned them. What the caller could not have
        // been shown, they cannot be taken to have dropped.
        var named = (await TripRoleLinks.PairsForAsync(db, [tripId], FeatureKind.Cave, ct))
            .Select(pair => pair.FeatureId)
            .Distinct()
            .ToList();

        // Put back everything this caller was never shown, decided by the very function that
        // decided what to show them. The two have to agree exactly: whatever the read takes out,
        // the write puts back. A narrower re-add is not a smaller safeguard — it is a silent
        // deletion, because the caller omits a cave they were never offered and the trip loses a
        // link nobody asked to drop. That is why this asks the shared rule rather than the
        // position rule alone: the position rule reads its rows past every visibility filter and
        // answers only about where a cave is, so a cave held back for being unreadable is not
        // among the ones it names.
        var disclosable = await TripCaveDisclosure.DisclosableCaveIdsAsync(db, protection, ctx, named, ct);
        var desired = requestedCaveIds.Concat(named.Where(id => !disclosable.Contains(id))).ToHashSet();

        foreach (var caveId in named.Where(id => !desired.Contains(id)))
        {
            await TripRoleLinks.UnnameFeatureAsync(db, tripId, caveId, CaveListRole, ct);
        }

        var addedCaveIds = new List<Guid>();
        foreach (var caveId in desired.Where(id => !named.Contains(id)))
        {
            // A role code that is not in the vocabulary means the installation's link types were
            // never seeded — the naming would silently record nothing, and answering 200 to a
            // write that stored nothing is worse than failing.
            if (!await TripRoleLinks.NameFeatureAsync(db, tripId, caveId, CaveListRole, ctx.UserId, ct))
            {
                throw new InvalidOperationException($"Relation type '{CaveListRole}' is not seeded.");
            }

            addedCaveIds.Add(caveId);
        }

        return addedCaveIds;
    }

    /// <summary>
    /// Brings a trip's whole roster in line with what was asked for, and reports the registered
    /// users genuinely newly listed — the only point at which that is knowable, since afterwards
    /// an added row is indistinguishable from one that was already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The roster is reconciled in one pass over every role, because the two lists together are
    /// the whole of it: a row in a job neither list mentions has been withdrawn, and leaving it
    /// standing would make a job impossible to take away once given. What that costs is stated on
    /// the request itself — a surface showing people must send back the rows it did not show.
    /// </para>
    /// <para>
    /// A row already there in the same job is kept and brought up to date rather than replaced,
    /// so an unchanged roster writes nothing: the change tracker sees equal values and records no
    /// history, which is what keeps re-saving a trip out of its timeline.
    /// </para>
    /// <para>
    /// <b>Newly listed is asked of the trip, not of the job.</b> Somebody already named on the
    /// trip who is now also its surveyor learns nothing from being told they are on a trip they
    /// are already on, so the second row tells nobody. Only a person the trip did not name at all
    /// before this write is new to it.
    /// </para>
    /// </remarks>
    private async Task<List<Guid>> ReconcileRosterAsync(
        Guid tripId, TripWriteInput input, CancellationToken ct)
    {
        var roles = await ShippedRosterRolesAsync(db, ct);

        // Each list says what its entries are for; an entry naming its own role overrides that,
        // which is how a job beyond the two the lists are named after gets recorded at all.
        var requested = new List<(long RoleId, TripRosterEntry Write)>();
        requested.AddRange(input.Participants.Select(p => (p.RoleId ?? roles.Participant, p)));
        requested.AddRange((input.Proposers ?? []).Select(p => (p.RoleId ?? roles.Proposer, p)));

        // A name with no roster entry becomes one, so the person can be counted and found again
        // on later trips. Repeating a name already in the roster reuses it rather than making a
        // second entry for the same person.
        var named = requested
            .Where(p => p.Write.CaverId is null)
            .Select(p => p.Write.NewCaverName!.Trim())
            .Where(name => name.Length > 0)
            .ToList();

        // Two people can share a name — that is exactly the state the roster merge exists to
        // resolve — so the lookup groups before it keys. Keying the query straight by name would
        // fault on the duplicate and lose the whole trip write over a coincidence of spelling.
        // The oldest entry wins, so the same typed name resolves to the same person every time
        // rather than to whichever row the database happened to return first.
        var matchedRows = named.Count == 0
            ? []
            : await db.Cavers.Where(c => named.Contains(c.FullName))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .ToListAsync(ct);
        var matched = matchedRows
            .GroupBy(c => c.FullName)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Keyed on the pair the roster is unique on, so one person in two jobs is two entries and
        // the same person named twice for one job is one — the last of them, since a request that
        // says a thing twice means it once.
        var desired = new Dictionary<(long RoleId, Guid CaverId), TripRosterEntry>();
        foreach (var (roleId, write) in requested)
        {
            var caverId = write.CaverId;
            if (caverId is null)
            {
                var name = write.NewCaverName!.Trim();
                if (!matched.TryGetValue(name, out var existingId))
                {
                    var created = new Caver { FullName = name };
                    db.Cavers.Add(created);
                    matched[name] = created.Id;
                    existingId = created.Id;
                }

                caverId = existingId;
            }

            desired[(roleId, caverId.Value)] = write;
        }

        var existing = await db.TripLogParticipants.Where(x => x.TripLogId == tripId).ToListAsync(ct);

        // Read before the loop below empties `desired`, and before any row is removed: this is
        // the trip's roster as it stood when the request arrived, which is the only thing that
        // can answer whether a person is new to the trip.
        var alreadyNamed = existing.Select(x => x.CaverId).ToHashSet();

        foreach (var participant in existing)
        {
            if (desired.Remove((participant.RoleId, participant.CaverId), out var write))
            {
                participant.EntryTime = write.EntryTime;
                participant.ExitTime = write.ExitTime;
                participant.Note = Trimmed(write.Note);
                continue;
            }

            db.TripLogParticipants.Remove(participant);
        }

        foreach (var (key, write) in desired)
        {
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = tripId,
                RoleId = key.RoleId,
                CaverId = key.CaverId,
                EntryTime = write.EntryTime,
                ExitTime = write.ExitTime,
                Note = Trimmed(write.Note),
            });
        }

        var newcomers = desired.Keys
            .Select(key => key.CaverId)
            .Where(caverId => !alreadyNamed.Contains(caverId))
            .Distinct()
            .ToList();

        // Only the newly listed people who hold an account: there is nobody to tell for the rest.
        return await db.Cavers
            .Where(c => newcomers.Contains(c.Id) && c.UserId != null)
            .Select(c => c.UserId!.Value)
            .ToListAsync(ct);
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Geometry validity, trip-purpose and cave existence, participant-user existence.</summary>
    private async Task ValidateReferencesAsync(AccessContext ctx, TripWriteInput input, CancellationToken ct)
    {
        // Both shapes on the request answer to one code and one sentence: a caller that sent a
        // malformed shape learns the same thing about either, and which field it was is in the
        // request they sent.
        if (input.GeometryMalformed)
        {
            throw new TripWriteException("trip_log.geometry_invalid", "Geometry is malformed or invalid.");
        }

        // The purpose vocabulary is a row set an installation extends, so an unknown identity is
        // a plain bad request rather than a shape the request validator could have caught. The
        // vocabulary is readable by every account, so naming a row that does not exist discloses
        // nothing that reading the list would not.
        if (input.TripTypeId is { } tripTypeId
            && !await db.TripTypes.AnyAsync(t => t.Id == tripTypeId, ct))
        {
            throw new TripWriteException("trip_log.type_unknown", "That trip type does not exist.");
        }

        if (input.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.TripLogs, input.CavingGroupId.Value))
        {
            throw new TripWriteException(
                CavingGroupBindingRules.ForbiddenCode,
                "That trip may not be bound to that caving group.",
                denied: true);
        }

        // A cave is a feature row, so existence and readability are one filtered count; an id
        // the caller cannot read is reported exactly like a nonexistent one, so linking cannot
        // be used to probe for caves. Nobody is ever forced to send one: the list a caller was
        // handed holds only caves they may be told about, and the ones kept off it are put back
        // by the reconcile rather than expected back from them — so refusing an unreadable id
        // here cannot turn into a save that fails over a cave the caller never saw.
        var caveIds = (input.CaveIds ?? []).Distinct().ToList();
        if (caveIds.Count > 0)
        {
            var readable = await db.Features.AsNoTracking()
                .Where(f => f.Kind == FeatureKind.Cave && caveIds.Contains(f.Id))
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .CountAsync(ct);
            if (readable != caveIds.Count)
            {
                throw new TripWriteException("trip_log.cave_not_found", "A linked cave does not exist.");
            }
        }

        var roster = input.Participants.Concat(input.Proposers ?? []).ToList();
        var caverIds = roster
            .Where(x => x.CaverId is not null).Select(x => x.CaverId!.Value).Distinct().ToList();
        if (caverIds.Count > 0)
        {
            var found = await db.Cavers.Where(c => caverIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            if (found.Count != caverIds.Count)
            {
                throw new TripWriteException(
                    "trip_log.participant_unknown", "A participant user does not exist.");
            }
        }

        // The role vocabulary is a row set an installation extends, so an unknown identity is a
        // plain bad request for the same reason the purpose is — and the restricting foreign key
        // would otherwise refuse it far below anything that could turn it into an answer.
        var roleIds = roster.Where(x => x.RoleId is not null).Select(x => x.RoleId!.Value).Distinct().ToList();
        if (roleIds.Count > 0)
        {
            var known = await db.TripParticipantRoles.AsNoTracking()
                .CountAsync(r => roleIds.Contains(r.Id), ct);
            if (known != roleIds.Count)
            {
                throw new TripWriteException(
                    "trip_log.participant_role_unknown", "A participant role does not exist.");
            }
        }
    }
}
