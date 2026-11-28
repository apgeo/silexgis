// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>The vocabulary a run adds beside the shipped one: its own rows, or the same codes an earlier run left.</summary>
public sealed record TestDataExtraVocabulary(long CaveTypeId, long EntranceTypeId, long RockTypeId, long LinkKindId, long FeatureTypeId);

/// <summary>
/// The rest of what the people part owns: vocabulary an installation adds of its own (a cave
/// type, an entrance type, a rock, a link kind, a feature kind with a properties schema), links
/// between caves, the ids caves carry in other registers, features of the added kind, and — only
/// on an installation holding nothing but generated data — shares and QR publications, which
/// open a cave to people without an account.
/// </summary>
public static class TestDataExtrasWriter
{
    public const string CaveTypeCode = "testgen_ice_cave";
    public const string EntranceTypeCode = "testgen_shaft_top";
    public const string RockTypeCode = "testgen_travertine";
    public const string LinkKindCode = "testgen_dye_trace";
    public const string FeatureTypeCode = "testgen_captured_spring";

    private const string FeatureTypeSchema =
        """
        {"type":"object","properties":{
          "debit_ls":{"type":"number","title":"Debit (l/s)","minimum":0},
          "captat_in":{"type":"integer","title":"Captat în anul","minimum":1900},
          "folosinta":{"type":"string","title":"Folosință","enum":["apă potabilă","irigații","abandonat"]}
        }}
        """;

    /// <summary>Which planned caves take the added cave type and rock: a few, spread over the run.</summary>
    public static bool TakesExtraCaveType(PlannedCave cave) => cave is not null && cave.Index % 40 == 7;

    public static bool TakesExtraRock(PlannedCave cave) => cave is not null && cave.Index % 45 == 11;

    /// <summary>
    /// Whether a cave's second-or-later entrance takes the added entrance type: the first cave of
    /// the run with more than one entrance, so every run that has one shows the type, and every
    /// fifth such cave after it.
    /// </summary>
    public static bool TakesExtraEntranceType(TestDataWorldPlan plan, PlannedCave cave, int entrance)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cave);
        if (entrance == 0 || cave.Entrances.Count < 2)
        {
            return false;
        }

        var before = plan.Caves.TakeWhile(c => c.Index != cave.Index).Count(c => c.Entrances.Count > 1);
        return before % 5 == 0;
    }

    public static async Task<TestDataExtraVocabulary> EnsureVocabularyAsync(
        SilexGisDbContext db, FeatureTypeWriteService featureTypes, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(featureTypes);
        ArgumentNullException.ThrowIfNull(session);
        var caveType = await db.CaveTypes.FirstOrDefaultAsync(t => t.Code == CaveTypeCode, ct)
            ?? db.CaveTypes.Add(new CaveType { Code = CaveTypeCode, Name = "Peșteră cu gheață (generată)", SortOrder = 900 }).Entity;
        var entranceType = await db.EntranceTypes.FirstOrDefaultAsync(t => t.Code == EntranceTypeCode, ct)
            ?? db.EntranceTypes.Add(new EntranceType { Code = EntranceTypeCode, Name = "Gură de aven (generată)", SortOrder = 900 }).Entity;
        var rockType = await db.RockTypes.FirstOrDefaultAsync(t => t.Code == RockTypeCode, ct)
            ?? db.RockTypes.Add(new RockType { Code = RockTypeCode, Name = "Travertin (generat)", SortOrder = 900 }).Entity;
        var linkKind = await db.LinkKinds.FirstOrDefaultAsync(t => t.Code == LinkKindCode, ct)
            ?? db.LinkKinds.Add(new LinkKind { Code = LinkKindCode, Name = "Trasare cu colorant (generată)", SortOrder = 900, Locating = false }).Entity;
        await session.SaveAndRegisterAsync(db, ct);

        // The kind writer saves by itself, so the kind it makes is listed by hand.
        var featureType = await db.FeatureTypes.FirstOrDefaultAsync(t => t.Code == FeatureTypeCode, ct);
        if (featureType is null)
        {
            featureType = await featureTypes.CreateAsync(new FeatureTypeInput(
                FeatureTypeCode,
                "Izvor captat (generat)",
                "Un izvor carstic captat, cu debitul și folosința lui; tip adăugat de generatorul de date de test.",
                900,
                FeatureCategory.Surface,
                [GeometryClass.Point],
                RequiresParent: false,
                ProtectedDisplay.SnapPoint,
                SymbolFile: null,
                Style: null,
                FeatureTypeSchema), ct);
            session.Register(db, "feature_types", featureType.Id);
            session.Count(TestDataSession.CountKind("feature_types"));
            await db.SaveChangesAsync(ct);
        }

        return new TestDataExtraVocabulary(caveType.Id, entranceType.Id, rockType.Id, linkKind.Id, featureType.Id);
    }

    /// <summary>Links between caves of one massif, every kind of link at least once, the added kind included.</summary>
    public static async Task WriteLinksAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var kinds = await db.LinkKinds.AsNoTracking().OrderBy(k => k.Code).Select(k => new { k.Id, k.Code }).ToListAsync(ct);
        var shipped = kinds.Where(k => k.Code != LinkKindCode).ToList();
        if (shipped.Count == 0)
        {
            return;
        }

        var seen = new HashSet<(Guid, Guid, long)>();
        var turn = 0;
        foreach (var region in session.Plan.Caves.GroupBy(c => c.RegionCode, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var caves = region.Where(c => session.Caves.ContainsKey(c.Index)).ToList();
            if (caves.Count < 2)
            {
                continue;
            }

            var rng = TestDataRng.For(session.Profile.Seed, "links:" + region.Key);
            var count = Math.Max(1, caves.Count / 12);
            for (var i = 0; i < count; i++)
            {
                var from = rng.Pick(caves);
                var to = rng.Pick(caves);
                if (from.Index == to.Index)
                {
                    continue;
                }

                // A water trace runs from a sinking cave or a pit to a spring cave when the massif has one.
                var springs = caves.Where(c => c.CaveTypeCode == "spring_cave" && c.Index != from.Index).ToList();
                var kind = (turn++ % (shipped.Count + 1)) switch
                {
                    var k when k < shipped.Count => shipped[k].Id,
                    _ => session.Extra?.LinkKindId ?? shipped[0].Id,
                };
                if (kind == session.Extra?.LinkKindId && springs.Count > 0)
                {
                    to = rng.Pick(springs);
                }

                var key = (session.Caves[from.Index], session.Caves[to.Index], kind);
                if (!seen.Add(key))
                {
                    continue;
                }

                db.FeatureLinks.Add(new FeatureLink
                {
                    FromId = key.Item1,
                    ToId = key.Item2,
                    LinkKindId = kind,
                    Note = rng.Chance(0.5) ? $"Legătură între {from.Name} și {to.Name}, după observațiile de teren." : null,
                });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>
    /// The ids caves carry in other registers: the national cadastre's for many (the cave's own
    /// code), Grottocenter's for a few, drawn from a range far above any real Grottocenter entry so
    /// an invented cave never links to a real one.
    /// </summary>
    public static async Task WriteExternalIdsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var caveIds = session.Caves.Values.ToList();
        var codes = await db.Caves.Where(c => caveIds.Contains(c.Id) && c.IdentificationCode != null)
            .ToDictionaryAsync(c => c.Id, c => c.IdentificationCode!, ct);
        foreach (var cave in session.Plan.Caves.Where(c => session.Caves.ContainsKey(c.Index)))
        {
            var id = session.Caves[cave.Index];
            var rng = TestDataRng.For(session.Profile.Seed, "external", cave.Index);
            if (codes.TryGetValue(id, out var code) && rng.Chance(0.4))
            {
                db.FeatureExternalIds.Add(new FeatureExternalId
                {
                    FeatureId = id, System = ExternalIdSystem.NationalCadastre, Value = code, CreatedBy = session.OwnerUserId,
                });
            }

            if (rng.Chance(0.1))
            {
                db.FeatureExternalIds.Add(new FeatureExternalId
                {
                    FeatureId = id,
                    System = ExternalIdSystem.Grottocenter,
                    Value = (90_000_000 + cave.Index).ToString(CultureInfo.InvariantCulture),
                    CreatedBy = session.OwnerUserId,
                });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>One captured spring of the added kind in each massif, with its properties filled.</summary>
    public static async Task WriteSpringsAsync(
        SilexGisDbContext db, FeatureWriteService writer, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        if (session.Extra is not { } extra)
        {
            return;
        }

        await using var hierarchy = await writer.BeginHierarchyWriteAsync(ct);
        foreach (var planned in session.Plan.Regions)
        {
            var region = planned.Region;
            var rng = TestDataRng.For(session.Profile.Seed, "spring:" + region.Code);
            var lon = (region.West + region.East) / 2 + rng.Between(-0.02, 0.02);
            var lat = (region.South + region.North) / 2 + rng.Between(-0.02, 0.02);
            var properties = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["debit_ls"] = Math.Round(rng.Between(0.5, 120.0), 1),
                ["captat_in"] = rng.Between(1950, 2015),
                ["folosinta"] = rng.Pick(["apă potabilă", "irigații", "abandonat"]),
            });
            var spring = await writer.CreateGenericAsync(
                new Feature
                {
                    FeatureTypeId = extra.FeatureTypeId,
                    Name = $"Izvorul captat de la {rng.Pick(region.Villages)}",
                    Description = TestDataTextRo.Footer(session.Profile.Marking.Tag),
                    Geom = new Point(lon, lat) { SRID = 4326 },
                    Properties = properties,
                    OwnerUserId = session.OwnerUserId,
                    Visibility = Visibility.Public,
                },
                parents: session.Massifs.TryGetValue(region.Code, out var massif) ? [new ParentSpec(massif, IsPrimary: true)] : [],
                ct);
            TestDataGeographyWriter.Mark(db, session, spring.Id);
            session.Count("springs");
        }

        await db.SaveChangesAsync(ct);
        await hierarchy.CommitAsync(ct);
    }

    /// <summary>
    /// Two share links and two QR publications on public caves. Only on an installation that held
    /// nothing but generated data when the run started: a share or a QR code opens a cave to people
    /// without an account, and beside real data a generated one could be mistaken for a real one.
    /// </summary>
    public static async Task WritePublicationsAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        if (session.AlongsideExistingData)
        {
            session.Note(TestDataNotes.NoPublicationsBesideData);
            return;
        }

        var open = session.Plan.Caves
            .Where(c => c.Visibility == Visibility.Public && !c.LocationProtected && session.Caves.ContainsKey(c.Index))
            .Take(4)
            .ToList();
        for (var i = 0; i < open.Count; i++)
        {
            var id = session.Caves[open[i].Index];
            if (i < 2)
            {
                db.FeatureShares.Add(new FeatureShare
                {
                    FeatureId = id,
                    // The token itself is thrown away: a link nobody holds, there to be listed and revoked.
                    TokenHash = Base64Url.EncodeToString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
                    Mode = i == 0 ? FeatureShareMode.Public : FeatureShareMode.RequiresLogin,
                    IncludeSubtree = i == 0,
                    CreatedBy = session.OwnerUserId,
                });
            }
            else
            {
                db.CaveQrPublications.Add(new CaveQrPublication { FeatureId = id, PublishedBy = session.OwnerUserId });
            }
        }

        await session.SaveAndRegisterAsync(db, ct);
    }
}
