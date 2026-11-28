// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Domain.Potential;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.TestData;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The rest of what the run's people did and kept: a few survey trips watched from the surface
/// with the word that came out of the cave, the links people drew between caves, trips, cavers and
/// survey files (a busy cave among them, and a relation type of the run's own), the map views and
/// filters accounts saved, a report template for the run's trip type, the clubs' announcements and
/// a cave-potential model. Nothing here sends anything.
/// </summary>
public static class TestDataActivityExtrasWriter
{
    public const string RelationTypeCode = "testgen-explored-with";

    private static readonly string[] SavedFilters =
    [
        """{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"surveyedLength","op":"greaterThan","values":[{"type":"number","value":500}]}}]}""",
        """{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"altitude","op":"lessThan","values":[{"type":"number","value":800}]}}],"sort":"updated","descending":true}""",
    ];

    /// <summary>Three past survey trips on surveyed caves, watched from the surface: who went in, where they were, and when they came out.</summary>
    public static async Task WriteTrackingAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var watched = session.Plan.Activity.Trips
            .Where(t => !t.Future && !t.Deleted && t.SurveyStations is not null && t.Members.Count >= 2
                && session.SurveyModels.ContainsKey(t.CaveIndices[0]) && session.Trips.ContainsKey(t.Index))
            .Take(3)
            .ToList();
        foreach (var (planned, n) in watched.Select((t, i) => (t, i)))
        {
            var tripId = session.Trips[planned.Index];
            var cave = session.Plan.Caves[planned.CaveIndices[0]];
            var model = session.SurveyModels[cave.Index];
            var named = cave.Network!.Stations.Where(s => s.Name.Length > 0 && !s.Entrance).ToList();
            var start = new DateTimeOffset(planned.Date.ToDateTime(planned.Entry ?? new TimeOnly(9, 0)), TimeSpan.Zero);
            db.TripTrackings.Add(new TripTracking
            {
                TripLogId = tripId,
                State = TripTrackingState.Closed,
                SurveyModelId = model,
                CaveFeatureId = session.Caves[cave.Index],
                ArmedAt = start.AddMinutes(-30),
                FirstArmedAt = start.AddMinutes(-30),
                ClosedAt = start.AddHours(6),
            });

            // Published only where nothing but generated data is, as the cave shares are: a link
            // nobody holds, there to be listed and withdrawn.
            if (n == 0 && !session.AlongsideExistingData)
            {
                db.TripTrackingShares.Add(new TripTrackingShare
                {
                    TripLogId = tripId,
                    TokenHash = TripTrackingRules.ShareTokenHash(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))),
                    CreatedBy = session.OwnerUserId,
                    ExpiresAt = TripPublicationWindow.ExpiresAtFor(
                        TripPublicationWindow.EndOfTrip(planned.Date, planned.DateEnd), planned.Date, planned.DateEnd, TripPublicationWindow.DefaultShareLifetime),
                });
            }

            foreach (var (member, k) in planned.Members.Take(3).Select((m, i) => (m, i)))
            {
                var caver = session.Cavers[member.CaverIndex];
                var label = session.Plan.People.Cavers[member.CaverIndex].ShortName ?? session.Plan.People.Cavers[member.CaverIndex].FullName.Split(' ')[0];
                db.TripTrackingParticipants.Add(new TripTrackingParticipant { TripLogId = tripId, CaverId = caver, DisplayLabel = label });
                var station = named[Math.Min(named.Count - 1, (named.Count / 2) + k)];
                Report(TripPositionEventKind.Entered, null, null, start.AddMinutes(10));
                Report(TripPositionEventKind.AtStation, station.Name, null, start.AddHours(2));
                if (k == 0)
                {
                    Report(TripPositionEventKind.Note, null, "Totul în regulă; continuăm cartarea.", start.AddHours(3));
                }

                Report(TripPositionEventKind.Exited, null, null, start.AddHours(5).AddMinutes(10 * k));

                void Report(TripPositionEventKind kind, string? at, string? note, DateTimeOffset when) =>
                    db.TripPositionEvents.Add(new TripPositionEvent
                    {
                        TripLogId = tripId,
                        CaverId = caver,
                        Kind = kind,
                        SurveyModelId = at is null ? null : model,
                        CaveFeatureId = at is null ? null : session.Caves[cave.Index],
                        ViewerStationName = at,
                        Note = note,
                        RecordedAt = when,
                        RecordedByUserId = session.OwnerUserId,
                    });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>
    /// Links people drew: survey trips to the caves they surveyed, pairs of caves explored
    /// together under the run's own relation type, cavers to the caves they know, a cave's survey
    /// file to the cave — and one busy cave linked many times, for the screens that have to cope.
    /// </summary>
    public static async Task WriteLinksAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var relation = await db.ResLinkRelationTypes.FirstOrDefaultAsync(r => r.Code == RelationTypeCode, ct);
        if (relation is null)
        {
            relation = new ResLinkRelationType
            {
                Code = RelationTypeCode,
                Name = "Explorată împreună cu (generat)",
                Description = "Două peșteri explorate în aceeași campanie; tip de legătură adăugat de generatorul de date de test.",
                Directed = false,
                SortOrder = 900,
            };
            db.ResLinkRelationTypes.Add(relation);
            await session.SaveAndRegisterAsync(db, ct);
        }

        var relations = await db.ResLinkRelationTypes.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Id, ct);
        var owner = session.OwnerUserId;

        // The trips that drew a survey name the cave as surveyed, beside the visit the trip writer recorded.
        foreach (var trip in session.Plan.Activity.Trips.Where(t => t.SurveyStations is not null && session.Trips.ContainsKey(t.Index)))
        {
            if (session.Caves.TryGetValue(trip.CaveIndices[0], out var cave))
            {
                await TripRoleLinks.NameFeatureAsync(db, session.Trips[trip.Index], cave, "trip-surveyed", owner, ct);
            }
        }

        var caves = session.Plan.Caves.Where(c => session.Caves.ContainsKey(c.Index)).ToList();
        var rng = TestDataRng.For(session.Profile.Seed, "reslinks");
        var pairs = Math.Max(1, caves.Count / 10);
        for (var i = 0; i < pairs && caves.Count > 1; i++)
        {
            var a = rng.Pick(caves);
            var b = rng.Pick(caves.Where(c => c.RegionCode == a.RegionCode && c.Index != a.Index).DefaultIfEmpty(caves.First(c => c.Index != a.Index)).ToList());
            Link(relation.Id, false, [Feature(session.Caves[a.Index]), Feature(session.Caves[b.Index])], "Explorate în aceeași campanie.");
        }

        // A caver and the caves they know best.
        foreach (var caver in session.Plan.People.Cavers.Take(Math.Max(1, session.Plan.People.Cavers.Count / 8)))
        {
            var known = Enumerable.Range(0, rng.Between(1, 3)).Select(_ => rng.Pick(caves)).DistinctBy(c => c.Index).ToList();
            Link(relations["related-to"], false,
                [Entity(AttachedEntityType.Caver, session.Cavers[caver.Index]), .. known.Select(c => Feature(session.Caves[c.Index]))],
                null);
        }

        // The busy cave: linked from many trips at once, so lists and the graph meet a long list.
        var busy = session.Plan.Activity.Trips.Where(t => session.Trips.ContainsKey(t.Index)).Take(Math.Min(99, session.Plan.Activity.Trips.Count)).ToList();
        if (busy.Count > 0 && caves.Count > 0)
        {
            Link(relations["documents"], true,
                [Feature(session.Caves[caves[0].Index], main: true), .. busy.Select(t => Entity(AttachedEntityType.TripLog, session.Trips[t.Index]))],
                "Turele care au lăsat urme în această peșteră.");
        }

        await session.SaveAndRegisterAsync(db, ct);

        void Link(long relationTypeId, bool directed, IReadOnlyList<ResLinkMember> members, string? description)
        {
            var link = new ResLink { ShortCode = ResLinkRules.NewShortCode(), RelationTypeId = relationTypeId, CreatedBy = owner, Description = description };
            db.ResLinks.Add(link);
            for (var m = 0; m < members.Count; m++)
            {
                members[m].ResLinkId = link.Id;
                members[m].SortOrder = m;
                members[m].IsMain = directed && members[m].IsMain;
                db.ResLinkMembers.Add(members[m]);
            }
        }

        static ResLinkMember Feature(Guid id, bool main = false) =>
            new() { FeatureId = id, AnchorKind = AnchorKind.Whole, IsMain = main };

        static ResLinkMember Entity(AttachedEntityType type, Guid id) =>
            new() { EntityType = type, EntityId = id, AnchorKind = AnchorKind.Whole };
    }

    /// <summary>Views of the map and filters the accounts saved; one account's view is its home.</summary>
    public static async Task WriteSavedViewsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var holders = session.Plan.People.AccountHolders.Where(c => session.Users.ContainsKey(c.Index)).Take(12).ToList();
        foreach (var (holder, n) in holders.Select((h, i) => (h, i)))
        {
            var region = session.Plan.Regions[n % session.Plan.Regions.Count].Region;
            var user = session.Users[holder.Index];
            db.MapViews.Add(new MapView
            {
                Name = $"{region.MassifName} — vedere de ansamblu",
                Description = "O vedere salvată de generatorul de date de test.",
                Config = JsonSerializer.Serialize(new { center = new[] { (region.West + region.East) / 2, (region.South + region.North) / 2 }, zoom = 12 }),
                OwnerUserId = user,
                Visibility = n % 3 == 0 ? Visibility.Authenticated : Visibility.Private,
                IsHome = n == 0,
            });

            var json = SavedFilters[n % SavedFilters.Length];
            var document = FilterDocument.Deserialize(json);
            if (document is null || !FilterValidation.IsWellFormed(document))
            {
                throw new InvalidOperationException("A generated saved filter is not a well-formed filter document.");
            }

            db.SavedFilters.Add(new SavedFilter
            {
                Name = n % 2 == 0 ? "Peșteri peste 500 m" : "Peșteri joase, cele mai noi întâi",
                Description = "Un filtru salvat de generatorul de date de test.",
                Document = FilterDocument.Serialize(document),
                OwnerUserId = user,
                Visibility = Visibility.Private,
            });
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>A write-up template for the run's own trip type, a cave-potential model, and each club's last announcement.</summary>
    public static async Task WriteClubWordingAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var vocabulary = session.Activity!;
        if (vocabulary.TripTypes.TryGetValue(TestDataActivityPlanner.AddedTripTypeCode, out var type)
            && !await db.ReportTemplates.AnyAsync(t => t.TripTypeId == type, ct))
        {
            const string body = "# Raportul unei ture de cartare\ntitle: {title}\nnote: {dates}\nfield: Peșteri = {caves}\nfield: Lungime cartată = {length}\nfield: Stații = {stations}\nheading: Ce s-a făcut\ntext: {description}\ntext: {results}\nheading: Echipa\nroster:\n";
            if (!ReportTemplateFormat.Parse(body, ReportTemplateKind.Trip).Ok)
            {
                throw new InvalidOperationException("The generated report template does not parse.");
            }

            db.ReportTemplates.Add(new ReportTemplate { Name = "Raport de cartare (generat)", Kind = ReportTemplateKind.Trip, Body = body, TripTypeId = type });
        }

        db.CavePotentialModels.Add(new CavePotentialModel
        {
            Name = "Potențial carstic după pantă (generat)",
            Description = "Un model simplu: pantele line sunt mai favorabile; adăugat de generatorul de date de test.",
            Method = CavePotentialMethod.DirectWeights,
            Definition = CavePotentialModelDefinition.Serialize(new CavePotentialModelDefinition(
                [new EvidenceLayerSetting("slope", new EvidenceTransform(EvidenceTransformKind.FuzzyLinear, Low: 35, High: 5), 1)])),
            OwnerUserId = session.OwnerUserId,
            Visibility = Visibility.Authenticated,
        });

        foreach (var club in session.Plan.People.Clubs)
        {
            var members = session.Plan.People.Cavers.Count(c => c.Memberships.Any(m => m.ClubIndex == club.Index));
            db.CavingGroupAnnouncements.Add(new CavingGroupAnnouncement
            {
                CavingGroupId = session.Clubs[club.Index],
                SenderUserId = session.OwnerUserId,
                Message = "Ședința de joi se mută la ora 19:30. Aduceți echipamentul pentru verificare.",
                CavingGroupName = club.Name,
                SenderName = "Generatorul de date de test",
                RecipientCount = members,
                SegmentsPerCopy = 1,

                // Already handed out: no worker picks it up and nothing is sent to anybody.
                ExpandedAt = DateTimeOffset.UtcNow,
            });
        }

        await session.SaveAndRegisterAsync(db, ct);
    }
}
