// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Demo dataset for local exploration and E2E tests (`dotnet run -- seed-demo`,
/// 06-deployment.md §4). Owned by the given user; idempotent by identification code.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, Guid ownerUserId, CancellationToken ct = default)
    {
        if (await db.Caves.AnyAsync(c => c.IdentificationCode == "DEMO-0001", ct))
        {
            return;
        }

        var caveTypeId = await db.CaveTypes.Where(t => t.Code == "cave").Select(t => t.Id).SingleAsync(ct);
        var pitTypeId = await db.CaveTypes.Where(t => t.Code == "pit").Select(t => t.Id).SingleAsync(ct);
        var naturalEntranceId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync(ct);
        var limestoneId = await db.RockTypes.Where(t => t.Code == "limestone").Select(t => t.Id).SingleAsync(ct);

        // Around Brașov / Piatra Craiului — plausible but fictional demo data.
        var demoData = new[]
        {
            new
            {
                Cave = new Cave
                {
                    Name = "Peștera Demo Mare",
                    OtherToponyms = "Big Demo Cave",
                    IdentificationCode = "DEMO-0001",
                    CaveTypeId = caveTypeId,
                    RockTypeId = (long?)limestoneId,
                    Region = "Brașov",
                    HydrographicBasin = "Olt",
                    Valley = "Valea Demo",
                    Description = "Demonstration cave with two entrances and public visibility.",
                    SurveyedLength = 1234.5m,
                    Depth = 87.2m,
                    Altitude = 950m,
                    ExplorationStatus = ExplorationStatus.Ongoing,
                    OwnerUserId = ownerUserId,
                    Visibility = Visibility.Public,
                },
                Entrances = new[]
                {
                    (Name: "Main entrance", Lon: 25.4472, Lat: 45.5312, Alt: 952m, IsMain: true),
                    (Name: "Upper entrance", Lon: 25.4481, Lat: 45.5325, Alt: 1010m, IsMain: false),
                },
            },
            new
            {
                Cave = new Cave
                {
                    Name = "Avenul Demo Protejat",
                    IdentificationCode = "DEMO-0002",
                    CaveTypeId = pitTypeId,
                    RockTypeId = (long?)limestoneId,
                    Region = "Brașov",
                    Description = "Protected demo pit — exact location restricted (bat colony).",
                    Depth = 154m,
                    ClosestAddress = "Forest road 12, km 3 (redacted for non-members)",
                    LocationProtected = true,
                    OwnerUserId = ownerUserId,
                    Visibility = Visibility.Authenticated,
                },
                Entrances = new[] { (Name: "Shaft", Lon: 25.2101, Lat: 45.5187, Alt: 1420m, IsMain: true) },
            },
        };

        foreach (var item in demoData)
        {
            db.Caves.Add(item.Cave);
            foreach (var e in item.Entrances)
            {
                db.CaveEntrances.Add(new CaveEntrance
                {
                    CaveId = item.Cave.Id,
                    Name = e.Name,
                    EntranceTypeId = naturalEntranceId,
                    IsMain = e.IsMain,
                    Geom = new Point(e.Lon, e.Lat) { SRID = 4326 },
                    Altitude = e.Alt,
                    PositionQuality = PositionQuality.Gps,
                });
            }

            item.Cave.EntranceCount = item.Entrances.Length;
            var main = item.Entrances.Single(e => e.IsMain);
            item.Cave.MainGeom = new Point(main.Lon, main.Lat) { SRID = 4326 };
        }

        await db.SaveChangesAsync(ct);
    }
}
