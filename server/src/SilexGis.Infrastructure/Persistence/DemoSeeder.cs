// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Demo dataset for local exploration and E2E tests (`dotnet run -- seed-demo`).
/// Owned by the given user; idempotent by identification code. Routes every feature
/// write through <see cref="FeatureWriteService"/> — the seeder is ordinary write-path
/// code, not a bypass.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, Guid ownerUserId, CancellationToken ct = default)
    {
        var writer = new FeatureWriteService(db, new JsonSchemaFeaturePropertiesValidator(), new AnonymousCurrentUser());

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
