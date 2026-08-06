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
}
