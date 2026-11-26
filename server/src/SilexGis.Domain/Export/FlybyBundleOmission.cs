// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Export;

/// <summary>
/// Something a flyby shows in the 3D scene that a render bundle of it does not carry.
///
/// <para>
/// A bundle is made anyway and says what it leaves out — a flyby is not refused because part of
/// its look has no counterpart yet. What a flyby changes while it plays is not on the list: the
/// relief, the colours of walls and lines, the layers a bundle draws shown, hidden and faded, and
/// the sky are carried, change by change. Neither is what it points out: entrances, the features
/// of the surface that are points, and the photographs taken at stations go in as markers, names
/// and pictures. The list is closed: whoever asks for a bundle names what
/// applies to their flyby in these words, the page says it before the file is made and the file
/// says it again in its own notes, so the two cannot drift into saying different things.
/// </para>
/// </summary>
public enum FlybyBundleOmission
{
    /// <summary>The ground and the map drawn on it. Always so: a bundle holds caves and no terrain.</summary>
    Ground = 0,

    /// <summary>The opening cut in the ground over a cave.</summary>
    SurfaceOpening = 1,

    /// <summary>
    /// The outlines of surface features that are lines or areas: they are drawn on the ground, and
    /// a bundle has none. The features that are points go in as markers.
    /// </summary>
    FeatureOutlines = 2,

    /// <summary>Point-cloud scans.</summary>
    Scans = 3,

    /// <summary>Tile overlays and georeferenced maps.</summary>
    MapLayers = 4,

    /// <summary>The lines of imported files.</summary>
    Tracks = 5,

    /// <summary>Walls the viewer chose not to download into the 3D view.</summary>
    WallsNotDownloaded = 6,

    /// <summary>Caves in view beyond the number a bundle takes.</summary>
    MoreCavesInView = 7,

    /// <summary>Entrances and features beyond the number of markers a bundle takes.</summary>
    MoreMarkers = 8,
}

/// <summary>
/// What a bundle's own notes say about each thing it leaves out, in English or in Romanian — the
/// two languages its notes are written in.
/// </summary>
public static class FlybyBundleOmissions
{
    /// <summary>
    /// The sentence for one omission, as it stands in a list headed "the render does not show".
    /// </summary>
    public static string Sentence(FlybyBundleOmission omission, bool romanian = false) => omission switch
    {
        FlybyBundleOmission.Ground => romanian
            ? "terenul și harta desenată pe el (peșterile stau în spațiu gol, sub un cer simplu)"
            : "the ground and the map drawn on it (the caves stand in empty space, under a plain sky)",
        FlybyBundleOmission.SurfaceOpening => romanian
            ? "deschiderea tăiată în teren deasupra unei peșteri"
            : "the opening cut in the ground over a cave",
        FlybyBundleOmission.FeatureOutlines => romanian
            ? "contururile fenomenelor de suprafață care sunt linii sau suprafețe (cele punctuale sunt marcaje)"
            : "the outlines of surface features that are lines or areas (those that are points are markers)",
        FlybyBundleOmission.Scans => romanian
            ? "scanările"
            : "the point-cloud scans",
        FlybyBundleOmission.MapLayers => romanian
            ? "straturile suprapuse de hartă și hărțile georeferențiate"
            : "the map overlays and the georeferenced maps",
        FlybyBundleOmission.Tracks => romanian
            ? "liniile fișierelor importate"
            : "the lines of imported files",
        FlybyBundleOmission.WallsNotDownloaded => romanian
            ? "pereții care nu au fost descărcați în vizualizarea 3D (acele peșteri sunt desenate prin drumuiri)"
            : "the walls that were not downloaded into the 3D view (those caves are drawn by their survey lines)",
        FlybyBundleOmission.MoreCavesInView => romanian
            ? "o parte din peșterile din vizualizare (un pachet ia doar un număr limitat)"
            : "some of the caves that were in view (a bundle takes only so many)",
        FlybyBundleOmission.MoreMarkers => romanian
            ? "o parte din intrările și obiectele din vizualizare (un pachet ia doar un număr limitat de marcaje)"
            : "some of the entrances and features that were in view (a bundle takes only so many markers)",
        _ => throw new ArgumentOutOfRangeException(nameof(omission), omission, "Not something a bundle leaves out."),
    };

    /// <summary>
    /// Caves that were asked for and are not drawn. One count and no reason: a cave that does not
    /// exist, one the asker may not read, one they may not place exactly and one with no survey to
    /// draw are all the same sentence, because telling them apart would say which caves are being
    /// kept from whom.
    /// </summary>
    public static string CavesNotDrawn(int count, bool romanian = false) => (romanian, count == 1) switch
    {
        (false, true) => "1 of the caves asked for (nothing of it is available to draw)",
        (false, false) => $"{count} of the caves asked for (nothing of them is available to draw)",
        (true, true) => "1 dintre peșterile cerute (nimic din ea nu este disponibil pentru desenare)",
        (true, false) => $"{count} dintre peșterile cerute (nimic din ele nu este disponibil pentru desenare)",
    };

    /// <summary>Caves whose walls were asked for and that have none to draw, so their lines stand in.</summary>
    public static string WallsNotAvailable(int count, bool romanian = false) => (romanian, count == 1) switch
    {
        (false, true) => "the walls of 1 cave, which has none to draw (it is drawn by its survey lines)",
        (false, false) => $"the walls of {count} caves, which have none to draw (they are drawn by their survey lines)",
        (true, true) => "pereții unei peșteri, care nu are pereți de desenat (este desenată prin drumuiri)",
        (true, false) => $"pereții a {Counted(count)} peșteri, care nu au pereți de desenat (sunt desenate prin drumuiri)",
    };

    /// <summary>Caves whose walls would have taken the bundle past the size this installation allows.</summary>
    public static string WallsOverBudget(int count, bool romanian = false) => (romanian, count == 1) switch
    {
        (false, true) =>
            "the walls of 1 cave, which would take the bundle past the size this installation allows (it is drawn by its survey lines)",
        (false, false) =>
            $"the walls of {count} caves, which would take the bundle past the size this installation allows (they are drawn by their survey lines)",
        (true, true) =>
            "pereții unei peșteri, care ar duce pachetul peste dimensiunea permisă de această instalare (este desenată prin drumuiri)",
        (true, false) =>
            $"pereții a {Counted(count)} peșteri, care ar duce pachetul peste dimensiunea permisă de această instalare (sunt desenate prin drumuiri)",
    };

    /// <summary>
    /// Entrances and features that were asked for and are not drawn. One count and no reason, as
    /// for caves: one that does not exist, one the asker may not read, one whose exact place they
    /// may not have and one that is not a point are the same sentence.
    /// </summary>
    public static string MarkersNotDrawn(int count, bool romanian = false) => (romanian, count == 1) switch
    {
        (false, true) => "1 of the entrances and features asked for (nothing of it is available to draw)",
        (false, false) => $"{count} of the entrances and features asked for (nothing of them is available to draw)",
        (true, true) => "1 dintre intrările și obiectele cerute (nimic din el nu este disponibil pentru desenare)",
        (true, false) => $"{count} dintre intrările și obiectele cerute (nimic din ele nu este disponibil pentru desenare)",
    };

    /// <summary>
    /// Photographs taken at stations of the caves drawn that the bundle does not show: a station
    /// shows one photograph, a bundle only so many, and one the camera never comes near none.
    /// </summary>
    public static string PhotosNotShown(int count, bool romanian = false) => (romanian, count == 1) switch
    {
        (false, true) =>
            "1 photograph taken at a station (a station shows one, a bundle only so many, and none the camera stays far from)",
        (false, false) =>
            $"{count} photographs taken at stations (a station shows one, a bundle only so many, and none the camera stays far from)",
        (true, true) =>
            "1 fotografie făcută la o stație (o stație arată una, un pachet doar un număr limitat și niciuna de care camera rămâne departe)",
        (true, false) =>
            $"{Counted(count)} fotografii făcute la stații (o stație arată una, un pachet doar un număr limitat și niciuna de care camera rămâne departe)",
    };

    /// <summary>
    /// A count before a Romanian noun: from twenty on, and for every number whose last two digits
    /// are not one to nineteen, the noun is joined to it by "de" ("20 de peșteri", "101 peșteri").
    /// </summary>
    internal static string Counted(int count)
    {
        var number = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return count % 100 is >= 1 and <= 19 ? number : number + " de";
    }
}

/// <summary>A section of a bundle's notes: its heading and its lines.</summary>
public sealed record FlybyBundleNotesSection(string Title, string Text);

/// <summary>How a bundle's caves appear part by part, as its notes say it.</summary>
/// <param name="ByDate">By the day each leg was surveyed, rather than by the distance from the entrances.</param>
/// <param name="LegsWithoutDate">By date: legs with no day of their own, which are there from the start.</param>
/// <param name="CavesOnTheSurveyDate">By date: caves that appear whole on their survey's one date.</param>
/// <param name="CavesWithoutDate">By date: caves with no date at all, which are there from the start.</param>
/// <param name="Revealed">Whether anything appears part by part at all: by date, whether any cave has a date.</param>
public sealed record FlybyBundleNotesReveal(
    bool ByDate, int LegsWithoutDate = 0, int CavesOnTheSurveyDate = 0, int CavesWithoutDate = 0, bool Revealed = true);

/// <summary>
/// What a bundle's notes say that is this application's own to say: what the folder is, and how
/// its scene is laid out and lit. In English or in Romanian; the lines are kept under ninety
/// characters, as the rest of the notes are.
/// </summary>
public static class FlybyBundleNotes
{
    /// <summary>
    /// Whether a language, given as a tag ("ro", "ro-RO"), is Romanian. Every other language, and
    /// none at all, is given the English notes.
    /// </summary>
    public static bool IsRomanian(string? language) =>
        (language ?? string.Empty).Split('-', '_')[0].Equals("ro", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the folder is, in the first lines of its notes.</summary>
    public static string Intro(bool romanian) => romanian
        ? "Un survol printre peșteri, făcut de SilexGIS pentru Blender: pereții și drumuirile peșterilor,\n"
          + "camera la fiecare cadru și aspectul, gata de randat. Nimic din acest dosar nu are nevoie de\n"
          + "SilexGIS sau de ThIDE."
        : "A flyby of caves made by SilexGIS for Blender: the caves' walls and survey lines, the camera at\n"
          + "every frame and the look, ready to render. Nothing in this folder needs SilexGIS or ThIDE.";

    /// <summary>
    /// The sections of the notes that are about this scene: how it is laid out, and how it is lit.
    /// </summary>
    /// <param name="daylightGoes">Whether the sun and the sky go out at some frame of the clip.</param>
    /// <param name="lampReaches">Whether the lamp on the camera is turned up as they go: there are walls for it to light.</param>
    /// <param name="markers">How many markers stand in the scene.</param>
    /// <param name="photos">How many photographs it shows.</param>
    /// <param name="reveal">How the caves appear part by part; null where they are there from the start.</param>
    public static IReadOnlyList<FlybyBundleNotesSection> Sections(
        bool romanian, bool daylightGoes, int markers = 0, int photos = 0, FlybyBundleNotesReveal? reveal = null, bool lampReaches = false) =>
    [
        .. LaidOutAndLit(romanian, daylightGoes, lampReaches),
        .. markers > 0 || photos > 0 ? [PointedOut(romanian, markers, photos)] : Array.Empty<FlybyBundleNotesSection>(),
        .. reveal is null ? Array.Empty<FlybyBundleNotesSection>() : [reveal.ByDate ? RevealByDate(romanian, reveal) : Reveal(romanian)],
    ];

    private static FlybyBundleNotesSection PointedOut(bool romanian, int markers, int photos) => romanian
        ? new(
            "CE ESTE SEMNALAT",
            $"Scena are {Both(Of(markers, "marcaj", "marcaje", true), Of(photos, "fotografie", "fotografii", true), "și")},\n"
            + "în \"annotations\" din scene-plan.json. Fiecare este prins de un punct al modelului unei\n"
            + "peșteri și merge cu el când relieful întinde peștera. Un marcaj este o sferă luminoasă,\n"
            + "atât de mare cât să se vadă de la cea mai mică distanță la care ajunge camera; numele\n"
            + "unei intrări stă deasupra lui. O fotografie făcută la o stație a unei ridicări stă\n"
            + "deasupra stației, lată cât o cincime din cea mai mică distanță la care ajunge camera —\n"
            + "mai lată acolo unde camera trece în zbor, ca să se vadă două secunde — și se vede cât\n"
            + "timp camera este la cel mult douăsprezece lățimi și jumătate de ea. Fiecare strat are un\n"
            + "rol (\"entrance\", \"surface-feature\", \"feature-layer-1\", \"station-photo\"; numele: rolul\n"
            + "urmat de \"-name\"): pentru a ascunde unul, ștergeți-i adnotările. Fotografiile sunt\n"
            + "fișierele din photos/; nu conțin poziție și nici datele aparatului.")
        : new(
            "WHAT IS POINTED OUT",
            $"The scene has {Both(Of(markers, "marker", "markers", false), Of(photos, "photograph", "photographs", false), "and")},\n"
            + "in \"annotations\" in scene-plan.json. Each is pinned to a point of a cave's model and\n"
            + "goes with it when the relief stretches the cave. A marker is a glowing ball, as large as\n"
            + "reads from the nearest the camera comes to it; the name of an entrance stands above it.\n"
            + "A photograph taken at a station of a survey stands above that station, as wide as a\n"
            + "fifth of the nearest the camera comes to it — wider where the camera flies past, so\n"
            + "that it is up for two seconds — and is shown while the camera is within twelve and a\n"
            + "half of its widths. Every layer has a role (\"entrance\", \"surface-feature\",\n"
            + "\"feature-layer-1\", \"station-photo\"; its names: the role followed by \"-name\"): to hide\n"
            + "one, delete its annotations. The photographs are the files in photos/; they hold no\n"
            + "position and none of the camera's own data.");

    private static FlybyBundleNotesSection Reveal(bool romanian) => romanian
        ? new(
            "APARIȚIA TREPTATĂ",
            "Fiecare peșteră apare parte cu parte, de la intrări spre interior, după distanța de-a\n"
            + "lungul galeriilor: fiecare vârf are acea distanță în metri (atributul \"reveal\" al\n"
            + "fiecărui .ply), iar cheile \"reveal\" din \"changes\" ridică un prag peste ele. O peșteră\n"
            + "fără nicio intrare marcată în ridicare pornește de la prima stație a primei vize. Pentru\n"
            + "a vedea peșterile întregi de la început, ștergeți din \"changes\" membrii \"reveal\" și\n"
            + "\"revealFront\".")
        : new(
            "THE REVEAL",
            "Each cave appears part by part, from its entrances inwards, by the distance along its\n"
            + "passages: every vertex has that distance in metres (the attribute \"reveal\" of each\n"
            + ".ply), and the \"reveal\" keys in \"changes\" raise a threshold over them. A cave with no\n"
            + "entrance marked in its survey starts at the first station of its first leg. To see the\n"
            + "caves whole from the start, remove the \"reveal\" and \"revealFront\" members from\n"
            + "\"changes\".");

    private static FlybyBundleNotesSection RevealByDate(bool romanian, FlybyBundleNotesReveal reveal)
    {
        var anyDated = reveal.Revealed;
        var said = new List<string>();
        if (romanian)
        {
            said.Add(anyDated
                ? "Peșterile apar parte cu parte în ordinea în care au fost explorate: fiecare vârf are ziua "
                  + "în care a fost ridicată viza lui, ca an cu fracțiunea lui (atributul \"reveal\" al fiecărui "
                  + ".ply), iar cheile \"reveal\" din \"changes\" ridică un singur prag peste anii tuturor "
                  + "peșterilor, de la cel mai vechi la cel mai nou. Contorul din stânga arată anul la care a "
                  + "ajuns (elementul \"reveal-counter\" din \"texts\")."
                : "Niciuna dintre peșteri nu are o dată a ridicării, așa că toate sunt acolo de la început.");
            if (reveal.LegsWithoutDate > 0)
            {
                said.Add(reveal.LegsWithoutDate == 1
                    ? "1 viză nu are dată: este acolo de la început."
                    : $"{FlybyBundleOmissions.Counted(reveal.LegsWithoutDate)} vize nu au dată: sunt acolo de la început.");
            }

            if (reveal.CavesOnTheSurveyDate > 0)
            {
                said.Add(reveal.CavesOnTheSurveyDate == 1
                    ? "1 peșteră are o dată doar pentru întreaga ridicare: apare întreagă la acea dată."
                    : $"{FlybyBundleOmissions.Counted(reveal.CavesOnTheSurveyDate)} peșteri au o dată doar pentru întreaga ridicare: fiecare apare întreagă la acea dată.");
            }

            if (anyDated && reveal.CavesWithoutDate > 0)
            {
                said.Add(reveal.CavesWithoutDate == 1
                    ? "1 peșteră nu are nicio dată a ridicării: este acolo de la început."
                    : $"{FlybyBundleOmissions.Counted(reveal.CavesWithoutDate)} peșteri nu au nicio dată a ridicării: sunt acolo de la început.");
            }

            if (anyDated)
            {
                said.Add("Pentru a vedea peșterile întregi de la început, ștergeți din \"changes\" membrii "
                    + "\"reveal\" și \"revealFront\", iar din \"texts\" contorul.");
            }

            return new("APARIȚIA DUPĂ DATA RIDICĂRII", Wrapped(string.Join(" ", said)));
        }

        said.Add(anyDated
            ? "The caves appear part by part in the order they were explored: every vertex has the day "
              + "its leg was surveyed, as a year with its fraction (the attribute \"reveal\" of each .ply), "
              + "and the \"reveal\" keys in \"changes\" raise one threshold over the years of all the caves, "
              + "from the earliest to the latest. The counter at the left says the year it has got to (the "
              + "item \"reveal-counter\" in \"texts\")."
            : "None of the caves has a survey date, so all of them are there from the start.");
        if (reveal.LegsWithoutDate > 0)
        {
            said.Add(reveal.LegsWithoutDate == 1
                ? "1 survey leg has no date: it is there from the start."
                : $"{reveal.LegsWithoutDate} survey legs have no date: they are there from the start.");
        }

        if (reveal.CavesOnTheSurveyDate > 0)
        {
            said.Add(reveal.CavesOnTheSurveyDate == 1
                ? "1 cave has a date only for its whole survey: it appears whole on that date."
                : $"{reveal.CavesOnTheSurveyDate} caves have a date only for the whole survey: each appears whole on that date.");
        }

        if (anyDated && reveal.CavesWithoutDate > 0)
        {
            said.Add(reveal.CavesWithoutDate == 1
                ? "1 cave has no survey date at all: it is there from the start."
                : $"{reveal.CavesWithoutDate} caves have no survey date at all: they are there from the start.");
        }

        if (anyDated)
        {
            said.Add("To see the caves whole from the start, remove the \"reveal\" and \"revealFront\" "
                + "members from \"changes\" and the counter from \"texts\".");
        }

        return new("THE REVEAL BY SURVEY DATE", Wrapped(string.Join(" ", said)));
    }

    /// <summary>Words in lines of at most ninety characters, as the rest of the notes are.</summary>
    private static string Wrapped(string text)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > 90)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : string.Empty).Append(word);
        }

        lines.Add(line.ToString());
        return string.Join("\n", lines);
    }

    /// <summary>A count with its noun, in the number the count asks for; nothing for none.</summary>
    private static string? Of(int count, string one, string many, bool romanian)
    {
        var number = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return count switch
        {
            <= 0 => null,
            1 => $"{number} {one}",
            _ => romanian && count % 100 is not (>= 1 and <= 19) ? $"{number} de {many}" : $"{number} {many}",
        };
    }

    /// <summary>One thing, or two joined by a word.</summary>
    private static string Both(string? first, string? second, string and) =>
        first is null ? second ?? string.Empty : second is null ? first : $"{first} {and} {second}";

    private static IReadOnlyList<FlybyBundleNotesSection> LaidOutAndLit(bool romanian, bool daylightGoes, bool lampReaches) => romanian
        ?
        [
            new(
                "CUM ESTE AȘEZATĂ SCENA",
                "Pozițiile sunt în metri spre est, spre nord și în sus față de mijlocul primului subiect al\n"
                + "survolului. Scena arată forma peșterilor și cum sunt așezate una față de alta, nu unde se\n"
                + "află pe hartă: nu conține coordonate, teren sau hartă."),
            new(
                "LUMINA",
                (daylightGoes
                    ? "Lampa de pe cameră este aprinsă tot timpul. Soarele și cerul se sting cât timp camera se\n"
                      + "află sub terenul vizualizării 3D în care a fost redat survolul: scad în decurs de o secundă\n"
                      + "din clipa în care camera coboară sub teren și revin în secunda dinainte ca ea să iasă.\n"
                      + (lampReaches
                          ? "Pe măsură ce se sting, puterea lămpii crește cu pătratul distanței de la cameră la cel mai\n"
                            + "apropiat perete (cheile \"energy\" ale luminii \"Headlamp\" din \"changes\", în\n"
                            + "scene-plan.json): pereții priviți de departe, sub teren, sunt luminați ca pereții aflați\n"
                            + "la trei metri de lampă. Pereții mai îndepărtați decât cel mai apropiat sunt mai\n"
                            + "întunecați, ca la orice lampă. Pentru a păstra lumina zilei, ștergeți din \"changes\"\n"
                            + "schimbarea cu cheile \"energy\" ale luminii \"Sun\", cheile \"worldStrength\" și, odată cu\n"
                            + "ele, schimbarea luminii \"Headlamp\".\n"
                          : "Pentru a păstra lumina zilei, ștergeți din \"changes\", în scene-plan.json, schimbarea cu\n"
                            + "cheile \"energy\" ale luminii \"Sun\" și cheile \"worldStrength\".\n")
                    : "Lampa de pe cameră și soarele sunt aprinse tot timpul: în acest survol camera nu coboară\n"
                      + "sub terenul vizualizării 3D în care a fost redat.\n")
                + "Drumuirile sunt desenate în culorile lor indiferent de lumină (\"unlit\" în materialele\n"
                + "lor): se văd la fel în întuneric ca la soare și nu luminează nimic."),
        ]
        :
        [
            new(
                "HOW THE SCENE IS LAID OUT",
                "Positions are metres east, north and up of the middle of the flyby's first subject. The scene\n"
                + "shows the shape of the caves and how they lie to each other, not where on a map they are: it\n"
                + "holds no coordinates, no ground and no map."),
            new(
                "THE LIGHT",
                (daylightGoes
                    ? "The lamp on the camera is on throughout. The sun and the sky go out while the camera is under\n"
                      + "the ground of the 3D view the flyby was played in: they fade over a second from the moment\n"
                      + "the camera goes under, and come back over the second before it comes out.\n"
                      + (lampReaches
                          ? "As they go, the lamp is turned up by the square of the distance from the camera to the\n"
                            + "nearest wall (the \"energy\" keys of the light \"Headlamp\" in \"changes\" in\n"
                            + "scene-plan.json): walls seen from far away under the ground are lit as walls three metres\n"
                            + "from the lamp are. Walls farther off than the nearest are darker, as by any lamp. To keep\n"
                            + "the daylight, remove from \"changes\" the change with the \"energy\" keys of the light\n"
                            + "\"Sun\", the \"worldStrength\" keys and, with them, the change of the light \"Headlamp\".\n"
                          : "To keep the daylight, remove from \"changes\" in scene-plan.json the change with the\n"
                            + "\"energy\" keys of the light \"Sun\", and the \"worldStrength\" keys.\n")
                    : "The lamp on the camera and the sun are on throughout: in this flyby the camera does not go\n"
                      + "under the ground of the 3D view it was played in.\n")
                + "The survey lines are drawn in their own colours whatever the light (\"unlit\" in their\n"
                + "materials): they read in the dark as they do under the sun, and light nothing."),
        ];
}
