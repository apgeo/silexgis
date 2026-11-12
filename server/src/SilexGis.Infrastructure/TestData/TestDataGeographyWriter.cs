// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// Writes the geography of a run — massifs, their karst areas, caves and entrances — through the
/// feature write service, so containment, protection and the cave mirror are kept exactly as a
/// person's edits keep them.
/// </summary>
public static class TestDataGeographyWriter
{
    public static async Task<TestDataVocabularyIds> ResolveVocabularyAsync(SilexGisDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var caveTypes = await db.CaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var entranceTypes = await db.EntranceTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var rockTypes = await db.RockTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var featureTypes = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == TestDataVocabulary.MassifType || t.Code == TestDataVocabulary.KarstAreaType)
            .ToDictionaryAsync(t => t.Code, t => t.Id, ct);

        var missing = TestDataVocabulary.CaveTypes.Where(c => !caveTypes.ContainsKey(c))
            .Concat(TestDataVocabulary.NaturalEntranceWeights.Keys.Where(c => !entranceTypes.ContainsKey(c)))
            .Concat(new[] { TestDataVocabulary.MassifType, TestDataVocabulary.KarstAreaType }.Where(c => !featureTypes.ContainsKey(c)))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The shipped vocabulary lacks {string.Join(", ", missing)}; the generator writes only against shipped codes.");
        }

        return new TestDataVocabularyIds(
            caveTypes, entranceTypes, rockTypes,
            featureTypes[TestDataVocabulary.MassifType], featureTypes[TestDataVocabulary.KarstAreaType]);
    }

    /// <summary>The marking tag, made if it does not exist yet; listed against the run only when this run made it.</summary>
    public static async Task EnsureMarkingTagAsync(SilexGisDbContext db, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var name = session.Profile.Marking.Tag.Trim();
        var slug = Tag.Slugify(name);
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tag is null)
        {
            tag = new Tag { Name = name, Slug = slug };
            db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
            session.Register(db, "tags", tag.Id);
            session.Count("tags");
            await db.SaveChangesAsync(ct);
        }

        session.MarkingTagId = tag.Id;
    }

    public static async Task WriteRegionsAsync(
        SilexGisDbContext db, FeatureWriteService writer, TestDataSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);

        foreach (var planned in session.Plan.Regions)
        {
            var region = planned.Region;
            var massif = await writer.CreateGenericAsync(
                new Feature
                {
                    FeatureTypeId = session.Vocabulary.MassifTypeId,
                    Name = region.MassifName,
                    Description = planned.Description,
                    Geom = Polygon(region.Outline()),
                    OwnerUserId = session.OwnerUserId,
                    Visibility = Visibility.Public,
                },
                parents: [], ct);
            Mark(db, session, massif.Id);
            session.Massifs[region.Code] = massif.Id;
            session.Count("massifs");

            var subAreas = new List<Guid>();
            foreach (var sub in planned.SubAreas)
            {
                var a = sub.Area;
                var area = await writer.CreateGenericAsync(
                    new Feature
                    {
                        FeatureTypeId = session.Vocabulary.KarstAreaTypeId,
                        Name = a.Name,
                        Description = sub.Description,
                        Geom = Polygon([(a.West, a.South), (a.East, a.South), (a.East, a.North), (a.West, a.North), (a.West, a.South)]),
                        OwnerUserId = session.OwnerUserId,
                        Visibility = Visibility.Public,
                    },
                    parents: [new ParentSpec(massif.Id, IsPrimary: true)], ct);
                Mark(db, session, area.Id);
                subAreas.Add(area.Id);
                session.Count("karstAreas");
            }

            session.SubAreas[region.Code] = subAreas;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Writes the caves of <paramref name="caves"/> with their entrances, in one save.</summary>
    public static async Task WriteCavesAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        TestDataSession session,
        IReadOnlyList<PlannedCave> caves,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(caves);
        var vocabulary = session.Vocabulary;
        var protectedCaves = new List<Guid>();

        writer.BeginDeferredDerivedState();
        foreach (var planned in caves)
        {
            var parent = planned.SubAreaIndex is { } sub
                ? session.SubAreas[planned.RegionCode][sub]
                : session.Massifs[planned.RegionCode];

            var visibility = planned.Visibility;
            if (visibility == Visibility.CavingGroup)
            {
                // There is no caving group to bind a cave to until the generator makes clubs, so
                // the share asked for as club-only is written as signed-in-only meanwhile.
                visibility = Visibility.Authenticated;
                session.Note("Club-only visibility is written as signed-in-only until the generator creates caving groups.");
            }

            var cave = await writer.CreateCaveAsync(
                new Feature
                {
                    Name = planned.Name,
                    Description = planned.Description,
                    OwnerUserId = session.OwnerUserId,
                    Visibility = visibility,
                },
                new Cave
                {
                    OtherToponyms = planned.OtherToponyms,
                    IdentificationCode = planned.IdentificationCode,
                    CaveTypeId = vocabulary.CaveTypes[planned.CaveTypeCode],
                    RockTypeId = vocabulary.RockTypes.TryGetValue(planned.RockTypeCode, out var rock) ? rock : null,
                    Website = planned.Website,
                    Region = planned.Region,
                    HydrographicBasin = planned.HydrographicBasin,
                    Valley = planned.Valley,
                    TributaryRiver = planned.TributaryRiver,
                    ClosestAddress = planned.ClosestAddress,
                    LandRegistryNumber = planned.LandRegistryNumber,
                    LocationNotes = planned.LocationNotes,
                    RockAge = planned.RockAge,
                    SurveyedLength = planned.SurveyedLength,
                    EstimatedLength = planned.EstimatedLength,
                    RealExtension = planned.RealExtension,
                    ProjectedExtension = planned.ProjectedExtension,
                    Depth = planned.Depth,
                    PositiveDepth = planned.PositiveDepth,
                    NegativeDepth = planned.NegativeDepth,
                    PotentialDepth = planned.PotentialDepth,
                    Altitude = planned.Altitude,
                    Volume = planned.Volume,
                    Area = planned.Area,
                    RamificationIndex = planned.RamificationIndex,
                    CaveAge = planned.CaveAge,
                    ExplorationStatus = planned.ExplorationStatus,
                    ProtectionClass = planned.ProtectionClass,
                    IsShowCave = planned.IsShowCave,
                    ShowCaveLength = planned.ShowCaveLength,
                    DiscoveryDate = planned.DiscoveryDate,
                    Discoverer = planned.Discoverer,
                },
                parents: [new ParentSpec(parent, IsPrimary: true)], ct);
            Mark(db, session, cave.Id);
            session.Caves[planned.Index] = cave.Id;
            session.Count("caves");

            foreach (var e in planned.Entrances)
            {
                var entrance = await writer.CreateEntranceAsync(
                    new Feature
                    {
                        Name = e.Name,
                        Geom = new Point(new CoordinateZ(e.Lon, e.Lat, e.AltitudeM)) { SRID = 4326 },
                        OwnerUserId = session.OwnerUserId,
                    },
                    new CaveEntrance
                    {
                        CaveFeatureId = cave.Id,
                        EntranceTypeId = vocabulary.EntranceTypes[e.EntranceTypeCode],
                        IsMain = e.IsMain,
                        Altitude = (decimal)e.AltitudeM,
                        PositionQuality = e.PositionQuality,
                        SurveyedAt = e.SurveyedAt,
                    },
                    ct);
                Mark(db, session, entrance.Id);
                session.Count("entrances");
            }

            if (planned.LocationProtected)
            {
                protectedCaves.Add(cave.Id);
            }
        }

        // Protection is stamped once the entrances exist, so it reaches them as it would a
        // person's change of the flag on a cave that already has entrances.
        foreach (var id in protectedCaves)
        {
            await writer.SetLocationProtectedAsync(id, true, ct);
            session.Count("protectedCaves");
        }

        await writer.FlushDerivedStateAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lists a generated feature against the run and gives it the marking tag.</summary>
    private static void Mark(SilexGisDbContext db, TestDataSession session, Guid featureId)
    {
        session.Register(db, "features", featureId);
        if (session.MarkingTagId is { } tagId)
        {
            db.Taggings.Add(new Tagging { TagId = tagId, FeatureId = featureId, AddedBy = session.OwnerUserId });
        }
    }

    private static Polygon Polygon(IReadOnlyList<(double Lon, double Lat)> ring) =>
        new(new LinearRing([.. ring.Select(p => new Coordinate(p.Lon, p.Lat))])) { SRID = 4326 };
}
