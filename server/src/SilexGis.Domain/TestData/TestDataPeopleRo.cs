// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>A real town clubs are based in, with the massif its cavers go to most.</summary>
public sealed record TestDataTown(string Name, string County, double Lon, double Lat, string HomeRegionCode, double HungarianShare, int Weight);

/// <summary>A country foreign guests come from: how their names are made, and the language and clock their accounts use.</summary>
public sealed record TestDataGuestCountry(
    string Country,
    string Locale,
    string TimeZone,
    IReadOnlyList<string> MaleGiven,
    IReadOnlyList<string> FemaleGiven,
    IReadOnlyList<string> Surnames,
    IReadOnlyList<string> Towns);

/// <summary>
/// Romanian naming for invented people and clubs. Towns are real, so a club reads as one of
/// somewhere; club names are invented in the usual patterns, from qualifiers no real club is
/// known by, so a generated club is never mistaken for, or taken to speak for, a real one.
/// </summary>
public static class TestDataPeopleRo
{
    public static IReadOnlyList<TestDataTown> Towns { get; } =
    [
        new("Cluj-Napoca", "Cluj", 23.590, 46.770, "bihor-vladeasa", 0.25, 10),
        new("Oradea", "Bihor", 21.919, 47.047, "padurea-craiului", 0.30, 8),
        new("Timișoara", "Timiș", 21.229, 45.756, "cerna", 0.05, 7),
        new("Brașov", "Brașov", 25.606, 45.652, "postavarul", 0.05, 7),
        new("București", "București", 26.103, 44.427, "bucegi", 0.0, 9),
        new("Reșița", "Caraș-Severin", 21.889, 45.300, "cerna", 0.0, 4),
        new("Alba Iulia", "Alba", 23.570, 46.067, "trascau", 0.05, 4),
        new("Deva", "Hunedoara", 22.912, 45.877, "sureanu", 0.05, 4),
        new("Petroșani", "Hunedoara", 23.373, 45.412, "retezat", 0.0, 3),
        new("Sibiu", "Sibiu", 24.125, 45.798, "sureanu", 0.02, 4),
        new("Târgu Mureș", "Mureș", 24.557, 46.542, "trascau", 0.45, 4),
        new("Zărnești", "Brașov", 25.318, 45.565, "piatra-craiului", 0.02, 2),
    ];

    /// <summary>Qualifiers for invented club names; chosen so that none is a real club's.</summary>
    private static readonly string[] ClubQualifiers =
    [
        "Coarda Udă", "Lampa de Carbid", "Sifonul Negru", "Ecoul Adâncului", "Liliacul Roșu", "Stalagmita Albă",
        "Galeria Uitată", "Puțul Fără Fund", "Bufnița de Piatră", "Meandrul Strâmt", "Lacul Ascuns", "Dolina Mare",
        "Ponorul Tăcut", "Vântul de Sub Munte", "Picătura", "Carstul Viu", "Umbra Pietrei", "Frânghia Lungă",
        "Cheia de Calcar", "Steaua Subterană", "Firul Apei", "Gura Muntelui", "Piatra Udă", "Ochiul Peșterii",
        "Lumina de Carbid", "Hornul Înalt", "Sala Mare", "Izvorul Ascuns", "Clopotul de Piatră", "Calea Apei",
        "Bolta Albă", "Raza de Lampă", "Pasul Strâmt", "Ursul de Peșteră", "Cristalul Ascuns", "Ceața Rece",
        "Drumul Întunericului", "Tăcerea Adâncă", "Râul Subteran", "Muntele Gol",
    ];

    private static readonly string[] ClubPatterns =
    [
        "Clubul de Speologie «{0}» {1}", "Clubul de Speologie «{0}» {1}", "Clubul de Speologie «{0}» {1}",
        "Asociația Speologică «{0}» {1}", "Grupul de Explorări Subterane «{0}» {1}", "Speo Club «{0}» {1}",
    ];

    private static readonly string[] OrganisationPatterns =
    [
        "Asociația Carstologilor «{0}»", "Liga Speologică «{0}»", "Asociația Salvatorilor din Peșteri «{0}»",
        "Societatea de Studii Carstice «{0}»",
    ];

    public static string ClubName(TestDataRng rng, string qualifier, string town)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, rng.Pick(ClubPatterns), qualifier, town);
    }

    public static string OrganisationName(TestDataRng rng, string qualifier)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, rng.Pick(OrganisationPatterns), qualifier);
    }

    /// <summary>The qualifiers in an order drawn from the run, so clubs of one run never share one.</summary>
    public static IReadOnlyList<string> ShuffledQualifiers(TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var list = ClubQualifiers.ToArray();
        for (var i = list.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    public static string ClubDescription(TestDataRng rng, string name, string town, string massif, int founded)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var focus = rng.Pick(
        [
            $"explorarea și cartarea peșterilor din {massif}",
            $"explorarea avenelor din {massif} și scufundarea în sifoane",
            $"cartarea sistematică a carstului din {massif}",
            $"protecția peșterilor și educația speologică în {town}",
            $"cercetarea faunei cavernicole din {massif}",
        ]);
        var extra = rng.Pick(
        [
            "Organizează în fiecare vară o tabără de explorare.",
            "Ține un curs de inițiere în speologie în fiecare primăvară.",
            "Membrii săi au echipat zeci de avene pentru cartare.",
            "Are o arhivă de hărți topografice începută din anii '80.",
            "Colaborează cu salvamontul local la exerciții de salvare din peșteri.",
        ]);
        return $"{name} este un club din {town}, înființat în {founded}, preocupat în principal de {focus}. {extra}";
    }

    public static string OrganisationDescription(string name) =>
        $"{name} reunește membri din mai multe cluburi, pentru proiecte comune de cercetare și de protecție a carstului.";

    private static readonly string[] MaleGiven =
    [
        "Ion", "Mihai", "Andrei", "Alexandru", "Gheorghe", "Vasile", "Constantin", "Dan", "Florin", "Radu",
        "Cristian", "Adrian", "Bogdan", "Sorin", "Marius", "Lucian", "Ovidiu", "Horia", "Tudor", "Paul",
        "Dorin", "Cornel", "Liviu", "Nicolae", "Emil", "Iulian", "Răzvan", "Victor", "Ștefan", "Petru",
        "Viorel", "Sergiu", "Cătălin", "Gabriel", "Daniel", "Claudiu", "Silviu", "Traian", "Vlad", "Matei",
    ];

    private static readonly string[] FemaleGiven =
    [
        "Maria", "Elena", "Ioana", "Ana", "Andreea", "Mihaela", "Cristina", "Daniela", "Alina", "Irina",
        "Raluca", "Oana", "Laura", "Simona", "Monica", "Adriana", "Camelia", "Diana", "Ileana", "Corina",
        "Roxana", "Bianca", "Teodora", "Larisa", "Ramona", "Gabriela", "Sorina", "Lavinia", "Iulia", "Carmen",
    ];

    private static readonly string[] Surnames =
    [
        "Popescu", "Ionescu", "Pop", "Popa", "Rusu", "Muntean", "Stan", "Oprea", "Moldovan", "Lazăr",
        "Mureșan", "Dumitru", "Matei", "Constantin", "Ciobanu", "Toma", "Barbu", "Avram", "Bogdan", "Sârbu",
        "Crișan", "Nistor", "Suciu", "Pavel", "Rădulescu", "Florea", "Bălan", "Câmpean", "Marian", "Coman",
        "Roman", "Tudor", "Ardelean", "Morar", "Vlad", "Iordache", "Cozma", "Ene", "Cristea", "Dinu",
        "Bucur", "Groza", "Lungu", "Ungureanu", "Oltean", "Pintea", "Tămaș", "Boca", "Hossu", "Gavrilă",
        "Cîmpeanu", "Sabău", "Vescan", "Filip", "Mocanu", "Neagu", "Preda", "Radu", "Stoica", "Zamfir",
    ];

    private static readonly string[] HungarianMaleGiven =
    [
        "István", "László", "Zoltán", "Attila", "Csaba", "Levente", "Gábor", "Tamás", "Ferenc", "Péter", "Botond", "Szabolcs",
    ];

    private static readonly string[] HungarianFemaleGiven =
    [
        "Erzsébet", "Katalin", "Ildikó", "Enikő", "Réka", "Tímea", "Andrea", "Zsófia", "Orsolya", "Csilla",
    ];

    private static readonly string[] HungarianSurnames =
    [
        "Kovács", "Szabó", "Nagy", "Tóth", "Fekete", "Kiss", "Molnár", "Balogh", "Farkas", "Varga",
        "Lukács", "Bartha", "Székely", "Fazakas", "Boros", "Demeter",
    ];

    private static readonly string[] Nicknames =
    [
        "Gigi", "Bebe", "Ursu", "Bizonul", "Șoricelul", "Pisicu", "Lupu", "Mache", "Tibi", "Fane",
        "Nelu", "Costi", "Dănuț", "Vali", "Liliacul", "Cârtița", "Melcul", "Vulpea", "Profu", "Doctorul",
        "Bunicu", "Piticu", "Cascadorul", "Zmeul",
    ];

    private static readonly string[] CaverNotes =
    [
        "Instructor de tehnică pe coardă.", "Fotograf al clubului.", "Topograf; lucrează cu DistoX.",
        "Scafandru de peșteră.", "Medicul clubului.", "Se ocupă de echipamentul colectiv.",
        "Nu mai merge în peșteri, dar ține arhiva clubului.", "Coordonează taberele de vară.",
        "Echipier de salvare.", "Cercetează fauna cavernicolă.",
    ];

    private static readonly string[] Bios =
    [
        "Merg în peșteri din copilărie; cel mai mult îmi plac avenele adânci.",
        "Topograf amator. Prefer o hartă bine închisă unei recorduri de adâncime.",
        "Fotografiez peșteri și oamenii care le explorează.",
        "Speolog de weekend, alpinist în restul timpului.",
        "Mă interesează hidrologia carstului și trasările cu colorant.",
        "Am învățat tehnica pe coardă la un curs al clubului și nu m-am mai oprit.",
    ];

    private static readonly string[] StreetNames =
    [
        "Str. Stalactitelor", "Str. Liliecilor", "Aleea Speologilor", "Str. Carstului", "Str. Dolinei", "Str. Izvorului Rece",
        "Str. Ponorului", "Aleea Galeriilor", "Str. Cheilor", "Str. Lămpii", "Str. Corzii", "Str. Calcarului",
    ];

    public static IReadOnlyList<TestDataGuestCountry> GuestCountries { get; } =
    [
        new("Polonia", "en", "Europe/Warsaw",
            ["Piotr", "Tomasz", "Krzysztof", "Paweł", "Marek", "Jakub"], ["Agnieszka", "Katarzyna", "Magdalena", "Zofia"],
            ["Nowak", "Kowalski", "Wiśniewski", "Wójcik", "Kamiński", "Lewandowski"], ["Kraków", "Wrocław", "Katowice"]),
        new("Cehia", "en", "Europe/Prague",
            ["Jan", "Petr", "Tomáš", "Lukáš", "Martin"], ["Jana", "Lucie", "Kateřina", "Tereza"],
            ["Novák", "Svoboda", "Dvořák", "Černý", "Procházka"], ["Brno", "Praha", "Olomouc"]),
        new("Franța", "en", "Europe/Paris",
            ["Pierre", "Julien", "Nicolas", "Mathieu", "Antoine"], ["Claire", "Camille", "Sophie", "Élise"],
            ["Martin", "Bernard", "Dubois", "Moreau", "Laurent", "Lefèvre"], ["Grenoble", "Lyon", "Montpellier"]),
        new("Marea Britanie", "en", "Europe/London",
            ["James", "Oliver", "Thomas", "William", "Robert"], ["Emily", "Sarah", "Charlotte", "Hannah"],
            ["Smith", "Jones", "Taylor", "Brown", "Wilson", "Evans"], ["Leeds", "Bristol", "Sheffield"]),
        new("Italia", "en", "Europe/Rome",
            ["Marco", "Luca", "Giovanni", "Alessandro", "Matteo"], ["Giulia", "Francesca", "Chiara", "Elena"],
            ["Rossi", "Russo", "Ferrari", "Esposito", "Bianchi", "Romano"], ["Trieste", "Torino", "Bologna"]),
    ];

    public sealed record PersonName(string Given, string Surname, bool Feminine)
    {
        public string Full => $"{Given} {Surname}";
    }

    /// <summary>A person's name: Romanian, or Hungarian at the share a town's clubs carry.</summary>
    public static PersonName Person(TestDataRng rng, double hungarianShare)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var feminine = rng.Chance(0.3);
        if (hungarianShare > 0 && rng.Chance(hungarianShare))
        {
            return new PersonName(rng.Pick(feminine ? HungarianFemaleGiven : HungarianMaleGiven), rng.Pick(HungarianSurnames), feminine);
        }

        return new PersonName(rng.Pick(feminine ? FemaleGiven : MaleGiven), rng.Pick(Surnames), feminine);
    }

    public static PersonName Guest(TestDataRng rng, TestDataGuestCountry country)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(country);
        var feminine = rng.Chance(0.3);
        return new PersonName(rng.Pick(feminine ? country.FemaleGiven : country.MaleGiven), rng.Pick(country.Surnames), feminine);
    }

    public static string Nickname(TestDataRng rng) => rng.Pick(Nicknames);

    public static string CaverNote(TestDataRng rng) => rng.Pick(CaverNotes);

    public static string Bio(TestDataRng rng) => rng.Pick(Bios);

    public static string Street(TestDataRng rng) => rng.Pick(StreetNames);
}
