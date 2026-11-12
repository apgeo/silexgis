// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.TestData;

/// <summary>
/// Romanian descriptive text for invented caves and areas, assembled from sentences that take the
/// cave's own numbers, so the text agrees with the fields beside it.
/// </summary>
public static class TestDataTextRo
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static string CaveDescription(TestDataRng rng, PlannedCaveFacts cave)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(cave);
        var parts = new List<string>();

        parts.Add(cave.CaveTypeCode switch
        {
            "pit" => rng.Pick(
            [
                $"Aven cu deschidere de {rng.Between(1, 6)} × {rng.Between(1, 4)} m, situat pe {rng.Pick(Slopes)} din {cave.Valley ?? cave.Massif}.",
                $"Avenul se deschide la marginea unei doline, sub un prag de calcar, la {M(cave.Altitude)} altitudine.",
            ]),
            "spring_cave" => $"Izbuc permanent la baza versantului, cu debit estimat la {rng.Between(5, 120)} l/s în perioadele de ape mari.",
            "rock_shelter" => $"Adăpost sub stâncă, larg de {rng.Between(4, 18)} m, folosit vreme îndelungată de ciobani.",
            "mine" => "Galerie de mină veche, săpată în calcar, cu urme de exploatare și vagonete abandonate.",
            _ => rng.Pick(
            [
                $"Peșteră situată în {cave.Valley ?? cave.Massif}, la circa {rng.Between(10, 120)} m deasupra talvegului.",
                $"Intrarea, de formă {rng.Pick(Shapes)}, se află la baza unui perete de calcar, la {M(cave.Altitude)} altitudine.",
                $"Cavitatea se deschide într-o dolină largă, la {rng.Between(200, 1500)} m de cel mai apropiat drum forestier.",
            ]),
        });

        if (cave.Length is { } length && cave.CaveTypeCode != "rock_shelter")
        {
            parts.Add(cave.CaveTypeCode == "pit"
                ? $"Verticala principală măsoară {M(cave.Depth)}; după baza puțului urmează un meandru îngust, colmatat după {rng.Between(5, 60)} m. Dezvoltarea totală cartată este de {M(length)}."
                : $"Galeria principală, cu secțiune {rng.Pick(Sections)}, se dezvoltă pe {M(length)} cartați, cu o denivelare de {M(cave.Depth)}.");
        }

        if (rng.Chance(0.6))
        {
            parts.Add(rng.Pick(Hydrology));
        }

        if (rng.Chance(0.5))
        {
            parts.Add(rng.Pick(Fauna));
        }

        if (cave.Ongoing)
        {
            parts.Add(rng.Pick(
            [
                "Explorarea continuă: la capătul galeriei active curentul de aer indică o continuare.",
                "Un horn neescaladat din sala terminală rămâne obiectiv pentru tura următoare.",
                "Sifonul terminal nu a fost încă scufundat.",
            ]));
        }

        return string.Join(" ", parts);
    }

    public static string AreaDescription(TestDataRng rng, string name, string county)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Pick(
        [
            $"{name}: zonă carstică în județul {county}, cu doline, ponoare și izbucuri.",
            $"{name}, platou carstic cu numeroase doline aliniate pe direcția fracturilor principale (jud. {county}).",
            $"Arie carstică ({name}) delimitată după harta geologică, jud. {county}.",
        ]);
    }

    public static string LocationNotes(TestDataRng rng, string village, string valley)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return rng.Pick(
        [
            $"Din {village} se urcă pe {valley} circa {rng.Between(2, 9)} km, apoi pe poteca marcată cu punct roșu până la stâna părăsită; intrarea este la {rng.Between(50, 400)} m spre nord-est.",
            $"Acces din {village} pe drumul forestier de pe {valley}; de la barieră, {rng.Between(20, 90)} de minute de mers pe jos.",
            $"Se pornește din {village}; din șaua de deasupra văii ({valley}) se coboară în dolina mare, intrarea fiind pe latura vestică.",
        ]);
    }

    /// <summary>The line every generated description ends with, so nobody takes it for a real register entry.</summary>
    public static string Footer(string tag) => $"— Date de test generate automat ({tag}).";

    private static string M(decimal? metres) =>
        metres is { } m ? string.Create(Ro, $"{m:0.#} m") : "— m";

    private static string M(double metres) => string.Create(Ro, $"{metres:0.#} m");

    private static readonly string[] Slopes = ["versantul estic", "versantul vestic", "culmea", "fundul văii", "marginea platoului"];

    private static readonly string[] Shapes = ["triunghiulară", "ovală", "de fereastră", "de portal larg", "de fisură"];

    private static readonly string[] Sections = ["de conductă forțată", "de canion", "în formă de gaură de cheie", "joasă, laminară", "înaltă, de diaclază"];

    private static readonly string[] Hydrology =
    [
        "Un pârâu subteran străbate partea inferioară și se pierde într-un sifon.",
        "Galeria este fosilă; apa apare doar ca picurări în sala principală.",
        "La viituri, intrarea funcționează ca ponor și primește apele dolinei.",
        "Apele din peșteră reapar, după trasări cu fluoresceină, în izbucul din vale.",
    ];

    private static readonly string[] Fauna =
    [
        "Adăpostește o colonie de lilieci de câteva sute de exemplare în sezonul cald.",
        "În bazinele cu apă au fost observați amfipozi (Niphargus).",
        "Pe podea se găsesc resturi osoase de urs de cavernă.",
        "Fauna terestră include colembole și coleoptere troglobionte.",
    ];
}

/// <summary>The facts about a cave its description is written from.</summary>
public sealed record PlannedCaveFacts(
    string CaveTypeCode,
    string Massif,
    string? Valley,
    double Altitude,
    decimal? Length,
    decimal? Depth,
    bool Ongoing);
