// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Metadata;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Demo dataset for local exploration and E2E tests (`dotnet run -- seed-demo`).
/// Owned by the given user; idempotent by identification code. Routes every feature
/// write through <see cref="FeatureWriteService"/> and every document write through
/// <see cref="DocumentWriteService"/> — the seeder is ordinary write-path code, not a
/// bypass, so a demo installation exercises the same rules a real one does.
/// </summary>
public static class DemoSeeder
{
    /// <summary>
    /// What each seeded trip did where it went, in the order the trips are listed. Read round and
    /// round, so adding a trip further down needs no matching entry here and cannot walk off the
    /// end of it — which it would do at run time on a fresh installation, not at compile time.
    /// </summary>
    private static readonly string[] TripRoles =
        ["trip-objective", "trip-surveyed", "trip-visited", "trip-visited", "trip-work-area"];

    /// <param name="documents">
    /// Present when the caller can also store bytes. Without it the dataset is caves and
    /// features only, which is what a caller with no file store can honestly produce.
    /// </param>
    public static async Task SeedAsync(
        SilexGisDbContext db,
        Guid ownerUserId,
        DocumentWriteService? documents = null,
        IFileStore? fileStore = null,
        CancellationToken ct = default)
    {
        var writer = new FeatureWriteService(db, new JsonSchemaPropertiesValidator(), new AnonymousCurrentUser());

        // Each section guards itself so re-running tops up data added in later versions.
        var demoCaveId = await db.Caves
            .Where(c => c.IdentificationCode == "DEMO-0001")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (demoCaveId is null)
        {
            demoCaveId = await SeedCavesAsync(db, writer, ownerUserId, ct);
        }

        await SeedGenericFeaturesAsync(db, writer, ownerUserId, demoCaveId.Value, ct);
        await db.SaveChangesAsync(ct);

        // Every kind of thing the selector offers needs something to find, or its button looks
        // broken rather than empty. Trips and saved views had nothing at all until now.
        await SeedMoreCavesAsync(db, writer, ownerUserId, ct);
        await db.SaveChangesAsync(ct);

        await SeedTripLogsAsync(db, ownerUserId, ct);
        await SeedMapViewsAsync(db, ownerUserId, ct);
        await db.SaveChangesAsync(ct);

        if (documents is not null && fileStore is not null)
        {
            await SeedDocumentsAsync(db, documents, fileStore, ownerUserId, demoCaveId.Value, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// A filed report and the link that says which cave it is about — the two halves of the
    /// dataset that only exist once documents and resource links do.
    /// </summary>
    /// <remarks>
    /// The report is a real PDF with real text, not a placeholder, because everything worth
    /// demonstrating about it needs the text to be there: the words are read into the search
    /// index by the same background job a genuine upload queues, the pages are drawn from the
    /// same file, and the link below anchors to a passage rather than to the document as a
    /// whole. A zero-byte stand-in would leave every one of those looking broken.
    /// </remarks>
    private static async Task SeedDocumentsAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid ownerUserId,
        Guid demoCaveId,
        CancellationToken ct)
    {
        const string cabinetName = "Demo archive";
        if (await db.Cabinets.AnyAsync(c => c.Name == cabinetName && c.ParentId == null, ct))
        {
            return;
        }

        // Through the write service so the materialized path and the ancestor array are
        // stamped by the one thing that knows how, exactly as a cabinet made in the interface
        // would be. The parent has to reach the database before the child can be placed under
        // it, because placement is decided from the parent's stored path.
        var cabinets = new CabinetWriteService(db);
        var archive = await cabinets.CreateAsync(
            cabinetName, "Where the demonstration dataset files its paperwork.", null, ct);
        await db.SaveChangesAsync(ct);

        var surveys = await cabinets.CreateAsync(
            "Survey reports", "Reports written up after a survey trip.", archive.Id, ct);

        var bytes = DemoPdf.Build();
        var storagePath = await StoreAsync(fileStore, bytes, ".pdf", ct);
        var content = new StoredContent(
            storagePath,
            "pestera-demo-mare-1987.pdf",
            "application/pdf",
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            FileKind.Document);

        var report = documents.Create(
            content,
            title: "Peștera Demo Mare — 1987 survey report",
            ownerUserId: ownerUserId,
            uploadedBy: ownerUserId,
            documentDate: new DateOnly(1987, 8, 14));

        // A demonstration document nobody but its owner can read demonstrates nothing, and the
        // demo caves are public for the same reason. The kind is set because it is what the
        // shelf listing, the search filters and the metadata schema all key off.
        //
        // The document is read out of the change tracker rather than the database: the write
        // service tracks it and leaves committing to the caller, so at this point it exists
        // only here.
        var document = db.Documents.Local.Single(d => d.Id == report.Version.DocumentId);
        document.Visibility = Visibility.Public;
        document.DocumentTypeId = await db.DocumentTypes
            .Where(t => t.Code == "survey_report")
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        db.CabinetDocuments.Add(new CabinetDocument
        {
            CabinetId = surveys.Id,
            DocumentId = report.Version.DocumentId,
        });

        await SeedResourceLinkAsync(db, ownerUserId, demoCaveId, report, ct);
    }

    /// <summary>
    /// The demonstration link: this cave is documented by that report, and specifically by the
    /// sentence about the chamber.
    /// </summary>
    /// <remarks>
    /// The anchor is written with offsets into the report's own text, computed here from the
    /// same lines the PDF was built out of — the offsets and the quote therefore agree by
    /// construction rather than by anyone keeping two constants in step. Once the extraction
    /// job has run, the same passage is what a reader selecting it in the browser would
    /// produce, which is what makes this a demonstration of the feature rather than a
    /// hand-placed number.
    /// </remarks>
    private static async Task SeedResourceLinkAsync(
        SilexGisDbContext db, Guid ownerUserId, Guid demoCaveId, DocumentFile report, CancellationToken ct)
    {
        var relationTypeId = await db.ResLinkRelationTypes
            .Where(r => r.Code == "documents")
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);

        var link = new ResLink
        {
            ShortCode = ResLinkRules.NewShortCode(),
            RelationTypeId = relationTypeId,
            Description = "The 1987 report, and the cave it is about.",
            CreatedBy = ownerUserId,
        };
        db.ResLinks.Add(link);

        // The report reads from: "this document documents that cave".
        var page = DemoPdf.Pages[0];
        var pageText = string.Join('\n', page);
        var start = pageText.IndexOf(DemoPdf.LinkedQuote, StringComparison.Ordinal);
        var anchor = start < 0 ? null : JsonSerializer.Serialize(
            new
            {
                page = 1,
                start,
                end = start + DemoPdf.LinkedQuote.Length,
                quote = DemoPdf.LinkedQuote,
            },
            JsonSerializerOptions.Web);

        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = link.Id,
            EntityType = AttachedEntityType.Document,
            EntityId = report.Version.DocumentId,
            IsMain = true,
            AnchorKind = anchor is null ? AnchorKind.Whole : AnchorKind.TextRange,
            Anchor = anchor,
            // Measured against this file, which never changes; a later version re-anchors by
            // quote rather than silently pointing at whatever is now at those offsets.
            AnchorFileId = anchor is null ? null : report.File.Id,
            SortOrder = 0,
            Note = "The passage describing the chamber beyond the second sump.",
            AddedBy = ownerUserId,
        });

        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = link.Id,
            FeatureId = demoCaveId,
            SortOrder = 1,
            AddedBy = ownerUserId,
        });
    }

    private static async Task<string> StoreAsync(
        IFileStore fileStore, byte[] bytes, string extension, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return await fileStore.SaveAsync(stream, extension, ct);
    }

    private static async Task<Guid> SeedCavesAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid ownerUserId, CancellationToken ct)
    {
        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync(ct);
        var pitTypeId = await db.CaveTypes.Where(t => t.Code == "pit").Select(t => t.Id).SingleAsync(ct);
        var naturalEntranceId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync(ct);
        var limestoneId = await db.RockTypes.Where(t => t.Code == "limestone").Select(t => t.Id).SingleAsync(ct);
        var karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync(ct);

        // A containing karst area, so the demo exercises the hierarchy from day one.
        var area = await writer.CreateGenericAsync(
            new Feature
            {
                FeatureTypeId = karstAreaTypeId,
                Name = "Platoul Demo",
                Description = "Demonstration karst area containing the demo caves.",
                Geom = new Polygon(new LinearRing(
                [
                    new Coordinate(25.42, 45.51), new Coordinate(25.46, 45.51),
                    new Coordinate(25.46, 45.54), new Coordinate(25.42, 45.54),
                    new Coordinate(25.42, 45.51),
                ]))
                { SRID = 4326 },
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Public,
            },
            parents: [], ct);

        // Around Brașov / Piatra Craiului — plausible but fictional demo data.
        var bigDemo = await writer.CreateCaveAsync(
            new Feature
            {
                Name = "Peștera Demo Mare",
                Description = "Demonstration cave with two entrances and public visibility.",
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Public,
            },
            new Cave
            {
                OtherToponyms = "Big Demo Cave",
                IdentificationCode = "DEMO-0001",
                CaveTypeId = caveTypeId,
                RockTypeId = limestoneId,
                Region = "Brașov",
                HydrographicBasin = "Olt",
                Valley = "Valea Demo",
                SurveyedLength = 1234.5m,
                Depth = 87.2m,
                Altitude = 950m,
                ExplorationStatus = ExplorationStatus.Ongoing,
            },
            parents: [new ParentSpec(area.Id, IsPrimary: true)], ct);

        await AddEntranceAsync(writer, bigDemo.Id, ownerUserId, naturalEntranceId,
            "Main entrance", 25.4472, 45.5312, 952m, isMain: true, ct);
        await AddEntranceAsync(writer, bigDemo.Id, ownerUserId, naturalEntranceId,
            "Upper entrance", 25.4481, 45.5325, 1010m, isMain: false, ct);

        var protectedPit = await writer.CreateCaveAsync(
            new Feature
            {
                Name = "Avenul Demo Protejat",
                Description = "Protected demo pit — exact location restricted (bat colony).",
                LocationProtected = true,
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Authenticated,
            },
            new Cave
            {
                IdentificationCode = "DEMO-0002",
                CaveTypeId = pitTypeId,
                RockTypeId = limestoneId,
                Region = "Brașov",
                Depth = 154m,
                ClosestAddress = "Forest road 12, km 3 (redacted for non-members)",
            },
            parents: [new ParentSpec(area.Id, IsPrimary: true)], ct);

        await AddEntranceAsync(writer, protectedPit.Id, ownerUserId, naturalEntranceId,
            "Shaft", 25.2101, 45.5187, 1420m, isMain: true, ct);
        // The protection flag was set at creation; restamp the subtree now that the
        // entrance exists.
        await writer.SetLocationProtectedAsync(protectedPit.Id, true, ct);

        return bigDemo.Id;
    }

    private static async Task AddEntranceAsync(
        FeatureWriteService writer, Guid caveFeatureId, Guid ownerUserId, long entranceTypeId,
        string name, double lon, double lat, decimal altitude, bool isMain, CancellationToken ct)
    {
        await writer.CreateEntranceAsync(
            new Feature
            {
                Name = name,
                Geom = new Point(new CoordinateZ(lon, lat, (double)altitude)) { SRID = 4326 },
                OwnerUserId = ownerUserId,
            },
            new CaveEntrance
            {
                CaveFeatureId = caveFeatureId,
                EntranceTypeId = entranceTypeId,
                IsMain = isMain,
                Altitude = altitude,
                PositionQuality = PositionQuality.Gps,
            },
            ct);
    }

    private static async Task SeedGenericFeaturesAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid ownerUserId, Guid demoCaveId, CancellationToken ct)
    {
        if (await db.Features.AnyAsync(f => f.Name == "Dolina Demo", ct))
        {
            return;
        }

        var sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync(ct);
        var fractureTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync(ct);
        var associatedCaveKindId = await db.LinkKinds.Where(k => k.Code == "associated_cave").Select(k => k.Id).SingleAsync(ct);

        var sinkhole = await writer.CreateGenericAsync(
            new Feature
            {
                Name = "Dolina Demo",
                FeatureTypeId = sinkholeTypeId,
                Geom = new Point(25.4455, 45.5301) { SRID = 4326 },
                Description = "Demo sinkhole above the main gallery.",
                Properties = """{"depth_m": 12.5, "diameter_m": 30}""",
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Public,
            },
            parents: [], ct);

        // The old hardcoded cave association is a typed, locating link now.
        db.FeatureLinks.Add(new FeatureLink
        {
            FromId = sinkhole.Id,
            ToId = demoCaveId,
            LinkKindId = associatedCaveKindId,
        });

        await writer.CreateGenericAsync(
            new Feature
            {
                Name = "Falia Demo",
                FeatureTypeId = fractureTypeId,
                Geom = new LineString(
                [
                    new Coordinate(25.4420, 45.5280),
                    new Coordinate(25.4460, 45.5305),
                    new Coordinate(25.4490, 45.5335),
                ])
                { SRID = 4326 },
                Description = "Demo fracture line crossing the plateau.",
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Public,
            },
            parents: [], ct);
    }

    /// <summary>
    /// A handful more caves, so a list is a list rather than two rows.
    /// </summary>
    /// <remarks>
    /// Deliberately varied in the axes somebody sorts and filters by — visibility, depth, region,
    /// exploration status — because a demo where every row is identical proves that sorting runs
    /// and nothing about whether it is right. Several share a name stem so type-ahead has
    /// something to narrow.
    /// </remarks>
    private static async Task SeedMoreCavesAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid ownerUserId, CancellationToken ct)
    {
        if (await db.Caves.AnyAsync(c => c.IdentificationCode == "DEMO-0003", ct))
        {
            return;
        }

        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync(ct);
        var pitTypeId = await db.CaveTypes.Where(t => t.Code == "pit").Select(t => t.Id).SingleAsync(ct);
        var limestoneId = await db.RockTypes.Where(t => t.Code == "limestone").Select(t => t.Id).SingleAsync(ct);
        var naturalEntranceId = await db.EntranceTypes
            .Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync(ct);
        var areaId = await db.Features
            .Where(f => f.Name == "Platoul Demo").Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);

        var more = new[]
        {
            ("DEMO-0003", "Peștera Demo Mică", "Small demo cave, public.",
                caveTypeId, Visibility.Public, 210.0m, 24.5m, "Brașov", 25.4390, 45.5265, 880m),
            ("DEMO-0004", "Peștera Demo Ursului", "Demo cave with a bear-bone chamber.",
                caveTypeId, Visibility.Public, 1890.0m, 63.0m, "Bihor", 22.5560, 46.5490, 640m),
            ("DEMO-0005", "Avenul Demo Vântului", "Demo pit with a strong draught.",
                pitTypeId, Visibility.Authenticated, 640.0m, 198.0m, "Hunedoara", 22.8120, 45.4410, 1180m),
            ("DEMO-0006", "Peștera Demo Izvorului", "Demo resurgence cave, club members only.",
                caveTypeId, Visibility.CavingGroup, 3120.0m, 41.0m, "Bihor", 22.5915, 46.5225, 520m),
        };

        foreach (var (code, name, description, typeId, visibility, length, depth, region, lon, lat, altitude) in more)
        {
            var cave = await writer.CreateCaveAsync(
                new Feature
                {
                    Name = name,
                    Description = description,
                    OwnerUserId = ownerUserId,
                    Visibility = visibility,
                },
                new Cave
                {
                    IdentificationCode = code,
                    CaveTypeId = typeId,
                    RockTypeId = limestoneId,
                    Region = region,
                    SurveyedLength = length,
                    Depth = depth,
                    Altitude = altitude,
                    ExplorationStatus = ExplorationStatus.Finished,
                },
                parents: areaId is null ? [] : [new ParentSpec(areaId.Value, IsPrimary: true)], ct);

            await AddEntranceAsync(writer, cave.Id, ownerUserId, naturalEntranceId,
                name + " entrance", lon, lat, altitude, isMain: true, ct);
        }
    }

    /// <summary>
    /// Trips, with the people on them.
    /// </summary>
    /// <remarks>
    /// Cavers are seeded alongside rather than assumed: a trip whose participants are all the one
    /// demo account is not what a club's records look like, and it would leave the roster controls
    /// with nothing to show. None of these cavers holds an account, which is the ordinary case the
    /// identity model exists for.
    /// </remarks>
    private static async Task SeedTripLogsAsync(
        SilexGisDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        if (await db.TripLogs.AnyAsync(t => t.Title.StartsWith("Demo:"), ct))
        {
            return;
        }

        var caverIds = new List<Guid>();
        foreach (var fullName in new[] { "Ana Demo", "Bogdan Demo", "Cristina Demo", "Dan Demo" })
        {
            var existing = await db.Cavers
                .Where(c => c.FullName == fullName)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                caverIds.Add(existing.Value);
                continue;
            }

            var caver = new Caver { FullName = fullName };
            db.Cavers.Add(caver);
            caverIds.Add(caver.Id);
        }

        await db.SaveChangesAsync(ct);

        var caveIds = await db.Caves
            .OrderBy(c => c.IdentificationCode)
            .Select(c => c.Id)
            .Take(4)
            .ToListAsync(ct);

        // Spread across a year and across types, so a date sort and a type filter both have
        // something to do. The dates are fixed rather than relative to now: a demo that drifts is
        // a demo whose screenshots stop matching it.
        // The lifecycle is mostly announced trips, because that is what a real archive is, with one
        // still being written and one called off so the states that read differently are both on
        // screen without being hunted for. A published trip carries the day it was announced —
        // fixed like the trip dates, for the same reason.
        // Purposes are rows now, so the demo names them by the code an installation exchanges
        // them under and looks the identity up; a code missing here means the taxonomy seed did
        // not run, and a demo trip with no purpose would hide that.
        var tripTypeIds = await db.TripTypes.ToDictionaryAsync(t => t.Code, t => t.Id, ct);

        var trips = new[]
        {
            ("Demo: exploration push", "exploration", new DateOnly(2026, 3, 14), Visibility.Public,
                ActivityState.Published, (DateTimeOffset?)new DateTimeOffset(2026, 3, 16, 18, 0, 0, TimeSpan.Zero)),
            ("Demo: survey trip", "survey", new DateOnly(2026, 4, 2), Visibility.Public,
                ActivityState.Published, new DateTimeOffset(2026, 4, 5, 18, 0, 0, TimeSpan.Zero)),
            ("Demo: science trip", "science", new DateOnly(2026, 5, 23), Visibility.Authenticated,
                ActivityState.Published, new DateTimeOffset(2026, 5, 27, 18, 0, 0, TimeSpan.Zero)),
            ("Demo: training weekend", "training", new DateOnly(2026, 6, 6), Visibility.CavingGroup,
                ActivityState.Draft, null),
            ("Demo: maintenance and rebolting", "maintenance", new DateOnly(2026, 7, 18), Visibility.Public,
                ActivityState.Cancelled, null),
        };

        var index = 0;
        foreach (var (title, typeCode, date, visibility, state, publishedAt) in trips)
        {
            if (!tripTypeIds.TryGetValue(typeCode, out var tripTypeId))
            {
                throw new InvalidOperationException($"Trip type '{typeCode}' is not seeded.");
            }

            var trip = new TripLog
            {
                Title = title,
                TripTypeId = tripTypeId,
                TripDate = date,
                Description = "Demonstration trip log.",
                EntryTime = new TimeOnly(9, 30),
                ExitTime = new TimeOnly(16, 45),
                OwnerUserId = ownerUserId,
                Visibility = visibility,
                State = state,
                PublishedAt = publishedAt,
            };
            // What a trip is counted by, on the trips that would plausibly produce numbers: a
            // demo where nothing is ever measured shows none of it, and a demo where everything
            // is measured suggests the figures are required. Exactly one trip went wrong, so a
            // search for incidents has both an answer and a counter-example.
            switch (typeCode)
            {
                case "exploration":
                    trip.DepthReachedM = 218.0m;
                    trip.RopeMetres = 260m;
                    break;
                case "survey":
                    trip.DepthReachedM = 96.5m;
                    trip.LengthSurveyedM = 412.5m;
                    trip.SurveyStations = 47;
                    break;
                case "training":
                    trip.HadIncident = true;
                    break;
                default:
                    break;
            }

            db.TripLogs.Add(trip);

            // The person who proposed it and one who was there — enough that the roster is a
            // roster and not a single name.
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = caverIds[index % caverIds.Count],
                Kind = TripParticipantKind.Proposer,
            });
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = caverIds[(index + 1) % caverIds.Count],
                Kind = TripParticipantKind.Participant,
            });

            // Where the trip went, and what it did there. Several roles rather than one, so a
            // surface that answers over all of them can be told apart from one that only ever
            // looked at the first: with a single role in the data, the two are indistinguishable
            // and a narrowing would go unnoticed. The protected cave keeps its place on a public
            // announced trip — that pairing is the only one showing a link being held back.
            if (caveIds.Count > 0
                && !await TripRoleLinks.NameFeatureAsync(
                    db,
                    trip.Id,
                    caveIds[index % caveIds.Count],
                    TripRoles[index % TripRoles.Length],
                    ownerUserId,
                    ct))
            {
                throw new InvalidOperationException(
                    $"Relation type '{TripRoles[index % TripRoles.Length]}' is not seeded.");
            }

            index++;
        }
    }

    /// <summary>
    /// A couple of saved views, so the map has somewhere to be taken.
    /// </summary>
    private static async Task SeedMapViewsAsync(
        SilexGisDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        if (await db.MapViews.AnyAsync(v => v.Name.StartsWith("Demo:"), ct))
        {
            return;
        }

        var views = new[]
        {
            ("Demo: Platoul Demo", "The demo karst area, at plateau scale.", 25.44, 45.525, 13.0),
            ("Demo: Bihor caves", "The Bihor demo caves.", 22.57, 46.535, 12.0),
        };

        foreach (var (name, description, lon, lat, zoom) in views)
        {
            db.MapViews.Add(new MapView
            {
                Name = name,
                Description = description,
                Config = JsonSerializer.Serialize(new { center = new[] { lon, lat }, zoom }),
                OwnerUserId = ownerUserId,
                Visibility = Visibility.Public,
            });
        }
    }
}
