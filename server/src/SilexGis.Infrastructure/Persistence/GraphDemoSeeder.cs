// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Surveys;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>What a request to lay down the graph demo network did, so a caller can say why.</summary>
public enum GraphDemoSeedOutcome
{
    /// <summary>The network is present: it was written, or it already was.</summary>
    Seeded,

    /// <summary>The host is not in development and no operator asked for it. Nothing was read or written.</summary>
    NotPermitted,

    /// <summary>The installation has no bootstrap administrator to own the rows.</summary>
    NoAdministrator,
}

/// <summary>The outcome plus, only when seeded, how much of everything there now is, and what could not be done.</summary>
public sealed record GraphDemoSeedResult(
    GraphDemoSeedOutcome Outcome,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<string> Notes);

/// <summary>
/// A large, invented, deterministic network of relations for exploring a graph view: caves with
/// entrances, features, feature links, resource links of every relation type, trips with
/// participants, expeditions, a cabinet tree with documents, tags and caving groups — with
/// visibility that differs deliberately per account.
/// </summary>
/// <remarks>
/// Gated like the other development datasets: refused unless the host is in development or the
/// operator named <see cref="AllowKey"/>, because it creates hundreds of rows and, when the test
/// logins exist, changes their club memberships.
/// <para>
/// Idempotent by invented code or name: every row is looked up before it is written, and the
/// random stream is always advanced the same way whether a row exists or not, so a re-run tops
/// up and a partly lost network comes back identical. Output is counts only.
/// </para>
/// <para>
/// Visibility on purpose: about a seventh of the items belong to the Alfa club, which the viewer
/// account is not in (its members see them, others see placeholders), some to Beta (the viewer is
/// in it) and some to Gama (only administrators); ten caves are location-protected and bound to no
/// club, so an account without the exact-location right loses their locating links; a few documents
/// are private to their creator. The editor role reads past visibility by design, so only the
/// viewer account demonstrates the placeholders and the dropped rows.
/// </para>
/// </remarks>
public static partial class GraphDemoSeeder
{
    /// <summary>Configuration key that permits the seeder on a host that is not in development.</summary>
    public const string AllowKey = "GraphDemo:Allow";

    private const int Seed = 20261101;

    public static async Task<GraphDemoSeedResult> SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!environment.IsDevelopment() && !configuration.GetValue(AllowKey, false))
        {
            return new GraphDemoSeedResult(GraphDemoSeedOutcome.NotPermitted, new Dictionary<string, int>(), []);
        }

        var db = services.GetRequiredService<SilexGisDbContext>();
        var userManager = services.GetRequiredService<UserManager<SilexGisUser>>();
        var admins = await userManager.GetUsersInRoleAsync(GlobalRoles.Admin);
        if (admins.Count == 0)
        {
            return new GraphDemoSeedResult(GraphDemoSeedOutcome.NoAdministrator, new Dictionary<string, int>(), []);
        }

        var admin = admins[0].Id;
        var documents = services.GetRequiredService<DocumentWriteService>();
        var fileStore = services.GetRequiredService<IFileStore>();
        var notes = new List<string>();

        // The network hangs off the base demo dataset's containing area; laying that down first
        // is idempotent and spares the operator a second command to remember.
        await DemoSeeder.SeedAsync(db, admin, documents, fileStore, ct);

        var ctx = new Context(db, admin, new Random(Seed), notes);
        await SeedGroupsAsync(ctx, ct);
        await SeedCaversAsync(ctx, ct);
        await SeedFeaturesAsync(ctx, ct);
        await SeedFeatureLinksAsync(ctx, ct);
        await SeedTripsAsync(ctx, ct);
        await SeedExpeditionsAsync(ctx, ct);
        await SeedDocumentsAsync(ctx, documents, fileStore, ct);
        await SeedSurveyModelsAsync(ctx, documents, fileStore, ct);
        await SeedTagsAsync(ctx, ct);
        await SeedResourceLinksAsync(ctx, ct);

        return new GraphDemoSeedResult(GraphDemoSeedOutcome.Seeded, await CountAsync(db, ct), notes);
    }

    private sealed class Context(SilexGisDbContext db, Guid admin, Random rng, List<string> notes)
    {
        public SilexGisDbContext Db { get; } = db;

        public Guid Admin { get; } = admin;

        public Random Rng { get; set; } = rng;

        public List<string> Notes { get; } = notes;

        public Guid Alfa { get; set; }

        public Guid Beta { get; set; }

        public Guid Gama { get; set; }

        public List<Guid> Cavers { get; set; } = [];

        public List<Guid> Caves { get; set; } = [];

        public List<Guid> Generics { get; set; } = [];

        public List<Guid> Trips { get; set; } = [];

        public List<Guid> Documents { get; set; } = [];

        public List<Guid> Cabinets { get; set; } = [];

        public List<Guid> Expeditions { get; set; } = [];

        public List<Guid> Groups => [Alfa, Beta, Gama];

        public Guid? EditorUserId { get; set; }
    }

    /// <summary>
    /// A deterministic spread of visibility over item number <paramref name="i"/>: Alfa-only about a
    /// seventh of the time, Beta every thirteenth, Gama every seventeenth, the rest public or
    /// signed-in only.
    /// </summary>
    private static (Visibility Visibility, Guid? Group) Spread(Context c, int i, int salt = 0)
    {
        var n = i + salt;
        if (n % 7 == 0) return (Visibility.CavingGroup, c.Alfa);
        if (n % 13 == 5) return (Visibility.CavingGroup, c.Beta);
        if (n % 17 == 4) return (Visibility.CavingGroup, c.Gama);
        return (n % 5 < 3 ? Visibility.Public : Visibility.Authenticated, null);
    }

    private static async Task SeedGroupsAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        c.Alfa = await EnsureGroupAsync(c, AlfaName, AlfaSlug, ct);
        c.Beta = await EnsureGroupAsync(c, BetaName, BetaSlug, ct);
        c.Gama = await EnsureGroupAsync(c, GamaName, GamaSlug, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Guid> EnsureGroupAsync(Context c, string name, string slug, CancellationToken ct)
    {
        var existing = await c.Db.CavingGroups.Where(g => g.Slug == slug).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing.Value;
        var group = new CavingGroup { Name = name, Slug = slug, Description = "Invented club for the graph demo network." };
        c.Db.CavingGroups.Add(group);
        // Starter permission groups are written only on creation: a second call would add a second set.
        await CavingGroupPermissionSeeder.SeedForNewGroupAsync(c.Db, group, c.Admin, ct);
        return group.Id;
    }

    private static async Task EnsureMembershipAsync(Context c, Guid caverId, Guid groupId, CancellationToken ct)
    {
        if (await c.Db.CavingGroupMemberships.AnyAsync(m => m.CaverId == caverId && m.CavingGroupId == groupId, ct)
            || c.Db.CavingGroupMemberships.Local.Any(m => m.CaverId == caverId && m.CavingGroupId == groupId))
        {
            return;
        }

        c.Db.CavingGroupMemberships.Add(new CavingGroupMembership { CaverId = caverId, CavingGroupId = groupId });
    }

    private static async Task SeedCaversAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        var known = await db.Cavers.Where(x => x.FullName.StartsWith("Speolog Graf ")).ToDictionaryAsync(x => x.FullName, x => x.Id, ct);
        for (var i = 1; i <= CaverCount; i++)
        {
            var name = CaverName(i);
            if (!known.TryGetValue(name, out var id))
            {
                var caver = new Caver { FullName = name };
                db.Cavers.Add(caver);
                id = caver.Id;
            }

            c.Cavers.Add(id);
        }

        await db.SaveChangesAsync(ct);

        // Fixed shares of the roster per club, leaving the first caver (the busy one) in Alfa.
        for (var i = 0; i < c.Cavers.Count; i++)
        {
            if (i % 3 == 0) await EnsureMembershipAsync(c, c.Cavers[i], c.Alfa, ct);
            if (i % 4 == 1) await EnsureMembershipAsync(c, c.Cavers[i], c.Beta, ct);
            if (i % 9 == 2) await EnsureMembershipAsync(c, c.Cavers[i], c.Gama, ct);
        }

        // The test logins, when they exist: viewer in Beta only, editor in Alfa and Beta.
        foreach (var (email, groups) in new[]
        {
            (ViewerEmail, new[] { c.Beta }),
            (EditorEmail, new[] { c.Alfa, c.Beta }),
        })
        {
            var user = await db.Users.Where(u => u.Email == email).Select(u => new { u.Id, u.DisplayName }).FirstOrDefaultAsync(ct);
            if (user is null)
            {
                c.Notes.Add($"account {email} does not exist; its memberships were not made (enable the test logins and run again)");
                continue;
            }

            if (email == EditorEmail) c.EditorUserId = user.Id;
            var caverId = await db.Cavers.Where(x => x.UserId == user.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (caverId is null)
            {
                var caver = new Caver { FullName = user.DisplayName ?? email, UserId = user.Id };
                db.Cavers.Add(caver);
                caverId = caver.Id;
                c.Notes.Add($"account {email} had no caver row; one was created");
            }

            foreach (var g in groups) await EnsureMembershipAsync(c, caverId.Value, g, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFeaturesAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        var writer = new FeatureWriteService(db, new JsonSchemaPropertiesValidator(), new AnonymousCurrentUser());
        var caveTypeIds = await db.CaveTypes.Where(t => t.Code == "cave" || t.Code == "pit").OrderBy(t => t.Code).Select(t => t.Id).ToListAsync(ct);
        var naturalId = await db.EntranceTypes.Where(t => t.Code == "natural").Select(t => t.Id).SingleAsync(ct);
        var limestoneId = await db.RockTypes.Where(t => t.Code == "limestone").Select(t => t.Id).SingleAsync(ct);
        var areaId = await db.Features.Where(f => f.Name == "Platoul Demo").Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct);
        var parents = areaId is null ? [] : new[] { new ParentSpec(areaId.Value, IsPrimary: true) };
        var existing = await db.Caves.Where(x => x.IdentificationCode!.StartsWith(CaveCodePrefix))
            .ToDictionaryAsync(x => x.IdentificationCode!, x => x.Id, ct);

        writer.BeginDeferredDerivedState();
        for (var i = 1; i <= CaveCount; i++)
        {
            // One stream per cave: what a cave draws while being written (entrances, altitudes)
            // must not shift what the caves after it get when this one already exists.
            c.Rng = new Random(Seed + 1000 + i);
            // Drawn for every cave, existing or not, so the stream does not depend on what is there.
            var lon = 25.30 + c.Rng.NextDouble() * 0.30;
            var lat = 45.40 + c.Rng.NextDouble() * 0.20;
            var typeIndex = c.Rng.Next(caveTypeIds.Count);
            var depth = 10 + c.Rng.Next(300);
            var code = CaveCode(i);
            if (existing.TryGetValue(code, out var existingId))
            {
                c.Caves.Add(existingId);
                continue;
            }

            // Every fifteenth cave from the eleventh is protected and bound to no club, so none of
            // the club members' exact-view rights reaches it: only the owner and administrators see
            // where it is. The busy cave (1) is plain and public.
            var isProtected = i % 15 == 11;
            var (visibility, group) = i == 1 ? (Visibility.Public, null) : isProtected ? (Visibility.Authenticated, null) : Spread(c, i);
            var cave = await writer.CreateCaveAsync(
                new Feature
                {
                    Name = CaveName(i),
                    Description = "Invented cave for the graph demo network.",
                    OwnerUserId = c.Admin,
                    Visibility = visibility,
                    CavingGroupId = group,
                    LocationProtected = isProtected,
                },
                new Cave
                {
                    IdentificationCode = code,
                    CaveTypeId = caveTypeIds[typeIndex],
                    RockTypeId = limestoneId,
                    Region = "Zona Graf",
                    Depth = depth,
                },
                // A child is visible through any readable ancestor, so a club-restricted cave placed
                // under the public area would be readable by everyone; only open caves go under it.
                visibility >= Visibility.Authenticated ? parents : [], ct);
            await db.SaveChangesAsync(ct);

            // Entrances copy owner, club and visibility from the cave, so the cave is bound first.
            var entrances = 1 + i % 3;
            for (var e = 0; e < entrances; e++)
            {
                await writer.CreateEntranceAsync(
                    new Feature
                    {
                        Name = $"Gura {e + 1}",
                        Geom = new Point(new CoordinateZ(lon + e * 0.0004, lat + e * 0.0003, 900 + c.Rng.Next(600))) { SRID = 4326 },
                        OwnerUserId = c.Admin,
                    },
                    new CaveEntrance
                    {
                        CaveFeatureId = cave.Id,
                        EntranceTypeId = naturalId,
                        IsMain = e == 0,
                        Altitude = 900 + c.Rng.Next(600),
                        PositionQuality = PositionQuality.Gps,
                    },
                    ct);
            }

            if (i % 9 == 3)
            {
                await writer.CreateCenterlineAsync(
                    new Feature
                    {
                        Name = "Axa",
                        OwnerUserId = c.Admin,
                        Geom = new MultiLineString([
                            new LineString([new Coordinate(lon, lat), new Coordinate(lon + 0.0006, lat + 0.0004), new Coordinate(lon + 0.0010, lat + 0.0002)]) { SRID = 4326 },
                        ]) { SRID = 4326 },
                    },
                    new Centerline { CaveFeatureId = cave.Id, PathCount = 1, LengthM = 80 + depth, Source = CenterlineSource.Uploaded },
                    firstBecomesDefault: true, ct);
            }

            await db.SaveChangesAsync(ct);
            c.Caves.Add(cave.Id);
        }

        c.Rng = new Random(Seed + 2000);
        var pointTypes = await db.FeatureTypes.Where(t => t.Code == "sinkhole" || t.Code == "peak" || t.Code == "bivouac")
            .OrderBy(t => t.Code).Select(t => t.Id).ToListAsync(ct);
        var lineTypeId = await db.FeatureTypes.Where(t => t.Code == "fracture_line").Select(t => t.Id).SingleAsync(ct);
        var existingGenerics = await db.Features.Where(f => f.Name!.StartsWith("Obiect Graf "))
            .ToDictionaryAsync(f => f.Name!, f => f.Id, ct);
        for (var i = 1; i <= GenericCount; i++)
        {
            var lon = 25.30 + c.Rng.NextDouble() * 0.30;
            var lat = 45.40 + c.Rng.NextDouble() * 0.20;
            var typeIndex = c.Rng.Next(pointTypes.Count);
            var name = GenericName(i);
            if (existingGenerics.TryGetValue(name, out var gid))
            {
                c.Generics.Add(gid);
                continue;
            }

            var isLine = i % 6 == 0;
            var (visibility, group) = Spread(c, i, salt: 3);
            var feature = await writer.CreateGenericAsync(
                new Feature
                {
                    Name = name,
                    FeatureTypeId = isLine ? lineTypeId : pointTypes[typeIndex],
                    Description = "Invented surface feature for the graph demo network.",
                    Geom = isLine
                        ? new LineString([new Coordinate(lon, lat), new Coordinate(lon + 0.003, lat + 0.002)]) { SRID = 4326 }
                        : new Point(lon, lat) { SRID = 4326 },
                    OwnerUserId = c.Admin,
                    Visibility = visibility,
                    CavingGroupId = group,
                },
                [], ct);
            c.Generics.Add(feature.Id);
        }

        await db.SaveChangesAsync(ct);
        await writer.FlushDerivedStateAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFeatureLinksAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        var kinds = await db.LinkKinds.Where(k => new[] { "associated_cave", "hydro_connection", "same_system", "related" }.Contains(k.Code))
            .OrderBy(k => k.Code).Select(k => k.Id).ToListAsync(ct);
        c.Rng = new Random(Seed + 3000);
        var all = c.Caves.Concat(c.Generics).ToList();
        var have = (await db.FeatureLinks.Where(l => all.Contains(l.FromId)).Select(l => new { l.FromId, l.ToId, l.LinkKindId }).ToListAsync(ct))
            .Select(l => (l.FromId, l.ToId, l.LinkKindId)).ToHashSet();

        void Add(Guid from, Guid to, long kind)
        {
            if (from == to || !have.Add((from, to, kind))) return;
            db.FeatureLinks.Add(new FeatureLink { FromId = from, ToId = to, LinkKindId = kind });
        }

        // The busy cave: the first 60 other features all point at it, one of each kind in turn.
        var busy = c.Caves[0];
        var others = all.Where(f => f != busy).ToList();
        for (var k = 0; k < 60; k++) Add(others[k], busy, kinds[k % kinds.Count]);

        // 60 more among the rest, spread over the four kinds.
        for (var k = 0; k < 60; k++)
        {
            var a = all[c.Rng.Next(all.Count)];
            var b = all[c.Rng.Next(all.Count)];
            var kind = kinds[k % kinds.Count];
            if (a != busy && b != busy) Add(a, b, kind);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedTripsAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        c.Rng = new Random(Seed + 4000);
        var types = await db.TripTypes.OrderBy(t => t.Code).Select(t => t.Id).ToListAsync(ct);
        var roles = await db.TripParticipantRoles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);
        var secondary = new[] { "participant", "surveyor", "photographer", "driver" };
        var existing = await db.TripLogs.Where(t => t.Title.StartsWith("Ieșire Graf ")).ToDictionaryAsync(t => t.Title, t => t.Id, ct);
        var withParty = (await db.TripLogParticipants.Where(p => c.Cavers.Contains(p.CaverId)).Select(p => p.TripLogId).Distinct().ToListAsync(ct)).ToHashSet();

        for (var n = 1; n <= TripCount; n++)
        {
            var partySize = 1 + c.Rng.Next(5);
            var picks = Enumerable.Range(0, partySize).Select(_ => 1 + c.Rng.Next(CaverCount - 1)).Distinct().ToList();
            var title = TripTitle(n);
            if (!existing.TryGetValue(title, out var id))
            {
                var (visibility, group) = Spread(c, n, salt: 1);
                var date = new DateOnly(2025, 1, 1).AddDays(n * 3);
                var trip = new TripLog
                {
                    Title = title,
                    TripTypeId = types[n % types.Count],
                    TripDate = date,
                    Description = "Invented trip for the graph demo network.",
                    OwnerUserId = c.Admin,
                    Visibility = visibility,
                    CavingGroupId = group,
                    State = n % 25 == 0 ? ActivityState.Draft : ActivityState.Published,
                    PublishedAt = n % 25 == 0 ? null : new DateTimeOffset(date.ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero),
                };
                db.TripLogs.Add(trip);
                id = trip.Id;
            }

            c.Trips.Add(id);
            if (withParty.Contains(id)) continue;

            // The first caver is on the first 120 trips, as leader on some, so his page has far more
            // trips than any list shows at once.
            var party = new List<Guid>();
            if (n <= BusyCaverTrips)
            {
                party.Add(c.Cavers[0]);
                db.TripLogParticipants.Add(new TripLogParticipant { TripLogId = id, CaverId = c.Cavers[0], RoleId = roles[n % 5 == 0 ? "leader" : "participant"] });
            }

            for (var p = 0; p < picks.Count; p++)
            {
                var caver = c.Cavers[picks[p]];
                party.Add(caver);
                var role = n > BusyCaverTrips && p == 0 ? "leader" : secondary[p % secondary.Length];
                db.TripLogParticipants.Add(new TripLogParticipant { TripLogId = id, CaverId = caver, RoleId = roles[role] });
            }

            await db.SaveChangesAsync(ct);
            await TripPartyNumbers.AssignAsync(db, id, party, ct);
        }

        await db.SaveChangesAsync(ct);

        // Which caves a trip worked, visited or was aimed at, through the same helper the trip form
        // uses: one host link per (trip, role) with the caves as members.
        for (var n = 1; n <= TripCount; n++)
        {
            if (n % 2 == 0) await TripRoleLinks.NameFeatureAsync(db, c.Trips[n - 1], c.Caves[(n * 7) % c.Caves.Count], "trip-visited", c.Admin, ct);
            if (n % 10 == 0) await TripRoleLinks.NameFeatureAsync(db, c.Trips[n - 1], c.Caves[0], "trip-work-area", c.Admin, ct);
            if (n % 9 == 0) await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedExpeditionsAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        c.Rng = new Random(Seed + 5000);
        var roleIds = await db.ExpeditionRosterRoles.OrderBy(r => r.SortOrder).Select(r => r.Id).Take(3).ToListAsync(ct);
        var existing = await db.Expeditions.Where(x => x.Name.StartsWith("Expediția Graf ")).ToDictionaryAsync(x => x.Name, x => x.Id, ct);
        for (var k = 1; k <= ExpeditionCount; k++)
        {
            var picks = Enumerable.Range(0, 6).Select(_ => c.Rng.Next(CaverCount)).Distinct().ToList();
            var name = ExpeditionName(k);
            var start = new DateOnly(2025, 3, 1).AddDays(k * 50);
            if (!existing.TryGetValue(name, out var id))
            {
                var visibility = k == 2 ? Visibility.CavingGroup : k == 5 ? Visibility.Authenticated : Visibility.Public;
                var expedition = new Expedition
                {
                    Name = name,
                    Description = "Invented expedition for the graph demo network.",
                    StartDate = start,
                    EndDate = DayRange.EndForStorage(start, start.AddDays(9)),
                    OwnerUserId = c.Admin,
                    Visibility = visibility,
                    CavingGroupId = k == 2 ? c.Alfa : null,
                    State = ActivityState.Published,
                    PublishedAt = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                };
                db.Expeditions.Add(expedition);
                id = expedition.Id;
            }

            c.Expeditions.Add(id);
            for (var p = 0; p < picks.Count; p++)
            {
                var caver = c.Cavers[picks[p]];
                var role = roleIds[p == 0 ? 0 : p % roleIds.Count];
                if (db.ExpeditionRoster.Local.Any(r => r.ExpeditionId == id && r.CaverId == caver && r.RoleId == role && r.FromDate == start)
                    || await db.ExpeditionRoster.AnyAsync(r => r.ExpeditionId == id && r.CaverId == caver && r.RoleId == role && r.FromDate == start, ct))
                {
                    continue;
                }

                db.ExpeditionRoster.Add(new ExpeditionRosterEntry
                {
                    ExpeditionId = id,
                    CaverId = caver,
                    RoleId = role,
                    FromDate = start,
                    ToDate = DayRange.OpenEndForStorage(start, start.AddDays(9)),
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // Ten trips per expedition, the first sixty trips in order; a trip belongs to one only.
        var placed = (await db.ExpeditionTrips.Select(m => m.TripLogId).ToListAsync(ct)).ToHashSet();
        for (var t = 0; t < 60; t++)
        {
            var trip = c.Trips[t];
            if (placed.Contains(trip)) continue;
            db.ExpeditionTrips.Add(new ExpeditionTrip
            {
                ExpeditionId = c.Expeditions[t / 10],
                TripLogId = trip,
                JoinedAt = new DateTimeOffset(2025, 2, 1, 12, 0, 0, TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDocumentsAsync(Context c, DocumentWriteService documents, IFileStore fileStore, CancellationToken ct)
    {
        var db = c.Db;
        var cabinets = new CabinetWriteService(db);

        // The tree: one root, three below it, two below each of those — three levels, ten cabinets.
        async Task<Guid> EnsureCabinetAsync(string name, Guid? parent)
        {
            var found = await db.Cabinets.Where(x => x.Name == name && x.ParentId == parent).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (found is not null) return found.Value;
            var made = await cabinets.CreateAsync(name, "Invented cabinet for the graph demo network.", parent, ct);
            // A child is placed from its parent's stored path, so each level is saved before the next.
            await db.SaveChangesAsync(ct);
            return made.Id;
        }

        var root = await EnsureCabinetAsync(RootCabinetName, null);
        var level2 = new List<Guid>();
        for (var i = 0; i < 3; i++) level2.Add(await EnsureCabinetAsync(CabinetNames[i], root));
        var level3 = new List<Guid>();
        for (var i = 0; i < 6; i++) level3.Add(await EnsureCabinetAsync(CabinetNames[3 + i], level2[i / 2]));
        c.Cabinets = [root, .. level2, .. level3];

        var documentTypeId = await db.DocumentTypes.OrderBy(t => t.SortOrder).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
        var existing = await db.Documents.Where(d => d.Title.StartsWith("Raport Graf ")).ToDictionaryAsync(d => d.Title, d => d.Id, ct);
        var privateOwner = c.EditorUserId ?? c.Admin;

        for (var i = 1; i <= DocumentCount; i++)
        {
            var title = DocumentTitle(i);
            if (!existing.TryGetValue(title, out var id))
            {
                var bytes = Encoding.UTF8.GetBytes($"{title}\ninvented document for the graph demo network\n");
                using var stream = new MemoryStream(bytes, writable: false);
                var path = await fileStore.SaveAsync(stream, ".bin", ct);
                var isPrivate = i % 18 == 5;
                var (visibility, group) = isPrivate ? (Visibility.Private, null) : Spread(c, i, salt: 2);
                var made = documents.Create(
                    new StoredContent(path, $"raport-graf-{i:D3}.bin", "application/octet-stream", bytes.LongLength,
                        Convert.ToHexStringLower(SHA256.HashData(bytes)), FileKind.Document),
                    title, isPrivate ? privateOwner : c.Admin, c.Admin,
                    documentDate: new DateOnly(2020, 1, 1).AddDays(i * 11), cavingGroupId: group);
                var document = db.Documents.Local.Single(d => d.Id == made.Version.DocumentId);
                document.Visibility = visibility;
                document.DocumentTypeId = documentTypeId;
                id = document.Id;
                await db.SaveChangesAsync(ct);
            }

            c.Documents.Add(id);
            var cabinet = c.Cabinets[i % c.Cabinets.Count];
            if (!await db.CabinetDocuments.AnyAsync(x => x.CabinetId == cabinet && x.DocumentId == id, ct))
            {
                db.CabinetDocuments.Add(new CabinetDocument { CabinetId = cabinet, DocumentId = id });
            }
        }

        await db.SaveChangesAsync(ct);

        // The first 40 reports are attached to a cave, the next 20 to a trip.
        var files = (await (from v in db.DocumentVersions.AsNoTracking()
                            join f in db.StoredFiles.AsNoTracking() on v.Id equals f.DocumentVersionId
                            where v.IsCurrent && c.Documents.Contains(v.DocumentId)
                            select new { v.DocumentId, FileId = f.Id }).ToListAsync(ct))
            .ToDictionary(x => x.DocumentId, x => x.FileId);
        for (var i = 0; i < 60; i++)
        {
            var fileId = files[c.Documents[i]];
            var isFeature = i < 40;
            var target = isFeature ? c.Caves[i + 1] : c.Trips[i - 40];
            var found = isFeature
                ? await db.Attachments.AnyAsync(a => a.FileId == fileId && a.FeatureId == target, ct)
                : await db.Attachments.AnyAsync(a => a.FileId == fileId && a.EntityType == AttachedEntityType.TripLog && a.EntityId == target, ct);
            if (found) continue;
            db.Attachments.Add(new Attachment
            {
                FileId = fileId,
                FeatureId = isFeature ? target : null,
                EntityType = isFeature ? null : AttachedEntityType.TripLog,
                EntityId = isFeature ? null : target,
                Role = isFeature ? AttachmentRole.Document : AttachmentRole.Report,
                SortOrder = 0,
                AddedBy = c.Admin,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSurveyModelsAsync(Context c, DocumentWriteService documents, IFileStore fileStore, CancellationToken ct)
    {
        var db = c.Db;
        for (var k = 1; k <= 3; k++)
        {
            var name = $"Model Graf {k}";
            if (await db.SurveyModels.AnyAsync(m => m.Name == name, ct)) continue;
            var caveId = c.Caves[k * 5];
            var spot = await db.Features.Where(f => f.Id == caveId).Select(f => f.Geom).FirstAsync(ct)
                ?? await db.Features.Where(f => f.Kind == FeatureKind.CaveEntrance && f.Entrance!.CaveFeatureId == caveId).Select(f => f.Geom).FirstAsync(ct);
            var bytes = DemoSurvey.Build();
            using var stream = new MemoryStream(bytes, writable: false);
            var path = await fileStore.SaveAsync(stream, ".3d", ct);
            var stored = documents.Create(
                new StoredContent(path, $"model-graf-{k}.3d", "application/octet-stream", bytes.LongLength,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), FileKind.Survey),
                name, c.Admin, c.Admin).File;
            var model = new SurveyModel
            {
                CaveFeatureId = caveId,
                Name = name,
                FileId = stored.Id,
                Format = SurveyModelFormat.Survex3d,
                Description = "An invented survey — no real cave was measured to make it.",
                Anchor = new Point(spot!.Coordinate.X, spot.Coordinate.Y) { SRID = 4326 },
                AnchorHeightM = 1000,
            };
            db.SurveyModels.Add(model);
            SurveyModelReading.QueueFirst(db, model, requestedBy: null);
            await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedTagsAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;
        c.Rng = new Random(Seed + 6000);
        var tagIds = new List<long>();
        for (var i = 1; i <= TagCount; i++)
        {
            var name = TagName(i);
            var slug = Tag.Slugify(name);
            var id = await db.Tags.Where(t => t.Slug == slug).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
            if (id is null)
            {
                var tag = new Tag { Name = name, Slug = slug };
                db.Tags.Add(tag);
                await db.SaveChangesAsync(ct);
                id = tag.Id;
            }

            tagIds.Add(id.Value);
        }

        // Items as (feature, entity-type, entity-id): features, trips and documents.
        var items = new List<(Guid? Feature, AttachedEntityType? Type, Guid Id)>();
        items.AddRange(c.Caves.Concat(c.Generics).Select(f => ((Guid?)f, (AttachedEntityType?)null, f)));
        items.AddRange(c.Trips.Select(t => ((Guid?)null, (AttachedEntityType?)AttachedEntityType.TripLog, t)));
        items.AddRange(c.Documents.Select(d => ((Guid?)null, (AttachedEntityType?)AttachedEntityType.Document, d)));
        var order = items.OrderBy(_ => c.Rng.Next()).ToList();

        var have = (await db.Taggings.Where(t => tagIds.Contains(t.TagId))
                .Select(t => new { t.TagId, Target = t.FeatureId ?? t.EntityId!.Value }).ToListAsync(ct))
            .Select(t => (t.TagId, t.Target)).ToHashSet();

        void Tag1(long tag, (Guid? Feature, AttachedEntityType? Type, Guid Id) item)
        {
            if (!have.Add((tag, item.Id))) return;
            db.Taggings.Add(new Tagging { TagId = tag, FeatureId = item.Feature, EntityType = item.Type, EntityId = item.Feature is null ? item.Id : null, AddedBy = c.Admin });
        }

        // One tag on 100 items (the busy one); each of the other 29 on about ten.
        for (var k = 0; k < BusyTagItems; k++) Tag1(tagIds[0], order[k]);
        for (var t = 1; t < tagIds.Count; t++)
        {
            for (var k = 0; k < 10; k++) Tag1(tagIds[t], order[(BusyTagItems + t * 10 + k) % order.Count]);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedResourceLinksAsync(Context c, CancellationToken ct)
    {
        var db = c.Db;

        foreach (var (code, name, directed, inverse) in CustomRelations)
        {
            if (await db.ResLinkRelationTypes.AnyAsync(r => r.Code == code, ct)) continue;
            var next = (await db.ResLinkRelationTypes.MaxAsync(r => (int?)r.SortOrder, ct) ?? 0) + 10;
            db.ResLinkRelationTypes.Add(new ResLinkRelationType { Code = code, Name = name, Directed = directed, InverseName = inverse, SortOrder = next });
            await db.SaveChangesAsync(ct);
        }

        var relations = await db.ResLinkRelationTypes.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(ct);
        var undirected = relations.Where(r => !r.Directed).ToList();
        var existing = (await db.ResLinks.Where(l => l.ShortCode.StartsWith("GL")).Select(l => l.ShortCode).ToListAsync(ct)).ToHashSet();

        var caves = c.Caves;
        AttachedEntityType? Ent(int kind) => kind switch
        {
            1 => AttachedEntityType.Document,
            2 => AttachedEntityType.TripLog,
            3 => AttachedEntityType.Caver,
            4 => AttachedEntityType.Cabinet,
            5 => AttachedEntityType.Expedition,
            6 => AttachedEntityType.CavingGroup,
            _ => null,
        };

        Guid Pick(int kind) => kind switch
        {
            0 => c.Rng.Next(10) == 0 ? c.Generics[c.Rng.Next(c.Generics.Count)] : caves[c.Rng.Next(caves.Count)],
            1 => c.Documents[c.Rng.Next(c.Documents.Count)],
            2 => c.Trips[c.Rng.Next(c.Trips.Count)],
            3 => c.Cavers[c.Rng.Next(c.Cavers.Count)],
            4 => c.Cabinets[c.Rng.Next(c.Cabinets.Count)],
            5 => c.Expeditions[c.Rng.Next(c.Expeditions.Count)],
            _ => c.Groups[c.Rng.Next(3)],
        };

        // 0 cave, 1 document, 2 trip, 3 caver, 4 cabinet, 5 expedition, 6 club — most often caves and documents.
        int RandomKind() => c.Rng.Next(100) switch { < 38 => 0, < 62 => 1, < 77 => 2, < 87 => 3, < 91 => 4, < 96 => 5, _ => 6 };

        for (var idx = 0; idx < ResLinkCount; idx++)
        {
            // One stream per link, so a link that already exists cannot shift the ones after it.
            c.Rng = new Random(Seed + 10000 + idx);
            var relation = idx < relations.Count
                ? relations[idx]
                : (idx % 50 == 25 ? relations.Where(r => r.Directed).ElementAt(c.Rng.Next(relations.Count(r => r.Directed)))
                    : c.Rng.Next(5) == 0 ? null : undirected[c.Rng.Next(undirected.Count)]);
            var directed = relation?.Directed == true;
            var size = c.Rng.Next(100) < 60 ? 2 : 3 + c.Rng.Next(10);

            // The first member's kind follows what the relation reads from where it can.
            var firstKind = relation?.Code switch
            {
                { } code when code.StartsWith("trip-", StringComparison.Ordinal) => 2,
                "documents" or "text-of" or "derived-from" => 1,
                { } code when code.StartsWith("map-", StringComparison.Ordinal) => 1,
                _ => RandomKind(),
            };

            var members = new List<(int Kind, Guid Id)>();
            var seen = new HashSet<Guid>();
            // The busy cave sits in the first links: as the first member where the relation reads
            // from a cave (or from nothing in particular), else as an extra member.
            var busyHere = idx < BusyCaveLinks;
            var mainIndex = 0;
            if (busyHere && firstKind == 0)
            {
                members.Add((0, caves[0]));
                seen.Add(caves[0]);
            }
            else
            {
                var first = (firstKind, Pick(firstKind));
                members.Add(first);
                seen.Add(first.Item2);
                if (busyHere && seen.Add(caves[0])) members.Add((0, caves[0]));
            }

            var guard = 0;
            while (members.Count < size && guard++ < 200)
            {
                var kind = RandomKind();
                var id = Pick(kind);
                if (seen.Add(id)) members.Add((kind, id));
            }

            var code2 = LinkCode(idx + 1);
            if (existing.Contains(code2)) continue;
            var link = new ResLink
            {
                ShortCode = code2,
                RelationTypeId = relation?.Id,
                Description = $"Invented relation {idx + 1} in the graph demo network.",
                CreatedBy = c.Admin,
            };
            db.ResLinks.Add(link);

            for (var m = 0; m < members.Count; m++)
            {
                var (kind, id) = members[m];
                var type = Ent(kind);
                var anchorKind = AnchorKind.Whole;
                string? anchor = null;
                if (kind == 1 && c.Rng.Next(4) == 0)
                {
                    if (c.Rng.Next(2) == 0)
                    {
                        anchorKind = AnchorKind.Page;
                        anchor = JsonSerializer.Serialize(new { page = 1 + c.Rng.Next(5) }, JsonSerializerOptions.Web);
                    }
                    else
                    {
                        anchorKind = AnchorKind.TimePoint;
                        anchor = JsonSerializer.Serialize(new { t = Math.Round(c.Rng.NextDouble() * 600, 1) }, JsonSerializerOptions.Web);
                    }

                    // A shape the link rules would refuse is written whole instead.
                    var problem = ResLinkRules.MemberProblem(new ResLinkRules.MemberShape(null, type, id, anchorKind, anchor));
                    if (problem is not null)
                    {
                        anchorKind = AnchorKind.Whole;
                        anchor = null;
                    }
                }

                db.ResLinkMembers.Add(new ResLinkMember
                {
                    ResLinkId = link.Id,
                    FeatureId = type is null ? id : null,
                    EntityType = type,
                    EntityId = type is null ? null : id,
                    IsMain = directed && members.Count >= 2 && m == mainIndex,
                    AnchorKind = anchorKind,
                    Anchor = anchor,
                    SortOrder = m,
                    AddedBy = c.Admin,
                });
            }

            if (idx % 40 == 39) await db.SaveChangesAsync(ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, int>> CountAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var caveIds = db.Caves.Where(x => x.IdentificationCode!.StartsWith(CaveCodePrefix)).Select(x => x.Id);
        return new Dictionary<string, int>
        {
            ["caves"] = await caveIds.CountAsync(ct),
            ["entrances"] = await db.CaveEntrances.CountAsync(e => caveIds.Contains(e.CaveFeatureId), ct),
            ["centerlines"] = await db.Centerlines.CountAsync(x => caveIds.Contains(x.CaveFeatureId), ct),
            ["protectedCaves"] = await db.Features.CountAsync(f => f.Kind == FeatureKind.Cave && f.LocationProtected && f.Name!.StartsWith("Peștera Graf "), ct),
            ["genericFeatures"] = await db.Features.CountAsync(f => f.Name!.StartsWith("Obiect Graf "), ct),
            ["featureLinks"] = await db.FeatureLinks.CountAsync(l => caveIds.Contains(l.FromId) || caveIds.Contains(l.ToId) || db.Features.Any(f => f.Id == l.FromId && f.Name!.StartsWith("Obiect Graf ")), ct),
            ["resourceLinks"] = await db.ResLinks.CountAsync(l => l.ShortCode.StartsWith("GL"), ct),
            ["directedResourceLinks"] = await db.ResLinks.CountAsync(l => l.ShortCode.StartsWith("GL") && l.RelationTypeId != null && db.ResLinkRelationTypes.Any(r => r.Id == l.RelationTypeId && r.Directed), ct),
            ["relationTypesUsed"] = await db.ResLinks.Where(l => l.ShortCode.StartsWith("GL") && l.RelationTypeId != null).Select(l => l.RelationTypeId).Distinct().CountAsync(ct),
            ["trips"] = await db.TripLogs.CountAsync(t => t.Title.StartsWith("Ieșire Graf "), ct),
            ["cavers"] = await db.Cavers.CountAsync(x => x.FullName.StartsWith("Speolog Graf "), ct),
            ["busyCaverTrips"] = await db.TripLogParticipants.CountAsync(p => db.Cavers.Any(x => x.Id == p.CaverId && x.FullName == CaverName(1)), ct),
            ["expeditions"] = await db.Expeditions.CountAsync(x => x.Name.StartsWith("Expediția Graf "), ct),
            ["cabinets"] = await db.Cabinets.CountAsync(x => x.Name == RootCabinetName || x.Name.StartsWith("Fond Graf ") || x.Name.StartsWith("Seria Graf "), ct),
            ["documents"] = await db.Documents.CountAsync(d => d.Title.StartsWith("Raport Graf "), ct),
            ["tags"] = await db.Tags.CountAsync(t => t.Name.StartsWith("graf-"), ct),
            ["taggings"] = await db.Taggings.CountAsync(t => db.Tags.Any(x => x.Id == t.TagId && x.Name.StartsWith("graf-")), ct),
            ["cavingGroups"] = await db.CavingGroups.CountAsync(g => g.Name.StartsWith("Clubul Graf "), ct),
            ["groupMemberships"] = await db.CavingGroupMemberships.CountAsync(m => db.CavingGroups.Any(g => g.Id == m.CavingGroupId && g.Name.StartsWith("Clubul Graf ")), ct),
            ["surveyModels"] = await db.SurveyModels.CountAsync(m => m.Name.StartsWith("Model Graf "), ct),
        };
    }
}
