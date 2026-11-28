// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;
using SilexGis.Domain.TestData;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.TestData;

/// <summary>The trip vocabulary a run writes against, by code, its own additions included.</summary>
public sealed record TestDataActivityVocabulary(
    IReadOnlyDictionary<string, long> TripTypes,
    IReadOnlyDictionary<string, long> ParticipantRoles,
    IReadOnlyDictionary<string, long> RosterRoles,
    Guid ChecklistId,
    IReadOnlyList<Guid> ChecklistItems);

/// <summary>
/// Writes what the run's people did: trips through the trip writer (so rosters, cave links and
/// party numbers are kept exactly as a person's trip keeps them), the invitations, teams and
/// checklist ticks on them, summer camps with their rosters and trips, and the clubs' events.
/// Nothing is announced: the generated people would be told of trips they never asked to join.
/// </summary>
public static class TestDataActivityWriter
{
    public const int TripChunk = 100;

    public const string ChecklistTitle = "Echipament de cartare (generat)";

    public const string AddedParticipantRole = "testgen_rigger";

    public const string AddedRosterRole = "testgen_scientist";

    private const string FieldDataSchema =
        """
        {"type":"object","properties":{
          "length_m":{"type":"number","title":"Lungime cartată (m)","minimum":0},
          "stations":{"type":"integer","title":"Stații","minimum":0},
          "method":{"type":"string","title":"Metodă","enum":["DistoX","busolă și clinometru","stație totală"]}
        }}
        """;

    private static readonly string[] ChecklistItems =
    [
        "DistoX încărcat și calibrat", "Carnet de teren și creioane", "Ruletă de rezervă", "Lampă de rezervă",
        "Baterii pentru toate aparatele", "Hartă cu stațiile cunoscute",
    ];

    /// <summary>
    /// The run's own trip type (a mapping trip whose field data has a schema), its checklist, and an
    /// added role on trips and on camps. The trip type writer keeps a transaction of its own, so
    /// this runs in a scope that has none.
    /// </summary>
    public static async Task<TestDataActivityVocabulary> EnsureVocabularyAsync(
        SilexGisDbContext db, TripTypeWriteService tripTypes, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(tripTypes);
        ArgumentNullException.ThrowIfNull(session);
        var checklist = await db.Checklists.FirstOrDefaultAsync(c => c.Title == ChecklistTitle && c.OwnerUserId == session.OwnerUserId, ct);
        if (checklist is null)
        {
            checklist = new Checklist
            {
                Title = ChecklistTitle,
                Description = "Ce verifică echipa de cartare înainte să plece; listă adăugată de generatorul de date de test.",
                OwnerUserId = session.OwnerUserId,
                Visibility = Visibility.Authenticated,
            };
            db.Checklists.Add(checklist);
            for (var i = 0; i < ChecklistItems.Length; i++)
            {
                db.ChecklistItems.Add(new ChecklistItem { ChecklistId = checklist.Id, Text = ChecklistItems[i], SortOrder = i });
            }
        }

        if (!await db.TripParticipantRoles.AnyAsync(r => r.Code == AddedParticipantRole, ct))
        {
            db.TripParticipantRoles.Add(new TripParticipantRole { Code = AddedParticipantRole, Name = "Echipator (generat)", SortOrder = 900 });
        }

        if (!await db.ExpeditionRosterRoles.AnyAsync(r => r.Code == AddedRosterRole, ct))
        {
            db.ExpeditionRosterRoles.Add(new ExpeditionRosterRole { Code = AddedRosterRole, Name = "Cercetător (generat)", SortOrder = 900 });
        }

        await session.SaveAndRegisterAsync(db, ct);

        if (!await db.TripTypes.AnyAsync(t => t.Code == TestDataActivityPlanner.AddedTripTypeCode, ct))
        {
            var type = await tripTypes.CreateAsync(new TripTypeInput(
                TestDataActivityPlanner.AddedTripTypeCode,
                "Cartare de tabără (generată)",
                "O tură de cartare făcută dintr-o tabără, cu lungimea, stațiile și metoda în datele de teren.",
                900,
                FieldDataSchema,
                null,
                null,
                checklist.Id), ct);
            session.Register(db, "trip_types", type.Id);
            session.Count(TestDataSession.CountKind("trip_types"));
            await db.SaveChangesAsync(ct);
        }

        var items = await db.ChecklistItems.Where(i => i.ChecklistId == checklist.Id).OrderBy(i => i.SortOrder).Select(i => i.Id).ToListAsync(ct);
        return new TestDataActivityVocabulary(
            await db.TripTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct),
            await db.TripParticipantRoles.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct),
            await db.ExpeditionRosterRoles.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct),
            checklist.Id,
            items);
    }

    public static async Task WriteExpeditionsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var vocabulary = session.Activity!;
        foreach (var camp in session.Plan.Activity.Expeditions)
        {
            var region = session.Plan.Regions.FirstOrDefault(r => r.Region.Code == camp.RegionCode)?.Region;
            Geometry? area = null;
            if (region is not null && camp.SubAreaIndex is { } sub && sub < region.SubAreas.Count)
            {
                var a = region.SubAreas[sub];
                area = new Polygon(new LinearRing([new(a.West, a.South), new(a.East, a.South), new(a.East, a.North), new(a.West, a.North), new(a.West, a.South)])) { SRID = 4326 };
            }

            var expedition = new Expedition
            {
                Name = camp.Name,
                OwnerUserId = OwnerOf(session, camp.Roster.Select(r => r.CaverIndex)),
                Description = camp.Description,
                PublicNote = camp.PublicNote,
                StartDate = camp.Start,
                EndDate = camp.End,
                Geom = area,
                CavingGroupId = session.Clubs[camp.ClubIndex],
                Visibility = camp.Visibility,
                State = camp.State,
                PublishedAt = camp.State == ActivityState.Published ? At(camp.End.AddDays(5)) : null,
            };
            db.Expeditions.Add(expedition);
            session.Expeditions[camp.Index] = expedition.Id;
            foreach (var entry in camp.Roster)
            {
                db.ExpeditionRoster.Add(new ExpeditionRosterEntry
                {
                    ExpeditionId = expedition.Id,
                    CaverId = session.Cavers[entry.CaverIndex],
                    RoleId = vocabulary.RosterRoles[entry.RoleCode],
                    FromDate = entry.From,
                    ToDate = entry.To,
                });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    public static async Task WriteTripsAsync(
        SilexGisDbContext db,
        TripLogWriteService trips,
        TestDataSession session,
        IReadOnlyList<PlannedTrip> chunk,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(trips);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(chunk);
        var vocabulary = session.Activity!;
        var ctx = await Permissions.AccessContextResolver.ResolveAsync(db, session.OwnerUserId, ct);
        foreach (var planned in chunk)
        {
            var owner = OwnerOf(session, planned.Members.Select(m => m.CaverIndex));
            var input = new TripWriteInput
            {
                Title = planned.Title,
                TripTypeId = vocabulary.TripTypes.GetValueOrDefault(planned.TypeCode),
                TripDate = planned.Date,
                TripDateEnd = planned.DateEnd,
                EntryTime = planned.Entry,
                ExitTime = planned.Exit,
                Description = planned.Description,
                Results = planned.Results,
                WeatherConditions = planned.Weather,
                OrganizingCavingGroupId = planned.OrganizingClubIndex is { } organiser ? session.Clubs[organiser] : null,
                CaveIds = [.. planned.CaveIndices.Where(session.Caves.ContainsKey).Select(i => session.Caves[i])],
                Participants = [.. planned.Members.Select(m => new TripRosterEntry
                {
                    CaverId = session.Cavers[m.CaverIndex],
                    RoleId = m.RoleCode is { } code ? vocabulary.ParticipantRoles[code] : null,
                    EntryTime = m.Entry,
                })],
                Proposers = planned.Proposers.Count == 0 ? null : [.. planned.Proposers.Select(p => new TripRosterEntry { CaverId = session.Cavers[p] })],
                Visibility = planned.Visibility,
                CavingGroupId = planned.ClubIndex is { } club ? session.Clubs[club] : null,
                DepthReachedM = planned.DepthReachedM,
                LengthSurveyedM = planned.LengthSurveyedM,
                SurveyStations = planned.SurveyStations,
                RopeMetres = planned.RopeMetres,
                HadIncident = planned.HadIncident,
                Sections = planned.TypeCode == TestDataActivityPlanner.AddedTripTypeCode && !planned.Future
                    ? new TripSectionWrite(FieldData(planned), null, null)
                    : new TripSectionWrite(null, null, null),
            };
            var trip = await trips.CreateAsync(
                planned.Future ? TripCreationIntent.Plan : TripCreationIntent.Report, input, ctx, owner, TripWriteNotice.Silent, ct);

            // A new trip starts as a draft; the run sets the state it has reached, as an import does.
            trip.State = planned.State;
            trip.PublishedAt = planned.State == ActivityState.Published ? At(planned.Date.AddDays(3)) : null;
            if (planned.Deleted)
            {
                trip.DeletedAt = At(planned.Date.AddDays(10));
                trip.DeletedByUserId = owner;
            }

            session.Trips[planned.Index] = trip.Id;
            foreach (var invitation in planned.Invitations)
            {
                db.TripInvitations.Add(new TripInvitation
                {
                    TripLogId = trip.Id,
                    CaverId = session.Cavers[invitation.CaverIndex],
                    Response = invitation.Response,
                    Note = invitation.Note,
                    InvitedAt = At(planned.Date.AddDays(-14)),
                    InvitedByUserId = owner,
                    RespondedAt = invitation.Response == TripInvitationResponse.Pending ? null : At(planned.Date.AddDays(-10)),
                });
            }

            foreach (var team in planned.Teams)
            {
                db.TripTeams.Add(new TripTeam { TripLogId = trip.Id, Title = team });
            }

            // A mapping trip of the run's own type ticked its checklist before it set off.
            if (planned.TypeCode == TestDataActivityPlanner.AddedTripTypeCode && !planned.Future)
            {
                foreach (var item in vocabulary.ChecklistItems)
                {
                    db.TripChecklistTicks.Add(new TripChecklistTick
                    {
                        TripLogId = trip.Id,
                        ChecklistId = vocabulary.ChecklistId,
                        ItemId = item,
                        TickedByUserId = owner,
                        TickedAt = At(planned.Date.AddDays(-1)),
                    });
                }
            }

            if (planned.ExpeditionIndex is { } camp && session.Expeditions.TryGetValue(camp, out var expedition))
            {
                db.ExpeditionTrips.Add(new ExpeditionTrip { ExpeditionId = expedition, TripLogId = trip.Id, JoinedAt = At(planned.Date) });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    public static async Task WriteEventsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        foreach (var planned in session.Plan.Activity.Events)
        {
            var club = session.Clubs[planned.ClubIndex];
            var owner = OwnerOf(session, session.Plan.People.Cavers
                .Where(c => c.Memberships.Any(m => m.ClubIndex == planned.ClubIndex && m.Role != CavingGroupRole.Member))
                .Select(c => c.Index));
            IReadOnlyList<DateOnly> days = [planned.Start];
            Guid? series = null;
            if (planned.Frequency is { } frequency)
            {
                var plan = EventRecurrence.Plan(planned.Start, frequency, planned.Count, null, planned.SpanDays);
                if (plan.Refused)
                {
                    throw new InvalidOperationException($"A planned event series is refused ({plan.RefusalCode}): {planned.Title}.");
                }

                days = plan.Days;
                series = Guid.CreateVersion7();
            }

            Guid? first = null;
            foreach (var day in days)
            {
                var row = new Event
                {
                    Title = planned.Title,
                    Description = planned.Description,
                    Kind = planned.Kind,
                    StartDate = day,
                    EndDate = planned.SpanDays > 0 ? day.AddDays(planned.SpanDays) : null,
                    StartTime = planned.StartTime,
                    EndTime = planned.EndTime,
                    Place = planned.Place,
                    OwnerUserId = owner,
                    CavingGroupId = club,
                    Visibility = planned.Visibility,
                    // A meeting that has been held is done; one still to come is planned.
                    State = day > TestDataPlanner.LastDay ? ActivityState.Planned
                        : planned.State == ActivityState.Planned ? ActivityState.Done : planned.State,
                    MaxParticipants = planned.MaxParticipants,
                    SeriesId = series,
                    SeriesRule = series is null ? null : planned.Rule,
                    SeriesFrequency = series is null ? null : planned.Frequency,
                };
                db.Events.Add(row);
                first ??= row.Id;
            }

            foreach (var invitation in planned.Invitations)
            {
                db.TripInvitations.Add(new TripInvitation
                {
                    EventId = first,
                    CaverId = session.Cavers[invitation.CaverIndex],
                    Response = invitation.Response,
                    InvitedAt = At(planned.Start.AddDays(-20)),
                    InvitedByUserId = owner,
                    RespondedAt = invitation.Response == TripInvitationResponse.Pending ? null : At(planned.Start.AddDays(-15)),
                });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>The account a row is written for: the first of its people who has one, else the run's administrator.</summary>
    private static Guid OwnerOf(TestDataSession session, IEnumerable<int> cavers)
    {
        foreach (var caver in cavers)
        {
            if (session.Users.TryGetValue(caver, out var user) && session.Plan.People.Cavers[caver].Account is { Kind: not TestDataAccountKind.Deactivated })
            {
                return user;
            }
        }

        return session.OwnerUserId;
    }

    private static string FieldData(PlannedTrip trip) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["length_m"] = (double)(trip.LengthSurveyedM ?? 40),
            ["stations"] = trip.SurveyStations ?? 8,
            ["method"] = trip.Index % 3 == 0 ? "busolă și clinometru" : "DistoX",
        });

    private static DateTimeOffset At(DateOnly day) => new(day.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero);
}
