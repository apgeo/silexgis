// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>
/// Romanian naming for invented caves, entrances and people, in the forms a Romanian cave
/// register uses: a generic word for the kind of cave, then a genitive, an adjective, a place,
/// a person or a description. Agreement follows the generic word's gender.
/// </summary>
public static class TestDataNamesRo
{
    private sealed record Generic(string Word, bool Feminine);

    private static readonly Generic Pestera = new("Peștera", true);
    private static readonly Generic Huda = new("Huda", true);
    private static readonly Generic Gaura = new("Gaura", true);
    private static readonly Generic Avenul = new("Avenul", false);
    private static readonly Generic Hornul = new("Hornul", false);
    private static readonly Generic Putul = new("Puțul", false);
    private static readonly Generic Ponorul = new("Ponorul", false);
    private static readonly Generic Izbucul = new("Izbucul", false);
    private static readonly Generic Coliba = new("Coliba", true);
    private static readonly Generic Adapostul = new("Adăpostul", false);
    private static readonly Generic Abriul = new("Abriul", false);
    private static readonly Generic Mina = new("Mina", true);
    private static readonly Generic Galeria = new("Galeria", true);

    private static readonly IReadOnlyDictionary<string, Generic[]> GenericsByType = new Dictionary<string, Generic[]>
    {
        ["cave"] = [Pestera, Pestera, Pestera, Pestera, Pestera, Huda, Gaura, Ponorul],
        ["pit"] = [Avenul, Avenul, Avenul, Avenul, Hornul, Putul],
        ["spring_cave"] = [Izbucul, Izbucul, Pestera],
        ["rock_shelter"] = [Adapostul, Coliba, Abriul],
        ["mine"] = [Mina, Galeria],
    };

    private static readonly string[] Genitives =
    [
        "Ursului", "Liliecilor", "Vântului", "Lupului", "Zmeilor", "Oaselor", "Dracului", "Haiducilor",
        "Vulturilor", "Corbului", "Cerbului", "Ciobanului", "Popii", "Fetei", "Mânzului", "Bourului",
        "Șarpelui", "Caprei", "Iepurelui", "Bufniței", "Cucului", "Hoților", "Comorii", "Zânelor",
        "Uriașilor", "Sihastrului", "Morii", "Stânii", "Izvorului", "Fântânii", "Cetății", "Bisericii",
        "Ciorii", "Bradului", "Fagului", "Jderului", "Râsului", "Vidrei", "Porumbeilor", "Lăstunilor",
        "Turcilor", "Tâlharilor", "Moșului", "Babei", "Ciutei", "Pârâului", "Stâncii", "Văii",
    ];

    // Masculine form, feminine form.
    private static readonly (string M, string F)[] Adjectives =
    [
        ("Mare", "Mare"), ("Mic", "Mică"), ("Rece", "Rece"), ("Negru", "Neagră"), ("Alb", "Albă"),
        ("Uscat", "Uscată"), ("Ascuns", "Ascunsă"), ("Înalt", "Înaltă"), ("Strâmt", "Strâmtă"),
        ("Lung", "Lungă"), ("Vechi", "Veche"), ("Nou", "Nouă"), ("Rotund", "Rotundă"),
        ("Întunecat", "Întunecată"), ("Umed", "Umedă"), ("Roșu", "Roșie"), ("Surpat", "Surpată"),
        ("Fierbinte", "Fierbinte"), ("Galben", "Galbenă"), ("Adânc", "Adâncă"),
    ];

    private static readonly string[] PlacesDeLa =
    [
        "Fântâna Rece", "Izvorul Rece", "Stâna din Deal", "Colții Albi", "Poiana Mare", "Piatra Albă",
        "Piatra Seacă", "Moara Veche", "Crucea Mare", "Fântâna Ursului", "Izvorul Tăuz", "Gura Văii",
        "Brazii Mari", "Podul Natural", "Cascada Mare", "Colțul Pietrei", "Strunga Mică", "Tăul Negru",
    ];

    private static readonly string[] PlacesDin =
    [
        "Dealul Bisericii", "Valea Seacă", "Șesuri", "Fața Muntelui", "Groapa Mare", "Coasta Lupului",
        "Poiana Ponor", "Dealul Mare", "Piatra Lungă", "Vârful Gol", "Muchia Rea", "Peretele Alb",
        "Ogașul Rece", "Groapa de la Barsa", "Vâlceaua Seacă", "Plaiul Ursului",
    ];

    private static readonly string[] Descriptions =
    [
        "Apă", "Cristale", "Gururi", "Oase", "Lac", "Cascadă", "Gheață", "Coloane", "Argilă", "Perle",
        "Bolovani", "Ferestre", "Două Intrări", "Sifon", "Ecou", "Brumă", "Stalactite",
    ];

    private static readonly string[] Persons =
    [
        "Ionele", "Gavrilă", "Popa Toader", "Moș Ion", "Lazăr", "Pavel", "Moș Vasile", "Bucur", "Avram",
        "Iancu", "Mihuț", "Todoran", "Bârsan", "Petru Rareș", "Muntean", "Ghiță", "Drăgan",
    ];

    private static readonly string[] EntranceNames =
    [
        "Intrarea principală", "Intrarea superioară", "Intrarea inferioară", "Gura de sus", "Gura de jos",
        "Intrarea din dolină", "Intrarea fosilă", "Intrarea activă", "Fereastra", "Puțul de intrare",
    ];

    /// <summary>A cave name for a cave of <paramref name="caveType"/> in <paramref name="region"/>.</summary>
    public static string CaveName(TestDataRng rng, string caveType, TestDataRegion region)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(region);
        var generic = rng.Pick(GenericsByType.TryGetValue(caveType, out var g) ? g : GenericsByType["cave"]);

        if (caveType == "mine")
        {
            return rng.Next(3) switch
            {
                0 => $"{generic.Word} de la {rng.Pick(region.Villages)}",
                1 => $"{generic.Word} {rng.Pick(Adjectives).F}",
                _ => $"{generic.Word} {rng.Pick(["Sf. Ioan", "Sf. Maria", "Sf. Gheorghe", "Carol", "Elisabeta", "Iosif", "Nr. 2", "Nr. 4"])}",
            };
        }

        var roll = rng.Next(100);
        return roll switch
        {
            < 34 => $"{generic.Word} {rng.Pick(Genitives)}",
            < 50 => $"{generic.Word} {Adjective(rng, generic)}",
            < 60 => $"{generic.Word} de la {rng.Pick(PlacesDeLa)}",
            < 70 => $"{generic.Word} din {rng.Pick(PlacesDin)}",
            < 78 => $"{generic.Word} cu {rng.Pick(Descriptions)}",
            < 84 => $"{generic.Word} lui {rng.Pick(Persons)}",
            < 92 => $"{generic.Word} de la {rng.Pick(region.Villages)}",
            < 96 => $"{generic.Word} din {rng.Pick(region.Valleys)}",
            _ => $"{generic.Word} {rng.Pick(Genitives)} {Adjective(rng, generic)}",
        };
    }

    /// <summary>Another name for the same cave, as registers keep them: a local or an older name.</summary>
    public static string OtherToponym(TestDataRng rng, TestDataRegion region)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(region);
        return rng.Next(3) switch
        {
            0 => $"Gaura {rng.Pick(Genitives)}",
            1 => $"Peștera de la {rng.Pick(region.Villages)}",
            _ => $"Huda {rng.Pick(Persons)}",
        };
    }

    /// <summary>Makes a name unique among those already given, the way registers do: by a qualifier or a number.</summary>
    public static string Distinct(string name, ISet<string> taken, TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(rng);
        if (taken.Add(name))
        {
            return name;
        }

        foreach (var qualifier in new[] { "de Sus", "de Jos", "Nouă", "Mică", "Mare", "II" })
        {
            var candidate = $"{name} {qualifier}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{name} {n}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    public static string EntranceName(int index) =>
        index < EntranceNames.Length ? EntranceNames[index] : $"Intrarea {index + 1}";

    private static readonly string[] MaleGiven =
    [
        "Ion", "Mihai", "Andrei", "Alexandru", "Gheorghe", "Vasile", "Constantin", "Dan", "Florin",
        "Radu", "Cristian", "Adrian", "Bogdan", "Sorin", "Marius", "Lucian", "Ovidiu", "Horia", "Tudor",
        "Paul", "Dorin", "Cornel", "Liviu", "Nicolae", "Emil", "Iulian", "Răzvan", "Victor", "Ștefan",
    ];

    private static readonly string[] FemaleGiven =
    [
        "Maria", "Elena", "Ioana", "Ana", "Andreea", "Mihaela", "Cristina", "Daniela", "Alina", "Irina",
        "Raluca", "Oana", "Laura", "Simona", "Monica", "Adriana", "Camelia", "Diana", "Ileana", "Corina",
    ];

    private static readonly string[] Surnames =
    [
        "Popescu", "Ionescu", "Pop", "Popa", "Rusu", "Muntean", "Stan", "Oprea", "Moldovan", "Lazăr",
        "Mureșan", "Dumitru", "Matei", "Constantin", "Ciobanu", "Toma", "Barbu", "Avram", "Bogdan",
        "Sârbu", "Crișan", "Nistor", "Suciu", "Pavel", "Rădulescu", "Florea", "Bălan", "Câmpean",
        "Marian", "Coman", "Roman", "Tudor", "Ardelean", "Morar", "Vlad", "Iordache", "Cozma", "Ene",
        "Cristea", "Dinu", "Bucur", "Groza", "Lungu", "Ungureanu", "Oltean", "Pintea", "Tămaș", "Boca",
    ];

    /// <summary>A plausible Romanian full name, given name first.</summary>
    public static string PersonName(TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var given = rng.Chance(0.3) ? rng.Pick(FemaleGiven) : rng.Pick(MaleGiven);
        return $"{given} {rng.Pick(Surnames)}";
    }

    private static string Adjective(TestDataRng rng, Generic generic)
    {
        var (m, f) = rng.Pick(Adjectives);
        return generic.Feminine ? f : m;
    }
}
