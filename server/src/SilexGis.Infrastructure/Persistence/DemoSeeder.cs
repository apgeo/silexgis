// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Events;
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.Features;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Trips;

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

    /// <summary>
    /// The jobs the demo hands out beyond simply being there and proposing it, read round and
    /// round like the link roles above so the list and the trips need not be the same length.
    /// </summary>
    private static readonly string[] ExtraParticipantRoles =
        ["leader", "driver", "surveyor", "photographer", "trainee", "instructor", "callout_contact"];

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

        // Each section guards itself row by row, so re-running tops up data added in later
        // versions and puts back a row that was lost. A block that stops at the first sign of
        // itself never repairs a partial loss, and on a page the loss reads as the feature being
        // empty rather than as the seed being stale.
        var demoCaveId = await SeedCavesAsync(db, writer, ownerUserId, ct);

        await SeedGenericFeaturesAsync(db, writer, ownerUserId, demoCaveId, ct);
        await db.SaveChangesAsync(ct);

        // Every kind of thing the selector offers needs something to find, or its button looks
        // broken rather than empty. Trips and saved views had nothing at all until now.
        await SeedMoreCavesAsync(db, writer, ownerUserId, ct);
        await db.SaveChangesAsync(ct);

        await SeedTripLogsAsync(db, ownerUserId, ct);
        await SeedExpeditionsAsync(db, ownerUserId, ct);
        await SeedMapViewsAsync(db, ownerUserId, ct);
        await db.SaveChangesAsync(ct);

        // After the camps are saved and on its own guard, not inside theirs: a database seeded
        // before this block existed already holds the camps, so anything gated on their absence
        // would never run there — the demo would quietly stay as it was on every machine that had
        // already seen it.
        await SeedExpeditionRosterAsync(db, ct);
        await SeedExpeditionTripsAsync(db, ct);
        await SeedTripInvitationsAsync(db, ct);
        await db.SaveChangesAsync(ct);

        // After the camp's trips are joined, because a lead reaches the camp's board through them.
        await SeedContinuationsAsync(db, writer, ownerUserId, ct);
        await SeedEventsAsync(db, ownerUserId, ct);
        await db.SaveChangesAsync(ct);

        if (documents is not null && fileStore is not null)
        {
            await SeedDocumentsAsync(db, documents, fileStore, ownerUserId, demoCaveId, ct);
            await db.SaveChangesAsync(ct);

            // After the documents block and outside it, on a guard of its own. That block is
            // gated on the demo archive not existing, so anything nested inside it never runs
            // again on a database that has already seen the demo — the dataset would quietly
            // stay as it was on every machine that had one, which is the same trap the camp
            // roster above is hoisted out of.
            await SeedAnnotatedTextAsync(db, documents, fileStore, ownerUserId, demoCaveId, ct);
            await db.SaveChangesAsync(ct);

            // Last, and on guards of its own for the same reason as the block above: a trip that
            // was followed underground needs a survey to be followed on, a survey is a stored
            // file, and so this half of the dataset exists only where bytes can be kept.
            await SeedTrackedTripAsync(db, documents, fileStore, ownerUserId, demoCaveId, ct);
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
        const string surveysName = "Survey reports";
        const string reportTitle = "Peștera Demo Mare — 1987 survey report";

        // Each piece on its own guard — the two cabinets, the report, its filing, its link — so a
        // lost one comes back on the next run while the rest is left as it is.
        //
        // Through the write service so the materialized path and the ancestor array are
        // stamped by the one thing that knows how, exactly as a cabinet made in the interface
        // would be. The parent has to reach the database before the child can be placed under
        // it, because placement is decided from the parent's stored path.
        var cabinets = new CabinetWriteService(db);
        var archive = await db.Cabinets.FirstOrDefaultAsync(c => c.Name == cabinetName && c.ParentId == null, ct);
        if (archive is null)
        {
            archive = await cabinets.CreateAsync(
                cabinetName, "Where the demonstration dataset files its paperwork.", null, ct);
            await db.SaveChangesAsync(ct);
        }

        var surveys = await db.Cabinets.FirstOrDefaultAsync(c => c.Name == surveysName && c.ParentId == archive.Id, ct)
            ?? await cabinets.CreateAsync(surveysName, "Reports written up after a survey trip.", archive.Id, ct);

        var reportId = await db.Documents
            .Where(d => d.Title == reportTitle)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        Guid reportFileId;
        if (reportId is { } existingReportId)
        {
            reportFileId = await (from version in db.DocumentVersions.AsNoTracking()
                                  join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                                  where version.DocumentId == existingReportId && version.IsCurrent
                                  select file.Id).FirstAsync(ct);
        }
        else
        {
            var (createdId, createdFileId) = await CreateReportAsync(db, documents, fileStore, ownerUserId, reportTitle, ct);
            reportId = createdId;
            reportFileId = createdFileId;
        }

        if (!await db.CabinetDocuments.AnyAsync(c => c.CabinetId == surveys.Id && c.DocumentId == reportId, ct))
        {
            db.CabinetDocuments.Add(new CabinetDocument
            {
                CabinetId = surveys.Id,
                DocumentId = reportId.Value,
            });
        }

        await SeedResourceLinkAsync(db, ownerUserId, demoCaveId, reportId.Value, reportFileId, ct);
    }

    /// <summary>The 1987 report as a stored document: its bytes, its row, and its kind.</summary>
    private static async Task<(Guid DocumentId, Guid FileId)> CreateReportAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid ownerUserId,
        string title,
        CancellationToken ct)
    {
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
            title: title,
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

        return (report.Version.DocumentId, report.File.Id);
    }

    /// <summary>
    /// The demonstration link-annotated text: a page of prose about the demo cave, with three of
    /// its passages linked — to the cave, to a feature on the surface above it, and to the 1987
    /// report the text is the reading of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every anchor here is computed from the body rather than written down, by finding the quote
    /// in the same canonical stream the format defines. That is not tidiness: an offset typed into
    /// this file would be a constant that has to be re-derived by hand whenever a word of the
    /// prose above it changes, and the failure when somebody forgets is a demonstration in which
    /// the highlights sit a few characters off the sentences they belong to — which reads as the
    /// feature not working rather than as the seed being stale.
    /// </para>
    /// <para>
    /// Three passages and not one, because what is worth showing is what one passage cannot: that
    /// a passage may point at something with a position and move the map, that another may point
    /// at a document and open a reader, and that the relation each carries is what colours it.
    /// The middle one overlaps nothing; that case has its own tests rather than a seeded example.
    /// </para>
    /// </remarks>
    private static async Task SeedAnnotatedTextAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid ownerUserId,
        Guid demoCaveId,
        CancellationToken ct)
    {
        const string title = "Peștera Demo Mare — notes on the 1987 survey";
        if (await db.Documents.AnyAsync(d => d.Title == title, ct))
        {
            return;
        }

        // The report this text is the reading of, and the shelf it is filed on, looked up by
        // name rather than handed in — that is what lets this run on a database seeded before
        // it existed. Without the report there is nothing for the text to be the text *of*, so
        // there is nothing worth demonstrating and the block does not run at all.
        var report = await (from version in db.DocumentVersions.AsNoTracking()
                            join reported in db.Documents.AsNoTracking() on version.DocumentId equals reported.Id
                            join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                            where version.IsCurrent && reported.Title.EndsWith("1987 survey report")
                            select new { reported.Id, FileId = file.Id }).FirstOrDefaultAsync(ct);
        if (report is null)
        {
            return;
        }

        var cabinetId = await db.Cabinets.AsNoTracking()
            .Where(c => c.Name == "Survey reports")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        AnnotatedBlock[] blocks =
        [
            new(AnnotatedBlockType.Heading2, "Peștera Demo Mare"),
            new(
                AnnotatedBlockType.Paragraph,
                "The cave was surveyed over three weekends in August 1987. The entrance series is "
                + "reached from the plateau, past Dolina Demo, and drops through a boulder choke "
                + "into the main gallery.",
                [new AnnotatedMark(45, 56, AnnotatedMarkKind.Italic)]),
            new(
                AnnotatedBlockType.Paragraph,
                "Beyond the second sump the passage widens into a chamber some forty metres "
                + "across, described in the 1987 survey report as the largest known volume in the "
                + "system.",
                null),
            new(AnnotatedBlockType.Heading3, "Still to do"),
            new(AnnotatedBlockType.BulletItem, "Re-survey the connection to the upper series.", null),
            new(AnnotatedBlockType.BulletItem, "Photograph the flowstone in the far chamber.", null),
        ];

        var body = new AnnotatedTextBody(blocks);
        var bytes = AnnotatedText.Serialize(body);
        var storagePath = await StoreAsync(fileStore, bytes, AnnotatedText.FileExtension, ct);
        var content = new StoredContent(
            storagePath,
            "pestera-demo-mare-notes" + AnnotatedText.FileExtension,
            AnnotatedText.MediaType,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            FileKind.Document);

        var text = documents.Create(
            content, title, ownerUserId: ownerUserId, uploadedBy: ownerUserId);
        var document = db.Documents.Local.Single(d => d.Id == text.Version.DocumentId);
        document.Visibility = Visibility.Public;

        // Filed beside the report it reads, because that is where somebody would look for it —
        // and because an unfiled document is reachable only through the inbox, which is a
        // different surface from the one this demonstrates.
        if (cabinetId is { } shelf)
        {
            db.CabinetDocuments.Add(new CabinetDocument
            {
                CabinetId = shelf,
                DocumentId = text.Version.DocumentId,
            });
        }

        var stream = AnnotatedText.CanonicalText(blocks);
        var dolinaId = await db.Features
            .Where(f => f.Name == "Dolina Demo")
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);

        await LinkPassageAsync(
            db, ownerUserId, text, stream, "the main gallery", "documents",
            feature: demoCaveId, document: null, ct);

        if (dolinaId is not null)
        {
            await LinkPassageAsync(
                db, ownerUserId, text, stream, "Dolina Demo", "related-to",
                feature: dolinaId.Value, document: null, ct);
        }

        await LinkPassageAsync(
            db, ownerUserId, text, stream, "the 1987 survey report", "text-of",
            feature: null, document: report.Id, ct);
    }

    /// <summary>
    /// One passage of the demonstration text, linked to one thing.
    /// </summary>
    /// <remarks>
    /// The context on either side of the quote is recorded exactly as the browser would record it,
    /// so these anchors behave like authored ones when the text is later edited — including
    /// finding themselves again after a passage above them grows.
    /// </remarks>
    private static async Task LinkPassageAsync(
        SilexGisDbContext db,
        Guid ownerUserId,
        DocumentFile text,
        string stream,
        string quote,
        string relationCode,
        Guid? feature,
        Guid? document,
        CancellationToken ct)
    {
        var start = stream.IndexOf(quote, StringComparison.Ordinal);
        if (start < 0)
        {
            // The prose was edited and this quote no longer occurs in it. Skipped rather than
            // anchored at a guess: a seeded link pointing at the wrong words would demonstrate
            // the failure the whole anchoring design exists to prevent.
            return;
        }

        var end = start + quote.Length;
        var relationTypeId = await db.ResLinkRelationTypes
            .Where(r => r.Code == relationCode)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);

        var link = new ResLink
        {
            ShortCode = ResLinkRules.NewShortCode(),
            RelationTypeId = relationTypeId,
            CreatedBy = ownerUserId,
        };
        db.ResLinks.Add(link);

        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = link.Id,
            EntityType = AttachedEntityType.Document,
            EntityId = text.Version.DocumentId,
            // The text is the end a directed relation reads from for "text-of"; for the others
            // the marker goes to what is being described, below.
            IsMain = relationCode == "text-of",
            AnchorKind = AnchorKind.TextRange,
            Anchor = JsonSerializer.Serialize(
                new
                {
                    start,
                    end,
                    quote,
                    prefix = stream[Math.Max(0, start - 32)..start],
                    suffix = stream[end..Math.Min(stream.Length, end + 32)],
                },
                JsonSerializerOptions.Web),
            AnchorFileId = text.File.Id,
            SortOrder = 0,
            AddedBy = ownerUserId,
        });

        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = link.Id,
            FeatureId = feature,
            EntityType = document is null ? null : AttachedEntityType.Document,
            EntityId = document,
            IsMain = relationCode != "text-of",
            SortOrder = 1,
            AddedBy = ownerUserId,
        });
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
        SilexGisDbContext db, Guid ownerUserId, Guid demoCaveId, Guid reportDocumentId, Guid reportFileId, CancellationToken ct)
    {
        // The link is the row here: one from this report, as its main member, is the one this
        // seeds, and a second would say nothing the first did not.
        if (await db.ResLinkMembers.AnyAsync(
                m => m.EntityType == AttachedEntityType.Document && m.EntityId == reportDocumentId && m.IsMain, ct))
        {
            return;
        }

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
            EntityId = reportDocumentId,
            IsMain = true,
            AnchorKind = anchor is null ? AnchorKind.Whole : AnchorKind.TextRange,
            Anchor = anchor,
            // Measured against this file, which never changes; a later version re-anchors by
            // quote rather than silently pointing at whatever is now at those offsets.
            AnchorFileId = anchor is null ? null : reportFileId,
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

    /// <summary>
    /// Where the big demonstration cave's main entrance is. Named because two things have to agree
    /// on it: the entrance on the map, and the survey of the cave, whose zero is that entrance.
    /// </summary>
    private const double MainEntranceLongitude = 25.4472;

    private const double MainEntranceLatitude = 45.5312;

    private const double MainEntranceHeightM = 952;

    private static async Task<Guid> SeedCavesAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid ownerUserId, CancellationToken ct)
    {
        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync(ct);
        var pitTypeId = await db.CaveTypes.Where(t => t.Code == "pit").Select(t => t.Id).SingleAsync(ct);
        var naturalEntranceId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync(ct);
        var limestoneId = await db.RockTypes.Where(t => t.Code == "limestone").Select(t => t.Id).SingleAsync(ct);
        var karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).SingleAsync(ct);

        // A containing karst area, so the demo exercises the hierarchy from day one. Each row of
        // this block is looked for before it is written, so a lost area, cave or entrance comes
        // back on the next run while the rest stays as it was.
        var areaId = await db.Features
            .Where(f => f.Name == "Platoul Demo")
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);
        if (areaId is null)
        {
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
            areaId = area.Id;
        }

        // Around Brașov / Piatra Craiului — plausible but fictional demo data.
        var bigDemoId = await db.Caves
            .Where(c => c.IdentificationCode == "DEMO-0001")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (bigDemoId is null)
        {
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
                parents: [new ParentSpec(areaId.Value, IsPrimary: true)], ct);
            bigDemoId = bigDemo.Id;
        }

        await EnsureEntranceAsync(db, writer, bigDemoId.Value, ownerUserId, naturalEntranceId,
            "Main entrance", MainEntranceLongitude, MainEntranceLatitude, (decimal)MainEntranceHeightM, isMain: true, ct);
        await EnsureEntranceAsync(db, writer, bigDemoId.Value, ownerUserId, naturalEntranceId,
            "Upper entrance", 25.4481, 45.5325, 1010m, isMain: false, ct);

        var protectedPitId = await db.Caves
            .Where(c => c.IdentificationCode == "DEMO-0002")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (protectedPitId is null)
        {
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
                parents: [new ParentSpec(areaId.Value, IsPrimary: true)], ct);
            protectedPitId = protectedPit.Id;
        }

        if (await EnsureEntranceAsync(db, writer, protectedPitId.Value, ownerUserId, naturalEntranceId,
                "Shaft", 25.2101, 45.5187, 1420m, isMain: true, ct))
        {
            // The protection flag was set at creation; restamp the subtree now that the
            // entrance exists.
            await writer.SetLocationProtectedAsync(protectedPitId.Value, true, ct);
        }

        return bigDemoId.Value;
    }

    /// <summary>
    /// An entrance by name under its cave, written only where the cave does not already have
    /// one of that name. Answers whether anything was written, for a caller with something to
    /// do only then.
    /// </summary>
    private static async Task<bool> EnsureEntranceAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid caveFeatureId, Guid ownerUserId,
        long entranceTypeId, string name, double lon, double lat, decimal altitude, bool isMain,
        CancellationToken ct)
    {
        if (await db.Features.AnyAsync(
                f => f.Kind == FeatureKind.CaveEntrance && f.Entrance!.CaveFeatureId == caveFeatureId && f.Name == name, ct))
        {
            return false;
        }

        await AddEntranceAsync(writer, caveFeatureId, ownerUserId, entranceTypeId, name, lon, lat, altitude, isMain, ct);
        return true;
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
        var sinkholeTypeId = await db.FeatureTypes.Where(t => t.Code == "sinkhole").Select(t => t.Id).SingleAsync(ct);
        var fractureTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync(ct);
        var associatedCaveKindId = await db.LinkKinds.Where(k => k.Code == "associated_cave").Select(k => k.Id).SingleAsync(ct);

        // Each row on its own guard — the sinkhole, its link to the cave, the fracture — so a
        // lost one comes back on the next run and the others are left as they are.
        var sinkholeId = await db.Features
            .Where(f => f.Name == "Dolina Demo")
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);
        if (sinkholeId is null)
        {
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
            sinkholeId = sinkhole.Id;
        }

        // The old hardcoded cave association is a typed, locating link now.
        if (!await db.FeatureLinks.AnyAsync(
                l => l.FromId == sinkholeId && l.ToId == demoCaveId && l.LinkKindId == associatedCaveKindId, ct))
        {
            db.FeatureLinks.Add(new FeatureLink
            {
                FromId = sinkholeId.Value,
                ToId = demoCaveId,
                LinkKindId = associatedCaveKindId,
            });
        }

        if (await db.Features.AnyAsync(f => f.Name == "Falia Demo", ct))
        {
            return;
        }

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
            // Per cave, so a lost one comes back on the next run and the rest stay as they are.
            var caveId = await db.Caves
                .Where(c => c.IdentificationCode == code)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (caveId is not null)
            {
                await EnsureEntranceAsync(db, writer, caveId.Value, ownerUserId, naturalEntranceId,
                    name + " entrance", lon, lat, altitude, isMain: true, ct);
                continue;
            }

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
        // Per trip rather than "any demo trip at all". A block that stops at the first sign of
        // itself never runs again on an installation the earlier version was run on, so a trip
        // added later would exist only on machines that had never seeded — and the section built
        // over it would read as empty there, which is indistinguishable from broken.
        var alreadySeeded = await db.TripLogs
            .Where(t => t.Title.StartsWith("Demo:"))
            .Select(t => t.Title)
            .ToListAsync(ct);

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
        var roleIds = await db.TripParticipantRoles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        long RoleId(string code) => roleIds.TryGetValue(code, out var id)
            ? id
            : throw new InvalidOperationException($"Participant role '{code}' is not seeded.");

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

            // The three that were done from the long camp, and dated inside its fortnight so the
            // camp reads as a fortnight of caving rather than as a folder somebody dropped
            // unrelated trips into. They are prefixed so the block that joins them to the camp can
            // find them by name and nothing else. Two of them carry figures, so the camp's totals
            // are numbers rather than zeroes; one is only visible to accounts, so the same camp
            // shows a signed-in reader a larger total than a visitor — which is the whole reason
            // the totals are shown with a caveat instead of as bare truth.
            (CampTripPrefix + "exploration push", "exploration", new DateOnly(2026, 7, 20),
                Visibility.Public, ActivityState.Published,
                new DateTimeOffset(2026, 7, 22, 18, 0, 0, TimeSpan.Zero)),
            (CampTripPrefix + "survey day", "survey", new DateOnly(2026, 7, 24),
                Visibility.Public, ActivityState.Published,
                new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.Zero)),
            (CampTripPrefix + "hydrology round", "science", new DateOnly(2026, 7, 28),
                Visibility.Authenticated, ActivityState.Published,
                new DateTimeOffset(2026, 7, 30, 18, 0, 0, TimeSpan.Zero)),
        };

        var index = 0;
        foreach (var (title, typeCode, date, visibility, state, publishedAt) in trips)
        {
            if (!tripTypeIds.TryGetValue(typeCode, out var tripTypeId))
            {
                throw new InvalidOperationException($"Trip type '{typeCode}' is not seeded.");
            }

            if (alreadySeeded.Contains(title))
            {
                // The counter still moves: which cave and which role a trip gets is read off it,
                // and a top-up that shifted them would give the new trips somebody else's pairing.
                index++;
                continue;
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
                // Where the camp's own trips went, sketched on the plateau the camp works. Only
                // those trips carry one: a sketch is optional on every trip, and a demo where
                // every trip had one would make a surface that ignores the empty case look right.
                Geom = title.StartsWith(CampTripPrefix, StringComparison.Ordinal)
                    ? new Point(25.42 + (index % 3 * 0.02), 45.51 + (index % 3 * 0.01)) { SRID = 4326 }
                    : null,
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
            // roster and not a single name — plus the jobs those two did besides turning up.
            // Two rows for one person on one trip is exactly what the roster's uniqueness
            // allows, and a demo where nobody holds two of them would show a narrowing back to
            // one job per person as no change at all. The extra jobs rotate two per trip so
            // every shipped role turns up somewhere in the demo data rather than only the two
            // the form has always offered.
            var proposer = caverIds[index % caverIds.Count];
            var attendee = caverIds[(index + 1) % caverIds.Count];
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = proposer,
                RoleId = RoleId(TripParticipantRoleSeeds.ProposerCode),
            });
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = attendee,
                RoleId = RoleId(TripParticipantRoleSeeds.ParticipantCode),
            });
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = attendee,
                RoleId = RoleId(ExtraParticipantRoles[(index * 2) % ExtraParticipantRoles.Length]),
            });
            // One person who was not underground for the same span as the trip, with the reason
            // said in words. Most rows leave the times empty, which is the ordinary case and
            // means the trip's own times stand for them; a demo where every row carried times
            // would make a surface that ignores the empty case look correct.
            db.TripLogParticipants.Add(new TripLogParticipant
            {
                TripLogId = trip.Id,
                CaverId = caverIds[(index + 2) % caverIds.Count],
                RoleId = RoleId(ExtraParticipantRoles[((index * 2) + 1) % ExtraParticipantRoles.Length]),
                EntryTime = new TimeOnly(9, 30),
                ExitTime = new TimeOnly(13, 15),
                Note = "Turned back at the pitch head and waited at the entrance series.",
            });

            // Their numbers in the party, from the writer every roster write goes through and in
            // the order the rows above name them — so the demonstration data is numbered by the
            // rule a real trip is, not by the fallback for a trip nobody numbered.
            await TripPartyNumbers.AssignAsync(
                db, trip.Id, [proposer, attendee, caverIds[(index + 2) % caverIds.Count]], ct);

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
    /// The long camp, named once: the roster block finds it by this and nothing else.
    /// </summary>
    private const string FortnightCampName = "Demo: Bihor summer camp";

    /// <summary>
    /// The one trip still being planned, named once so the block that seeds who was asked on it
    /// finds it by name rather than by which state it happens to be in.
    /// </summary>
    private const string PlannedTripTitle = "Demo: training weekend";

    /// <summary>
    /// What the trips done from the long camp are called, so the block that joins them to it finds
    /// them by name rather than by guessing from their dates.
    /// </summary>
    private const string CampTripPrefix = "Demo: camp ";

    /// <summary>
    /// A handful of camps spread across the lifecycle, so a page listing them shows every reading.
    /// </summary>
    private static async Task SeedExpeditionsAsync(
        SilexGisDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        // Fixed dates rather than relative to now, for the reason the trips above give: a demo
        // that drifts is a demo whose screenshots stop matching it.
        //
        // The lifecycle spread is the point of the block. A camp exists long before it happens,
        // so the demo shows one that has been written up and announced, one going ahead with its
        // dates settled, one still somebody's idea, and one put back — the four readings that
        // look different on a page, without anybody having to drive the transitions to see them.
        // A single-day camp is here too, holding no end date at all, because that is the row
        // every reader of the date range is written against.
        var camps = new[]
        {
            (FortnightCampName, "A fortnight on the plateau: exploration, survey and rigging.",
                new DateOnly(2026, 7, 18), (DateOnly?)new DateOnly(2026, 8, 1), Visibility.Public,
                ActivityState.Published,
                (DateTimeOffset?)new DateTimeOffset(2026, 8, 10, 18, 0, 0, TimeSpan.Zero)),
            ("Demo: autumn survey camp", "Finishing the survey of the lower series.",
                new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 18), Visibility.Authenticated,
                ActivityState.Confirmed, null),
            ("Demo: winter recce", "One day looking at the entrances above the valley.",
                new DateOnly(2026, 12, 5), null, Visibility.CavingGroup, ActivityState.Proposed, null),
            ("Demo: spring camp (postponed)", "Put back until the access permit is renewed.",
                new DateOnly(2027, 4, 3), new DateOnly(2027, 4, 12), Visibility.Public,
                ActivityState.Delayed, null),
        };

        // Roughly the plateau the demo caves sit on. A working area is drawn on the plan and
        // stays what it was drawn as — it is not derived from where the trips ended up.
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var workingArea = factory.CreatePolygon(
        [
            new Coordinate(25.40, 45.50),
            new Coordinate(25.50, 45.50),
            new Coordinate(25.50, 45.56),
            new Coordinate(25.40, 45.56),
            new Coordinate(25.40, 45.50),
        ]);

        var index = 0;
        foreach (var (name, description, start, end, visibility, state, publishedAt) in camps)
        {
            // Per camp, so a lost one comes back on the next run and the rest stay as they are.
            if (await db.Expeditions.AnyAsync(x => x.Name == name, ct))
            {
                index++;
                continue;
            }

            var camp = new Expedition
            {
                Name = name,
                Description = description,
                StartDate = start,
                EndDate = DayRange.EndForStorage(start, end),
                // Only the first carries one, so a surface that draws the area has something to
                // draw and one that must cope with its absence has that too.
                Geom = index == 0 ? workingArea : null,
                OwnerUserId = ownerUserId,
                Visibility = visibility,
                State = state,
                PublishedAt = publishedAt,
            };
            db.Expeditions.Add(camp);
            index++;
        }
    }

    /// <summary>
    /// Which trips the fortnight camp gathered.
    /// </summary>
    /// <remarks>
    /// Without this the demo's camps hold no trips at all, and every surface built over the
    /// membership — the trips list, the totals, the map's sketches and the entrances of the caves
    /// those trips name — reads as empty. An empty answer and a broken one look the same on a
    /// page, so a demo that only ever shows the empty one proves nothing about either.
    /// <para>
    /// Not every demo trip: the ones left out include one dated the day the camp began, which is
    /// what shows that a camp's trips are the ones joined to it and not simply the ones whose
    /// dates happen to fall inside it. One of the three joined is visible to accounts only, so a
    /// visitor and a signed-in reader get different totals for the same camp — the difference the
    /// totals are captioned about.
    /// </para>
    /// <para>
    /// Guarded per membership rather than on the camps' absence or on any membership at all, so a
    /// database seeded before this block existed picks the rows up on the next run, and one that
    /// lost a membership gets it back.
    /// </para>
    /// </remarks>
    private static async Task SeedExpeditionTripsAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var camp = await db.Expeditions.FirstOrDefaultAsync(x => x.Name == FortnightCampName, ct);
        if (camp is null)
        {
            return;
        }

        var tripIds = await db.TripLogs
            .Where(t => t.Title.StartsWith(CampTripPrefix))
            .OrderBy(t => t.TripDate)
            .Select(t => t.Id)
            .ToListAsync(ct);

        // A trip is in at most one camp, and the database is what holds that rule. Skipping the
        // ones already placed keeps a re-seed from being refused by the unique index.
        var alreadyPlaced = await db.ExpeditionTrips
            .Where(m => tripIds.Contains(m.TripLogId))
            .Select(m => m.TripLogId)
            .ToListAsync(ct);

        var joinedAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        foreach (var tripId in tripIds.Except(alreadyPlaced))
        {
            db.ExpeditionTrips.Add(new ExpeditionTrip
            {
                ExpeditionId = camp.Id,
                TripLogId = tripId,
                JoinedAt = joinedAt,
            });
        }
    }

    /// <summary>
    /// Who was asked on the trip still being planned, and what each of them has said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Against the one trip that is still a draft, because that is the only one where the question
    /// is live: on a trip already run, who was asked is history and who was there is the roster.
    /// It is given room for three against five yeses, so the two beyond the limit are waiting and
    /// a surface that ignored the limit shows five people on a trip for three.
    /// </para>
    /// <para>
    /// Every answer in the vocabulary appears, including somebody asked who has not replied, so a
    /// reading that quietly counted silence as one of the real answers has a row to get wrong. One
    /// person answered without ever being asked — the ordinary case of somebody seeing a trip
    /// their club is running — and one was picked out of the order by whoever runs the trip, from
    /// behind the limit, so the pick displaces somebody and the order underneath it stays visible.
    /// </para>
    /// <para>
    /// The stamps are minutes apart and fixed rather than relative to now, because the order they
    /// give is what decides who is on the trip: a demo whose queue came out differently on
    /// different machines would not be showing the feature at all. The person who changed their
    /// mind carries the later stamp, which is what puts them behind everybody who answered in
    /// between.
    /// </para>
    /// <para>
    /// Guarded per row rather than on any one of them, so a database seeded before this block
    /// existed picks the answers up on the next run, and so an answer added here later reaches an
    /// installation that already holds the rest.
    /// </para>
    /// </remarks>
    private static async Task SeedTripInvitationsAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var trip = await db.TripLogs.FirstOrDefaultAsync(t => t.Title == PlannedTripTitle, ct);
        if (trip is null)
        {
            return;
        }

        // Set where nothing has been said about it rather than unconditionally, so an installation
        // whose own limit was edited on the demo trip keeps what it chose.
        trip.MaxParticipants ??= 3;

        // Four more people than the trips themselves use, added here and guarded by name, because
        // a list of who is considering a trip is only worth drawing when there are more people on
        // it than there is room for — and a demo where the limit was never reached would show a
        // surface that ignored the limit as working perfectly. They are ordinary directory
        // entries with no account, which is what most of a club's roster is.
        var caverIds = new List<Guid>();
        foreach (var fullName in new[]
        {
            "Ana Demo", "Bogdan Demo", "Cristina Demo", "Dan Demo",
            "Elena Demo", "Florin Demo", "Gabriela Demo", "Horia Demo",
        })
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

        var asked = new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero);
        var answered = new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero);

        // Caver, what they said, how many minutes after the first answer they said it, whether
        // anybody asked them, and whether they were picked.
        var answers = new (int Caver, TripInvitationResponse Response, int Minutes, bool Invited, bool Picked)[]
        {
            (0, TripInvitationResponse.Yes, 0, true, false),
            (1, TripInvitationResponse.Yes, 20, true, false),
            (2, TripInvitationResponse.No, 35, true, false),
            (3, TripInvitationResponse.Yes, 50, true, true),

            // Asked and silent, which is a state somebody reads and acts on rather than an absence.
            (4, TripInvitationResponse.Pending, -1, true, false),

            // Said maybe first and yes much later, so their place in the queue is where the yes
            // put them and not where the maybe did.
            (5, TripInvitationResponse.Yes, 400, true, false),

            // Nobody asked this one; they saw the trip and said they were coming.
            (6, TripInvitationResponse.Yes, 90, false, false),

            (7, TripInvitationResponse.Maybe, 120, true, false),
        };

        foreach (var (caver, response, minutes, invited, picked) in answers)
        {
            var caverId = caverIds[caver];
            if (await db.TripInvitations.AnyAsync(x => x.TripLogId == trip.Id && x.CaverId == caverId, ct))
            {
                continue;
            }

            db.TripInvitations.Add(new TripInvitation
            {
                TripLogId = trip.Id,
                CaverId = caverId,
                Response = response,
                InvitedAt = invited ? asked : null,
                // Stamped only where there is an answer to stamp: somebody who has not replied has
                // not replied at a time, and a date here would put them in the queue.
                RespondedAt = minutes < 0 ? null : answered.AddMinutes(minutes),
                SelectedAt = picked ? answered.AddMinutes(600) : null,
                Note = response == TripInvitationResponse.Maybe
                    ? "Only if we are back before dark."
                    : null,
            });
        }
    }

    /// <summary>
    /// Who was at the fortnight camp, and for which days.
    /// </summary>
    /// <remarks>
    /// Against the long camp because that is the one a camp's roster reads as anything: a single-day
    /// recce with a presence list is a list of everybody who turned up, which shows none of what
    /// the table is for.
    /// <para>
    /// Deliberately not the people on the demo trips. A camp's roster is not derived from its
    /// trips — the cook and whoever kept the base camp went underground on none of it — and a demo
    /// where the two lists matched would make a surface that quietly computed one from the other
    /// look correct. Four people in six rows, one of them holding two roles over overlapping days
    /// and one leaving and coming back, so anything counting rows instead of people reports six
    /// where four were there.
    /// </para>
    /// <para>
    /// Guarded per row rather than on the camps' absence or on any row at all, so a database seeded
    /// before this block existed picks the rows up on the next run, and one that lost a stay gets
    /// it back.
    /// </para>
    /// </remarks>
    private static async Task SeedExpeditionRosterAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var camp = await db.Expeditions.FirstOrDefaultAsync(x => x.Name == FortnightCampName, ct);
        if (camp is null)
        {
            return;
        }

        var caverIds = await db.Cavers
            .Where(c => c.FullName.EndsWith(" Demo"))
            .OrderBy(c => c.FullName)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (caverIds.Count < 4)
        {
            return;
        }

        var roleIds = await db.ExpeditionRosterRoles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);
        long RoleId(string code) => roleIds.TryGetValue(code, out var id)
            ? id
            : throw new InvalidOperationException($"Camp-roster role '{code}' is not seeded.");

        var start = camp.StartDate;
        var end = camp.EndDate ?? camp.StartDate;

        // A stay is one person, one role, from one day: that triple is what tells a row that was
        // lost from one that was never there.
        var held = await db.ExpeditionRoster
            .Where(r => r.ExpeditionId == camp.Id)
            .Select(r => new { r.CaverId, r.RoleId, r.FromDate })
            .ToListAsync(ct);

        void Add(Guid caverId, string role, DateOnly from, DateOnly? to, string? note = null)
        {
            var roleId = RoleId(role);
            if (held.Any(h => h.CaverId == caverId && h.RoleId == roleId && h.FromDate == from))
            {
                return;
            }

            db.ExpeditionRoster.Add(new ExpeditionRosterEntry
            {
                ExpeditionId = camp.Id,
                CaverId = caverId,
                RoleId = roleId,
                FromDate = from,
                ToDate = DayRange.OpenEndForStorage(from, to),
                Note = note,
            });
        }

        // One person, two roles, over spans that overlap: nothing forbids it, and it is what a
        // count of rows gets wrong.
        Add(caverIds[0], ExpeditionRosterRoleSeeds.MemberCode, start, end);
        Add(caverIds[0], "cook", start, start.AddDays(7));

        // Left in the middle of the fortnight and came back for the last days — two rows for one
        // person in one role, which is an ordinary record and not a duplicate.
        Add(caverIds[1], ExpeditionRosterRoleSeeds.MemberCode, start, start.AddDays(4),
            "Went back for the mid-camp resupply.");
        Add(caverIds[1], ExpeditionRosterRoleSeeds.MemberCode, end.AddDays(-4), end);

        // There for one day, which stores that day as its last as well as its first: on a roster
        // an absent last day is somebody who has not left.
        Add(caverIds[2], "base_camp", start.AddDays(2), start.AddDays(2),
            "Drove the food up and stayed the day.");

        // A fourth person, so the six rows are four people and the gap between the two numbers is
        // large enough to be obvious on a surface that counted the wrong one.
        Add(caverIds[3], "driver", start, start.AddDays(1), "Brought the gear up and went home.");
    }

    /// <summary>
    /// A couple of saved views, so the map has somewhere to be taken.
    /// </summary>
    private static async Task SeedMapViewsAsync(
        SilexGisDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        var views = new[]
        {
            ("Demo: Platoul Demo", "The demo karst area, at plateau scale.", 25.44, 45.525, 13.0),
            ("Demo: Bihor caves", "The Bihor demo caves.", 22.57, 46.535, 12.0),
        };

        foreach (var (name, description, lon, lat, zoom) in views)
        {
            // Per view, so a lost one comes back on the next run and the other stays as it is.
            if (await db.MapViews.AnyAsync(v => v.Name == name, ct))
            {
                continue;
            }

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

    /// <summary>
    /// Two ways on that the camp's trips found, so the camp's leads board has leads to show — one
    /// still going and one that closed, since grouping by exactly that is what the board does.
    /// </summary>
    /// <remarks>
    /// A lead reaches the board by being named by a member trip, so each is named by one of the
    /// camp's trips under the objective role. Guarded per lead; the naming helper says nothing
    /// twice, so the naming is its own guard.
    /// </remarks>
    private static async Task SeedContinuationsAsync(
        SilexGisDbContext db, FeatureWriteService writer, Guid ownerUserId, CancellationToken ct)
    {
        var typeId = await db.FeatureTypes
            .Where(t => t.Code == FeatureTypeSeeds.Continuation)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);
        var campTripIds = await db.TripLogs
            .Where(t => t.Title.StartsWith(CampTripPrefix))
            .OrderBy(t => t.TripDate)
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (typeId is null || campTripIds.Count == 0)
        {
            return;
        }

        var leads = new[]
        {
            ("Demo: draughting rift", "A rift at the end of the upper series with a strong draught.", 25.4478, 45.5321,
                """{"state":"continues","grade":"A","note":"Needs a hammer and a short ladder; the draught is strong."}"""),
            ("Demo: choked crawl", "A low crawl off the main gallery that closed down in gravel.", 25.4466, 45.5309,
                """{"state":"dead-end","grade":"D","note":"Dug for two hours; gravel all the way."}"""),
        };

        var index = 0;
        foreach (var (name, description, lon, lat, properties) in leads)
        {
            var leadId = await db.Features
                .Where(f => f.Name == name)
                .Select(f => (Guid?)f.Id)
                .FirstOrDefaultAsync(ct);
            if (leadId is null)
            {
                var lead = await writer.CreateGenericAsync(
                    new Feature
                    {
                        Name = name,
                        Description = description,
                        FeatureTypeId = typeId.Value,
                        Geom = new Point(lon, lat) { SRID = 4326 },
                        Properties = properties,
                        OwnerUserId = ownerUserId,
                        Visibility = Visibility.Public,
                    },
                    parents: [], ct);
                leadId = lead.Id;
            }

            if (!await TripRoleLinks.NameFeatureAsync(
                    db, campTripIds[index % campTripIds.Count], leadId.Value, "trip-objective", ownerUserId, ct))
            {
                throw new InvalidOperationException("Relation type 'trip-objective' is not seeded.");
            }

            index++;
        }
    }

    /// <summary>
    /// Club dates, so the calendar and the events surface show every reading they have: a date
    /// on its own, a series of them, and one people have answered.
    /// </summary>
    /// <remarks>
    /// Fixed dates, for the reason the trips give. Guarded per row — per occurrence of the series
    /// and per answer — so a database seeded before this block existed picks them up on the next
    /// run and one that lost a row gets it back. A lost occurrence rejoins its series, because the
    /// series identity is read off the occurrences still present rather than minted afresh.
    /// </remarks>
    private static async Task SeedEventsAsync(SilexGisDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        await EnsureEventAsync(db, ownerUserId, new DemoEvent(
            "Demo: autumn general meeting", EventKind.ClubMeeting, new DateOnly(2026, 10, 20), new TimeOnly(19, 0),
            "The club room", "Accounts, the winter programme and the new rope.", ActivityState.Confirmed), null, null, ct);

        // Six Tuesday evenings of rope training, written out as the events route writes a series.
        const string trainingTitle = "Demo: Tuesday rope training";
        var plan = EventRecurrence.Plan(new DateOnly(2026, 9, 1), EventRecurrenceFrequency.Weekly, 6, null);
        if (!plan.Refused)
        {
            var seriesId = await db.Events
                .Where(e => e.Title == trainingTitle && e.SeriesId != null)
                .Select(e => e.SeriesId)
                .FirstOrDefaultAsync(ct) ?? Guid.CreateVersion7();
            foreach (var day in plan.Days)
            {
                await EnsureEventAsync(db, ownerUserId, new DemoEvent(
                    trainingTitle, EventKind.Training, day, new TimeOnly(18, 30),
                    "The climbing wall", "Single-rope technique; bring your own harness.", ActivityState.Planned),
                    seriesId, "Every Tuesday, six weeks", ct);
            }
        }

        // One people have answered, with every answer in the vocabulary, as on the planned trip.
        var gearCheck = await EnsureEventAsync(db, ownerUserId, new DemoEvent(
            "Demo: gear check evening", EventKind.GearCheck, new DateOnly(2026, 9, 24), new TimeOnly(18, 0),
            "The store", "Every rope and every harness, logged and inspected.", ActivityState.Planned), null, null, ct);
        var caverIds = await db.Cavers
            .Where(c => c.FullName.EndsWith(" Demo"))
            .OrderBy(c => c.FullName)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (caverIds.Count < 4)
        {
            return;
        }

        var asked = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var answers = new (int Caver, TripInvitationResponse Response, int Minutes)[]
        {
            (0, TripInvitationResponse.Yes, 0),
            (1, TripInvitationResponse.Yes, 30),
            (2, TripInvitationResponse.No, 60),
            (3, TripInvitationResponse.Maybe, 90),
        };
        foreach (var (caver, response, minutes) in answers)
        {
            var caverId = caverIds[caver];
            if (await db.TripInvitations.AnyAsync(x => x.EventId == gearCheck.Id && x.CaverId == caverId, ct))
            {
                continue;
            }

            db.TripInvitations.Add(new TripInvitation
            {
                EventId = gearCheck.Id,
                CaverId = caverId,
                Response = response,
                InvitedAt = asked,
                RespondedAt = asked.AddHours(26).AddMinutes(minutes),
                Note = response == TripInvitationResponse.Maybe ? "If the car is back from the garage." : null,
            });
        }
    }

    private sealed record DemoEvent(
        string Title, EventKind Kind, DateOnly Day, TimeOnly StartTime, string Place, string Description, ActivityState State);

    /// <summary>An event by title and day, written only where the pair is not already there.</summary>
    private static async Task<Event> EnsureEventAsync(
        SilexGisDbContext db, Guid ownerUserId, DemoEvent demo, Guid? seriesId, string? seriesRule, CancellationToken ct)
    {
        var existing = await db.Events.FirstOrDefaultAsync(e => e.Title == demo.Title && e.StartDate == demo.Day, ct);
        if (existing is not null)
        {
            return existing;
        }

        var row = new Event
        {
            Title = demo.Title,
            Description = demo.Description,
            Kind = demo.Kind,
            StartDate = demo.Day,
            StartTime = demo.StartTime,
            Place = demo.Place,
            OwnerUserId = ownerUserId,
            Visibility = Visibility.Authenticated,
            State = demo.State,
            SeriesId = seriesId,
            SeriesRule = seriesRule,
        };
        db.Events.Add(row);
        return row;
    }

    /// <summary>
    /// The one trip of the demonstration data that was followed underground, named once: every
    /// piece of the block below hangs off the trip found by this.
    /// </summary>
    /// <remarks>
    /// Not under the camp's prefix, so the camp does not gather it; and not in the list of trips
    /// further up, whose position decides which cave and which people a trip gets — this one names
    /// its own.
    /// </remarks>
    public const string TrackedTripTitle = "Demo: tracked descent";

    /// <summary>The day the tracked trip was done. Fixed, like every other date here, and in the past.</summary>
    private static readonly DateOnly TrackedTripDate = new(2026, 8, 29);

    /// <summary>
    /// A trip that was followed from the surface, start to finish: a survey of the demonstration
    /// cave, a watch that was started and closed on it, and the reports that came out in between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it a fresh demonstration installation has a tracking tab on every trip and nothing
    /// in any of them — no replay to play, no log to read, no movie to make — and those surfaces
    /// read as unfinished rather than as empty.
    /// </para>
    /// <para>
    /// <b>The survey arrives the way an uploaded one does</b>: its bytes are stored, the model row
    /// names them, and the reading that fills in its stations is queued for the worker every
    /// upload waits on. Nothing here writes a station. The reports can still be written first,
    /// because a report holds a station by its name and that name is the one in the file.
    /// </para>
    /// <para>
    /// <b>The watch is written closed.</b> Starting one is something a person does while a party
    /// is underground, with the clock; a seeded trip is over, so its watch is laid down as the
    /// record of one — when it was started, when it was closed — and never passes through being
    /// live. It is on the unprotected public cave, so its positions are ones every account may be
    /// told, and a reader of the demonstration sees the replay rather than a list of withheld
    /// places.
    /// </para>
    /// <para>
    /// <b>No link is published for it.</b> Publishing puts a page in front of people with no
    /// account, and that is a person's decision about a particular trip. The demonstration data
    /// is also what a public showcase installation runs on, where a seeded link would be a page
    /// nobody chose to put on the internet.
    /// </para>
    /// <para>
    /// Each piece is looked for before it is written — the model, the declared place, the trip,
    /// the team, the watch, and every report by whom it is about, what kind it is and its moment —
    /// so a second run writes nothing and a lost piece comes back by itself. One limit, stated
    /// rather than papered over: a survey model that was lost comes back as a new model, and
    /// nothing re-points a watch that still exists at it, because a watch is never moved to
    /// another model on its own behalf — it then says, truthfully, that its model is gone.
    /// </para>
    /// </remarks>
    private static async Task SeedTrackedTripAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid ownerUserId,
        Guid demoCaveId,
        CancellationToken ct)
    {
        // The two people who went. They are seeded with the trips above, which always run first;
        // without them there is nobody to follow and the block has nothing honest to write.
        var ana = await db.Cavers.Where(c => c.FullName == "Ana Demo").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var bogdan = await db.Cavers.Where(c => c.FullName == "Bogdan Demo").Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (ana is null || bogdan is null)
        {
            return;
        }

        var modelId = await db.SurveyModels
            .Where(m => m.CaveFeatureId == demoCaveId && m.Name == DemoSurvey.ModelName)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        modelId ??= await CreateDemoSurveyAsync(db, documents, fileStore, ownerUserId, demoCaveId, ct);

        // What the cave says a reported depth means. A depth is the commonest thing to come out
        // of a cave by word of mouth, and a place declared for one is what turns "at minus sixty"
        // into a station without anybody measuring; one row is enough to show both the report
        // placed by it and the name people use for the place.
        if (!await db.CaveDepthPlaces.AnyAsync(
                x => x.CaveFeatureId == demoCaveId && x.DepthM == DemoSurvey.PitFootDepthM, ct))
        {
            db.CaveDepthPlaces.Add(new CaveDepthPlace
            {
                CaveFeatureId = demoCaveId,
                DepthM = DemoSurvey.PitFootDepthM,
                ViewerStationName = DemoSurvey.PitFoot,
                PlaceLabel = "Foot of the pit",
            });
        }

        var tripId = await db.TripLogs
            .Where(t => t.Title == TrackedTripTitle)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (tripId is null)
        {
            tripId = await CreateTrackedTripAsync(db, ownerUserId, demoCaveId, ana.Value, bogdan.Value, ct);
        }

        const string teamTitle = "Pit team";
        var teamId = await db.TripTeams
            .Where(t => t.TripLogId == tripId && t.Title == teamTitle)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (teamId is null)
        {
            var team = new TripTeam { TripLogId = tripId.Value, Title = teamTitle };
            db.TripTeams.Add(team);
            teamId = team.Id;
        }

        DateTimeOffset At(int hour, int minute) =>
            new(TrackedTripDate.Year, TrackedTripDate.Month, TrackedTripDate.Day, hour, minute, 0, TimeSpan.Zero);

        // Started before anybody went in and closed after the last person was out, as a watch is.
        var watch = await db.TripTrackings.FirstOrDefaultAsync(t => t.TripLogId == tripId, ct);
        if (watch is null)
        {
            watch = new TripTracking
            {
                TripLogId = tripId.Value,
                State = TripTrackingState.Closed,
                SurveyModelId = modelId,
                // The cave the model belongs to, kept beside it as choosing a model keeps it:
                // it is what decides who may be told a place, and it outlives the model.
                CaveFeatureId = demoCaveId,
                ArmedAt = At(7, 0),
                FirstArmedAt = At(7, 0),
                ClosedAt = At(10, 20),
            };
            db.TripTrackings.Add(watch);
        }

        // Reports are written against the model the watch is on, as the product writes them. A
        // watch that outlived its model keeps pointing at the one that is gone; a report put back
        // beside it names that same model, so the log stays one statement about one survey.
        var reportedOn = watch.SurveyModelId ?? modelId;

        // About three hours, in the order word would have come out: both in, both at the end of
        // the gallery, both at the head of the pit, one down to its foot — reported as a depth,
        // which the declared place turns into a station — a word from the one who stayed, and
        // out one after the other. A station is said only by a report that places somebody; the
        // note says nothing about where, because a note is read by everybody who reads the trip.
        var reports = new (Guid Caver, TripPositionEventKind Kind, string? Station, decimal? DepthM, string? Note, DateTimeOffset At)[]
        {
            (ana.Value, TripPositionEventKind.Entered, null, null, null, At(7, 10)),
            (bogdan.Value, TripPositionEventKind.Entered, null, null, null, At(7, 10)),
            (ana.Value, TripPositionEventKind.AtStation, DemoSurvey.GalleryEnd, null, null, At(7, 40)),
            (bogdan.Value, TripPositionEventKind.AtStation, DemoSurvey.GalleryEnd, null, null, At(7, 40)),
            (ana.Value, TripPositionEventKind.AtStation, DemoSurvey.PitHead, null, null, At(8, 15)),
            (bogdan.Value, TripPositionEventKind.AtStation, DemoSurvey.PitHead, null, null, At(8, 15)),
            (ana.Value, TripPositionEventKind.AtDepth, DemoSurvey.PitFoot, DemoSurvey.PitFootDepthM, null, At(8, 50)),
            (bogdan.Value, TripPositionEventKind.Note, null, null, "Rope checked and holding. Waiting until the way back up is clear.", At(9, 0)),
            (bogdan.Value, TripPositionEventKind.Exited, null, null, null, At(10, 0)),
            (ana.Value, TripPositionEventKind.Exited, null, null, null, At(10, 15)),
        };

        var written = await db.TripPositionEvents
            .Where(e => e.TripLogId == tripId)
            .Select(e => new { e.CaverId, e.Kind, e.RecordedAt })
            .ToListAsync(ct);
        foreach (var report in reports)
        {
            if (written.Any(e => e.CaverId == report.Caver && e.Kind == report.Kind && e.RecordedAt == report.At))
            {
                continue;
            }

            var placed = report.Station is not null;
            db.TripPositionEvents.Add(new TripPositionEvent
            {
                TripLogId = tripId.Value,
                CaverId = report.Caver,
                TeamId = teamId,
                Kind = report.Kind,
                // A report that places nobody names no model and no cave, exactly as one recorded
                // by hand does: the pair is there to say whose stations a name belongs to and who
                // may be told it, and there is no name here to say either about.
                SurveyModelId = placed ? reportedOn : null,
                CaveFeatureId = placed ? demoCaveId : null,
                ViewerStationName = report.Station,
                DepthEnteredM = report.DepthM,
                Note = report.Note,
                RecordedAt = report.At,
                RecordedByUserId = ownerUserId,
            });
        }
    }

    /// <summary>
    /// The invented survey as a survey model of the demonstration cave, stored and queued exactly
    /// as an upload stores and queues one.
    /// </summary>
    /// <remarks>
    /// Saved here rather than left to the caller: whether the model becomes the one the cave is
    /// represented by is decided against the rows already stored, and the model and the job that
    /// reads it have to land together or not at all.
    /// </remarks>
    private static async Task<Guid> CreateDemoSurveyAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid ownerUserId,
        Guid demoCaveId,
        CancellationToken ct)
    {
        var bytes = DemoSurvey.Build();
        var storagePath = await StoreAsync(fileStore, bytes, ".3d", ct);
        var stored = documents.Create(
            new StoredContent(
                storagePath,
                DemoSurvey.FileName,
                "application/octet-stream",
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                FileKind.Survey),
            DemoSurvey.ModelName,
            ownerUserId,
            ownerUserId).File;

        var model = new SurveyModel
        {
            CaveFeatureId = demoCaveId,
            Name = DemoSurvey.ModelName,
            FileId = stored.Id,
            Format = SurveyModelFormat.Survex3d,
            Description = "An invented survey — no real cave was measured to make it.",

            // What an uploader answers for a file in plain metres about its own zero: where that
            // zero is. Here it is the survey's first station, the way in, so it is the cave's
            // main entrance. Without it the reading refuses the file — it cannot say where a
            // single station is — and the model would sit on the cave's page as one that failed.
            Anchor = new Point(MainEntranceLongitude, MainEntranceLatitude) { SRID = 4326 },
            AnchorHeightM = MainEntranceHeightM,
        };
        db.SurveyModels.Add(model);

        // Its reading is queued for the worker, not done here: the stations, the walls and the
        // measurements a survey is read into come from the one reader, on the one path, so the
        // demonstration model is in every respect a model somebody uploaded.
        SurveyModelReading.QueueFirst(db, model, requestedBy: null);
        await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);
        await db.SaveChangesAsync(ct);
        return model.Id;
    }

    /// <summary>The tracked trip itself: its row, who was on it, their numbers, and where it went.</summary>
    private static async Task<Guid> CreateTrackedTripAsync(
        SilexGisDbContext db, Guid ownerUserId, Guid demoCaveId, Guid ana, Guid bogdan, CancellationToken ct)
    {
        var tripTypeId = await db.TripTypes.Where(t => t.Code == "exploration").Select(t => (long?)t.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Trip type 'exploration' is not seeded.");
        var roleIds = await db.TripParticipantRoles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        var trip = new TripLog
        {
            Title = TrackedTripTitle,
            TripTypeId = tripTypeId,
            TripDate = TrackedTripDate,
            Description = "Demonstration trip log: a descent that was followed from the surface.",
            EntryTime = new TimeOnly(7, 10),
            ExitTime = new TimeOnly(10, 15),
            DepthReachedM = DemoSurvey.PitFootDepthM,
            OwnerUserId = ownerUserId,
            // For accounts, not for visitors: the trip adds nothing to what somebody without an
            // account can reach on an installation running the demonstration data.
            Visibility = Visibility.Authenticated,
            State = ActivityState.Published,
            PublishedAt = new DateTimeOffset(2026, 8, 31, 18, 0, 0, TimeSpan.Zero),
        };
        db.TripLogs.Add(trip);

        db.TripLogParticipants.Add(new TripLogParticipant
        {
            TripLogId = trip.Id,
            CaverId = ana,
            RoleId = roleIds[TripParticipantRoleSeeds.ProposerCode],
        });
        db.TripLogParticipants.Add(new TripLogParticipant
        {
            TripLogId = trip.Id,
            CaverId = bogdan,
            RoleId = roleIds[TripParticipantRoleSeeds.ParticipantCode],
        });

        // Their numbers in the party, from the writer every roster write goes through.
        await TripPartyNumbers.AssignAsync(db, trip.Id, [ana, bogdan], ct);

        if (!await TripRoleLinks.NameFeatureAsync(db, trip.Id, demoCaveId, "trip-visited", ownerUserId, ct))
        {
            throw new InvalidOperationException("Relation type 'trip-visited' is not seeded.");
        }

        return trip.Id;
    }
}
