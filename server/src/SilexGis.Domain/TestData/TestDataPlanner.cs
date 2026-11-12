// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>
/// Works out the whole world a run will write, from the profile alone. Pure and deterministic:
/// the same profile always gives the same plan, which is what makes a run repeatable and what the
/// tests compare.
/// </summary>
public static class TestDataPlanner
{
    public static TestDataWorldPlan Plan(TestDataProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var seed = profile.Seed;
        var regions = Regions(profile);
        var marking = profile.Marking;

        var plannedRegions = regions
            .Select(region =>
            {
                var rng = TestDataRng.For(seed, "region:" + region.Code);
                return new PlannedRegion(
                    region,
                    Mark(TestDataTextRo.AreaDescription(rng, region.MassifName, region.County), marking),
                    [.. region.SubAreas.Select(a => new PlannedSubArea(
                        a, Mark(TestDataTextRo.AreaDescription(rng, a.Name, region.County), marking)))]);
            })
            .ToList();

        return new TestDataWorldPlan(profile, plannedRegions, Caves(profile, regions));
    }

    /// <summary>The regions a profile places things in, in a fixed order.</summary>
    public static IReadOnlyList<TestDataRegion> Regions(TestDataProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var pack = TestDataRegions.ForLocale(profile.Locale);
        return profile.Regions is { Count: > 0 } chosen
            ? [.. pack.Where(r => chosen.Contains(r.Code))]
            : pack;
    }

    /// <summary>
    /// The ground height of a region at a point: a smooth invented surface between the region's
    /// lowest and highest ground. A terrain build, where one exists, gives the real height instead.
    /// </summary>
    public static double SurfaceHeight(TestDataRegion region, long seed, double lon, double lat)
    {
        ArgumentNullException.ThrowIfNull(region);
        var rng = TestDataRng.For(seed, "surface:" + region.Code);
        var (p1, p2, p3) = (rng.Between(0, 6.28), rng.Between(0, 6.28), rng.Between(0, 6.28));
        var xKm = (lon - region.West) * 111.32 * Math.Cos(lat * Math.PI / 180);
        var yKm = (lat - region.South) * 110.57;
        var shape = (0.55 * Math.Sin((xKm / 3.1) + p1) * Math.Cos((yKm / 2.7) + p2))
            + (0.45 * Math.Sin(((xKm + yKm) / 5.3) + p3));
        var unit = Math.Clamp((shape + 1) / 2, 0, 1);
        return region.SurfaceMinM + (unit * (region.SurfaceMaxM - region.SurfaceMinM));
    }

    private static List<PlannedCave> Caves(TestDataProfile profile, IReadOnlyList<TestDataRegion> regions)
    {
        var seed = profile.Seed;
        var count = profile.Caves.Count;
        if (regions.Count == 0 || count == 0)
        {
            return [];
        }

        var quotaRng = TestDataRng.For(seed, "caves:quotas");
        var v = profile.Access.VisibilityPercent;
        var visibility = TestDataQuota.Allocate(count,
        [
            (Visibility.Public, v.Public), (Visibility.Authenticated, v.Authenticated),
            (Visibility.CavingGroup, v.CavingGroup), (Visibility.Private, v.Private),
        ], quotaRng);
        var protectedFlags = TestDataQuota.Flags(count, profile.Access.LocationProtectedCavesPercent, quotaRng);
        var multiEntrance = TestDataQuota.Flags(count, profile.Caves.MultiEntrancePercent, quotaRng);
        var types = TestDataQuota.Allocate(count,
            [.. profile.Caves.TypeWeights.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => (w.Key, (double)w.Value))],
            quotaRng);

        // A show cave or two in any run large enough to have a long cave worth showing.
        var showCaves = Math.Max(count >= 50 ? 1 : 0, count / 250);

        var takenNames = new HashSet<string>(StringComparer.Ordinal);
        var nextNumber = regions.ToDictionary(r => r.Code, _ => 0);
        var caves = new List<PlannedCave>(count);

        for (var i = 0; i < count; i++)
        {
            var rng = TestDataRng.For(seed, "cave", i);

            // Regions in turn, weighted towards the larger ones by how many sub-areas they have.
            var region = PickRegion(rng, regions);
            var type = types[i];
            var rock = rng.Weighted(region.RockWeights);

            var (lon, lat, subArea) = Place(rng, region);
            var altitude = SurfaceHeight(region, seed, lon, lat);

            var name = TestDataNamesRo.Distinct(TestDataNamesRo.CaveName(rng, type, region), takenNames, rng);
            nextNumber[region.Code] += rng.Between(1, 3);
            var code = $"{region.BasinCode}/{nextNumber[region.Code]}";

            var measures = Measures(rng, type, profile.Caves);
            var status = Status(rng, measures.Length);
            var isShowCave = showCaves > 0 && type == "cave" && measures.Length >= 300 && rng.Chance(0.25);
            if (isShowCave)
            {
                showCaves--;
            }

            var entranceCount = multiEntrance[i] ? rng.Between(2, type == "pit" ? 2 : 4) : 1;
            var entrances = Entrances(rng, region, seed, lon, lat, altitude, entranceCount, type);
            var valley = rng.Pick(region.Valleys);
            var village = rng.Pick(region.Villages);

            var facts = new PlannedCaveFacts(type, region.MassifName, valley, altitude, measures.Length, measures.Depth, status == ExplorationStatus.Ongoing);
            caves.Add(new PlannedCave
            {
                Index = i,
                RegionCode = region.Code,
                SubAreaIndex = subArea,
                Name = name,
                IdentificationCode = code,
                CaveTypeCode = type,
                RockTypeCode = rock,
                Visibility = visibility[i],
                LocationProtected = protectedFlags[i],
                Entrances = entrances,
                Description = Mark(TestDataTextRo.CaveDescription(rng, facts), profile.Marking),
                OtherToponyms = rng.Chance(0.2) ? TestDataNamesRo.OtherToponym(rng, region) : null,
                Website = isShowCave ? $"https://www.example.org/pesteri/{Slug(name)}" : null,
                Region = region.County,
                HydrographicBasin = region.Basin,
                Valley = valley,
                TributaryRiver = rng.Chance(0.7) ? rng.Pick(region.Rivers) : null,
                ClosestAddress = rng.Chance(0.8) ? $"{village}, jud. {region.County}" : null,
                LandRegistryNumber = rng.Chance(0.1) ? $"CF {rng.Between(10_000, 99_999)}" : null,
                LocationNotes = rng.Chance(0.5) ? TestDataTextRo.LocationNotes(rng, village, valley) : null,
                RockAge = rng.Chance(0.6) ? rng.Pick(TestDataVocabulary.RockAgesRo) : null,
                SurveyedLength = measures.Length,
                EstimatedLength = status == ExplorationStatus.Ongoing && measures.Length is { } l
                    ? Round(l * (decimal)rng.Between(1.2, 3.0)) : null,
                RealExtension = measures.RealExtension,
                ProjectedExtension = measures.ProjectedExtension,
                Depth = measures.Depth,
                PositiveDepth = measures.PositiveDepth,
                NegativeDepth = measures.NegativeDepth,
                PotentialDepth = measures.Depth is { } d && (type == "pit" || status == ExplorationStatus.Ongoing) && rng.Chance(0.25)
                    ? Round(d * (decimal)rng.Between(1.2, 2.5)) : null,
                Altitude = Round((decimal)altitude),
                Volume = measures.Volume,
                Area = measures.Area,
                RamificationIndex = measures.RamificationIndex,
                CaveAge = rng.Chance(0.03) ? rng.Between(50, 2000) * 1000 : null,
                ExplorationStatus = status,
                ProtectionClass = rng.Chance(0.4) ? rng.Weighted(new Dictionary<string, int> { ["A"] = 5, ["B"] = 25, ["C"] = 40, ["D"] = 30 }) : null,
                IsShowCave = isShowCave,
                ShowCaveLength = isShowCave && measures.Length is { } shown ? Round(shown * 0.4m) : null,
                DiscoveryDate = rng.Chance(0.85) ? DiscoveryDate(rng) : null,
                Discoverer = rng.Chance(0.75) ? Discoverer(rng, village) : null,
            });
        }

        return caves;
    }

    private static TestDataRegion PickRegion(TestDataRng rng, IReadOnlyList<TestDataRegion> regions)
    {
        var weights = regions.ToDictionary(r => r.Code, r => 2 + r.SubAreas.Count);
        var code = rng.Weighted(weights);
        return regions.First(r => r.Code == code);
    }

    /// <summary>Most caves cluster in the sub-areas (plateaus, valley sides); the rest anywhere in the massif.</summary>
    private static (double Lon, double Lat, int? SubArea) Place(TestDataRng rng, TestDataRegion region)
    {
        if (region.SubAreas.Count > 0 && rng.Chance(0.7))
        {
            var index = rng.Next(region.SubAreas.Count);
            var a = region.SubAreas[index];
            return (rng.Between(a.West, a.East), rng.Between(a.South, a.North), index);
        }

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var lon = rng.Between(region.West, region.East);
            var lat = rng.Between(region.South, region.North);
            if (region.Contains(lon, lat))
            {
                return (lon, lat, null);
            }
        }

        return ((region.West + region.East) / 2, (region.South + region.North) / 2, null);
    }

    private sealed record CaveMeasures(
        decimal? Length,
        decimal? Depth,
        decimal? PositiveDepth,
        decimal? NegativeDepth,
        decimal? RealExtension,
        decimal? ProjectedExtension,
        decimal? Area,
        decimal? Volume,
        decimal? RamificationIndex);

    /// <summary>
    /// Length and depth by kind of cave. Provisional: once caves get surveys of their own, these
    /// are computed from the survey instead of drawn.
    /// </summary>
    private static CaveMeasures Measures(TestDataRng rng, string type, TestDataCaveProfile caves)
    {
        double length;
        double depth;
        double positiveShare;
        switch (type)
        {
            case "pit":
                depth = rng.LogNormal(25, 1.0, 6, 650);
                length = depth * rng.Between(1.1, 2.5);
                positiveShare = 0;
                break;
            case "rock_shelter":
                length = rng.Between(3.0, 25.0);
                depth = rng.Between(0.0, 3.0);
                positiveShare = 0.5;
                break;
            case "mine":
                length = rng.Between(20.0, 1500.0);
                depth = rng.Between(0.0, 60.0);
                positiveShare = 0.3;
                break;
            case "spring_cave":
                length = rng.LogNormal(caves.LengthMedianM * 0.8, caves.LengthSigma, 5, caves.MaxLengthM);
                depth = Math.Min(length * rng.Between(0.02, 0.15), 250);
                positiveShare = 0.8;
                break;
            default:
                length = rng.LogNormal(caves.LengthMedianM, caves.LengthSigma, 5, caves.MaxLengthM);
                depth = Math.Min(length * rng.Between(0.03, 0.25), 900);
                positiveShare = rng.Between(0.0, 0.4);
                break;
        }

        var positive = depth * positiveShare;
        var real = length > 100 ? length * rng.Between(0.3, 0.7) : (double?)null;
        var hasVolume = length > 200 && rng.Chance(0.5);
        var area = hasVolume ? length * rng.Between(2.0, 8.0) : (double?)null;

        return new CaveMeasures(
            Round((decimal)length),
            Round((decimal)depth),
            Round((decimal)positive),
            Round((decimal)(depth - positive)),
            real is { } r ? Round((decimal)r) : null,
            real is { } p ? Round((decimal)(p * rng.Between(0.85, 1.0))) : null,
            area is { } a ? Round((decimal)a) : null,
            area is { } av ? Round((decimal)(av * rng.Between(2.0, 10.0))) : null,
            length > 300 ? Math.Round((decimal)rng.Between(1.0, 3.0), 2) : null);
    }

    private static ExplorationStatus Status(TestDataRng rng, decimal? length) =>
        rng.Weighted(new Dictionary<string, int>
        {
            ["finished"] = 45,
            ["ongoing"] = length > 500 ? 45 : 20,
            ["unknown"] = 20,
            ["abandoned"] = 10,
        }) switch
        {
            "finished" => ExplorationStatus.Finished,
            "ongoing" => ExplorationStatus.Ongoing,
            "abandoned" => ExplorationStatus.Abandoned,
            _ => ExplorationStatus.Unknown,
        };

    private static List<PlannedEntrance> Entrances(
        TestDataRng rng, TestDataRegion region, long seed, double lon, double lat, double altitude, int count, string type)
    {
        var entrances = new List<PlannedEntrance>(count);
        for (var e = 0; e < count; e++)
        {
            var (eLon, eLat, eAlt) = (lon, lat, altitude);
            if (e > 0)
            {
                // Within a few hundred metres of the main one, as a second way into the same cave is.
                var distanceM = rng.Between(30.0, 400.0);
                var bearing = rng.Between(0, 2 * Math.PI);
                eLat = lat + (distanceM * Math.Cos(bearing) / 110_570);
                eLon = lon + (distanceM * Math.Sin(bearing) / (111_320 * Math.Cos(lat * Math.PI / 180)));
                eAlt = SurfaceHeight(region, seed, eLon, eLat);
            }

            var quality = rng.Weighted(new Dictionary<string, int> { ["gps"] = 60, ["map"] = 20, ["estimated"] = 10, ["unknown"] = 10 }) switch
            {
                "gps" => PositionQuality.Gps,
                "map" => PositionQuality.Map,
                "estimated" => PositionQuality.Estimated,
                _ => PositionQuality.Unknown,
            };

            entrances.Add(new PlannedEntrance(
                TestDataNamesRo.EntranceName(e),
                eLon,
                eLat,
                Math.Round(eAlt, 1),
                type == "mine" ? TestDataVocabulary.ArtificialEntrance : rng.Weighted(TestDataVocabulary.NaturalEntranceWeights),
                e == 0,
                quality,
                quality == PositionQuality.Gps ? new DateOnly(rng.Between(2005, 2025), rng.Between(1, 12), rng.Between(1, 28)) : null));
        }

        return entrances;
    }

    private static string DiscoveryDate(TestDataRng rng)
    {
        var year = (int)Math.Round(1952 + (73 * Math.Pow(rng.NextDouble(), 0.8)));
        return rng.Next(10) switch
        {
            < 6 => year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            < 9 => new DateOnly(year, rng.Between(1, 12), rng.Between(1, 28)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => rng.Pick(["anii '60", "anii '70", "înainte de 1950", "cunoscută de localnici"]),
        };
    }

    private static string Discoverer(TestDataRng rng, string village) =>
        rng.Next(10) switch
        {
            < 7 => TestDataNamesRo.PersonName(rng),
            < 9 => $"{TestDataNamesRo.PersonName(rng)}, {TestDataNamesRo.PersonName(rng)}",
            _ => $"localnici din {village}",
        };

    private static string Mark(string text, TestDataMarkingProfile marking) =>
        marking.DescriptionFooter ? $"{text}\n\n{TestDataTextRo.Footer(marking.Tag)}" : text;

    private static decimal Round(decimal value) => Math.Round(value, 1);

    private static string Slug(string name) => Entities.Tag.Slugify(name);
}
