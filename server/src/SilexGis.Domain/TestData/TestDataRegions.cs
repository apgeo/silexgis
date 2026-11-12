// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>A sub-area of a region (a plateau, a valley side) where caves cluster.</summary>
public sealed record TestDataSubArea(string Name, double West, double South, double East, double North);

/// <summary>
/// A karst massif the generator places invented caves in. The outline, the names around it
/// (villages, valleys, rivers, county, basin) and the rock are those of the real place; the caves
/// are not. Outlines are drawn by hand and approximate.
/// </summary>
public sealed record TestDataRegion(
    string Code,
    string Locale,
    string MassifName,
    string County,
    string Basin,
    string BasinCode,
    double West,
    double South,
    double East,
    double North,
    double SurfaceMinM,
    double SurfaceMaxM,
    IReadOnlyList<double> FractureAzimuthsDeg,
    IReadOnlyDictionary<string, int> RockWeights,
    IReadOnlyList<TestDataSubArea> SubAreas,
    IReadOnlyList<string> Villages,
    IReadOnlyList<string> Valleys,
    IReadOnlyList<string> Rivers)
{
    /// <summary>
    /// The massif's outline: its box with the corners cut, so it reads as an area rather than a
    /// map sheet. Closed (first point repeated last), longitude first.
    /// </summary>
    public IReadOnlyList<(double Lon, double Lat)> Outline()
    {
        var dx = (East - West) * 0.18;
        var dy = (North - South) * 0.18;
        return
        [
            (West + dx, South), (East - dx, South), (East, South + dy), (East, North - dy),
            (East - dx, North), (West + dx, North), (West, North - dy), (West, South + dy), (West + dx, South),
        ];
    }

    public bool Contains(double lon, double lat)
    {
        var ring = Outline();
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (xi, yi) = ring[i];
            var (xj, yj) = ring[j];
            if ((yi > lat) != (yj > lat) && lon < ((xj - xi) * (lat - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>The regions of each language pack.</summary>
public static class TestDataRegions
{
    private static readonly Dictionary<string, int> Limestone = new() { ["limestone"] = 88, ["dolomite"] = 10, ["other"] = 2 };

    /// <summary>
    /// The Romanian pack: massifs inside the Copernicus elevation tiles N45–N47 × E22–E25, so a
    /// terrain build over them has real ground under every generated entrance.
    /// </summary>
    public static IReadOnlyList<TestDataRegion> Romanian { get; } =
    [
        new("bihor-vladeasa", TestDataLocales.Romanian, "Munții Bihor", "Bihor", "Crișul Negru", "2155",
            22.56, 46.47, 22.84, 46.66, 950, 1650, [35, 125],
            new Dictionary<string, int> { ["limestone"] = 85, ["dolomite"] = 12, ["other"] = 3 },
            [
                new("Platoul Padiș", 22.68, 46.57, 22.76, 46.62),
                new("Zona Cetățile Ponorului", 22.74, 46.54, 22.80, 46.58),
                new("Zona Lumea Pierdută", 22.64, 46.52, 22.70, 46.56),
            ],
            ["Pietroasa", "Rieni", "Budureasa", "Arieșeni", "Gârda de Sus", "Scărișoara", "Ghețari", "Casa de Piatră"],
            ["Valea Galbenei", "Valea Sighiștelului", "Valea Ordâncușei", "Valea Ponorului", "Valea Gârdișoarei", "Valea Bulzului"],
            ["Crișul Pietros", "Arieșul Mare", "Gârda Seacă", "Galbena"]),
        new("padurea-craiului", TestDataLocales.Romanian, "Munții Pădurea Craiului", "Bihor", "Crișul Repede", "2141",
            22.26, 46.86, 22.58, 47.04, 350, 950, [20, 110], Limestone,
            [
                new("Platoul Zece Hotare", 22.34, 46.94, 22.42, 46.99),
                new("Zona Vadu Crișului", 22.48, 46.95, 22.55, 47.00),
                new("Zona Roșia", 22.36, 46.87, 22.44, 46.92),
            ],
            ["Șuncuiuș", "Vadu Crișului", "Roșia", "Damiș", "Bratca", "Aușeu", "Lorău", "Câmpani"],
            ["Valea Mișidului", "Valea Roșia", "Valea Albioarei", "Valea Brătcuței", "Valea Iadului", "Valea Topa"],
            ["Crișul Repede", "Iada", "Brătcuța", "Roșia"]),
        new("trascau", TestDataLocales.Romanian, "Munții Trascău", "Alba", "Arieș", "3047",
            23.46, 46.21, 23.70, 46.45, 550, 1250, [10, 100],
            new Dictionary<string, int> { ["limestone"] = 82, ["dolomite"] = 8, ["volcanic"] = 5, ["conglomerate"] = 5 },
            [
                new("Zona Râmeț", 23.48, 46.27, 23.55, 46.32),
                new("Zona Ponor–Colțești", 23.55, 46.38, 23.62, 46.43),
            ],
            ["Râmeț", "Întregalde", "Sălciua", "Remetea", "Ponor", "Colțești", "Rimetea", "Valea Mănăstirii"],
            ["Valea Râmețului", "Valea Galdei", "Valea Ponorului", "Cheile Turenilor", "Valea Geoagiului"],
            ["Arieș", "Galda", "Geoagiu", "Râmeț"]),
        new("cerna", TestDataLocales.Romanian, "Munții Cernei", "Caraș-Severin", "Cerna", "2233",
            22.40, 45.02, 22.72, 45.22, 300, 1400, [45, 135],
            new Dictionary<string, int> { ["limestone"] = 82, ["marble"] = 6, ["dolomite"] = 10, ["other"] = 2 },
            [
                new("Zona Cerna-Sat", 22.60, 45.13, 22.68, 45.19),
                new("Zona Iuți", 22.50, 45.06, 22.57, 45.11),
            ],
            ["Cerna-Sat", "Băile Herculane", "Pecinișca", "Mehadia", "Prisacina", "Cornereva", "Ineleț"],
            ["Valea Cernei", "Valea Iuților", "Valea Țesnei", "Valea Mehadica", "Valea Prisacinei"],
            ["Cerna", "Belareca", "Mehadica", "Țesna"]),
        new("sureanu", TestDataLocales.Romanian, "Munții Șureanu", "Hunedoara", "Strei", "2245",
            23.06, 45.50, 23.26, 45.62, 650, 1250, [30, 120], Limestone,
            [
                new("Platoul Ohaba-Ponor", 23.10, 45.54, 23.16, 45.58),
                new("Zona Cioclovina", 23.12, 45.57, 23.19, 45.61),
            ],
            ["Ohaba-Ponor", "Cioclovina", "Boșorod", "Pui", "Federi", "Ponorici", "Luncani"],
            ["Valea Ponorici", "Valea Luncanilor", "Valea Federi", "Valea Ohabei"],
            ["Strei", "Ponorici", "Luncanilor"]),
        new("retezat", TestDataLocales.Romanian, "Retezatul Mic", "Hunedoara", "Jiu", "2312",
            22.76, 45.30, 23.00, 45.40, 1150, 2050, [60, 150],
            new Dictionary<string, int> { ["limestone"] = 94, ["dolomite"] = 5, ["other"] = 1 },
            [
                new("Zona Piule–Iorgovanul", 22.84, 45.33, 22.92, 45.37),
                new("Zona Scocul Jiului", 22.78, 45.31, 22.84, 45.35),
            ],
            ["Câmpu lui Neag", "Uricani", "Lupeni", "Cârnic"],
            ["Valea Jiului de Vest", "Valea Scocului", "Valea Buta", "Valea Soarbele"],
            ["Jiul de Vest", "Buta", "Soarbele"]),
        new("piatra-craiului", TestDataLocales.Romanian, "Piatra Craiului", "Brașov", "Dâmbovița", "2511",
            25.15, 45.46, 25.31, 45.60, 900, 2200, [25, 115],
            new Dictionary<string, int> { ["limestone"] = 88, ["conglomerate"] = 10, ["other"] = 2 },
            [
                new("Culmea Pietrei Craiului", 25.20, 45.50, 25.27, 45.57),
                new("Platoul Dâmbovicioara", 25.22, 45.46, 25.29, 45.50),
            ],
            ["Zărnești", "Măgura", "Peștera", "Plaiul Foii", "Dâmbovicioara", "Ciocanu", "Podu Dâmboviței"],
            ["Valea Dâmbovicioarei", "Cheile Zărneștilor", "Valea Bârsei", "Valea Crăpăturii", "Valea Seacă"],
            ["Dâmbovicioara", "Bârsa Mare", "Dâmbovița"]),
        new("bucegi", TestDataLocales.Romanian, "Munții Bucegi", "Dâmbovița", "Ialomița", "2621",
            25.40, 45.35, 25.52, 45.47, 1250, 2350, [0, 90],
            new Dictionary<string, int> { ["limestone"] = 52, ["conglomerate"] = 44, ["sandstone"] = 4 },
            [
                new("Zona Ialomicioara", 25.42, 45.40, 25.47, 45.44),
                new("Platoul Bucegi", 25.45, 45.38, 25.50, 45.43),
            ],
            ["Moieciu", "Bran", "Sinaia", "Bușteni", "Peștera", "Pucheni", "Moroeni"],
            ["Valea Ialomiței", "Valea Cerbului", "Valea Gaura", "Valea Horoabei", "Valea Doamnelor"],
            ["Ialomița", "Ialomicioara", "Horoaba"]),
        new("postavarul", TestDataLocales.Romanian, "Masivul Postăvarul", "Brașov", "Bârsa", "2523",
            25.50, 45.54, 25.63, 45.62, 700, 1700, [15, 105], Limestone,
            [
                new("Zona Răcădău", 25.55, 45.58, 25.60, 45.61),
            ],
            ["Brașov", "Râșnov", "Cristian", "Poiana Brașov", "Timișu de Jos"],
            ["Valea Timișului", "Valea Râșnoavei", "Valea Răcădăului", "Valea Lungă"],
            ["Timiș", "Râșnoava", "Ghimbășel"]),
        new("praid", TestDataLocales.Romanian, "Diapirul Praid", "Harghita", "Târnava Mică", "3122",
            25.10, 46.52, 25.17, 46.57, 500, 750, [70, 160],
            new Dictionary<string, int> { ["salt"] = 75, ["gypsum"] = 25 },
            [
                new("Zona Praid", 25.12, 46.53, 25.15, 46.56),
            ],
            ["Praid", "Ocna de Jos", "Ocna de Sus", "Sovata"],
            ["Valea Corundului", "Valea Sării", "Pârâul Sărat"],
            ["Corund", "Târnava Mică"]),
    ];

    /// <summary>Whether a language pack's regions and names are available.</summary>
    public static bool HasPack(string locale) => locale == TestDataLocales.Romanian;

    public static IReadOnlyList<TestDataRegion> ForLocale(string locale) =>
        locale == TestDataLocales.Romanian ? Romanian : [];

    public static TestDataRegion? Find(string code) => Romanian.FirstOrDefault(r => r.Code == code);
}
