// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Export;

/// <summary>
/// Something a flyby shows in the 3D scene that a render bundle of it does not carry.
///
/// <para>
/// A bundle is made anyway and says what it leaves out — a flyby is not refused because part of
/// its look has no counterpart yet. What a flyby changes while it plays is not on the list: the
/// relief, the colours of walls and lines, the layers a bundle draws shown, hidden and faded, and
/// the sky are carried, change by change. The list is closed: whoever asks for a bundle names what
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

    /// <summary>Entrance markers.</summary>
    Entrances = 2,

    /// <summary>Surface features and layers of features.</summary>
    SurfaceFeatures = 3,

    /// <summary>Point-cloud scans.</summary>
    Scans = 4,

    /// <summary>Photograph markers.</summary>
    Photos = 5,

    /// <summary>Tile overlays and georeferenced maps.</summary>
    MapLayers = 6,

    /// <summary>The lines of imported files.</summary>
    Tracks = 7,

    /// <summary>Walls the viewer chose not to download into the 3D view.</summary>
    WallsNotDownloaded = 8,

    /// <summary>Caves in view beyond the number a bundle takes.</summary>
    MoreCavesInView = 9,
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
        FlybyBundleOmission.Entrances => romanian
            ? "marcajele intrărilor"
            : "the entrance markers",
        FlybyBundleOmission.SurfaceFeatures => romanian
            ? "fenomenele de suprafață și straturile de obiecte"
            : "the surface features and the layers of features",
        FlybyBundleOmission.Scans => romanian
            ? "scanările"
            : "the point-cloud scans",
        FlybyBundleOmission.Photos => romanian
            ? "marcajele fotografiilor"
            : "the photograph markers",
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
    /// A count before a Romanian noun: from twenty on, and for every number whose last two digits
    /// are not one to nineteen, the noun is joined to it by "de" ("20 de peșteri", "101 peșteri").
    /// </summary>
    private static string Counted(int count)
    {
        var number = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return count % 100 is >= 1 and <= 19 ? number : number + " de";
    }
}

/// <summary>A section of a bundle's notes: its heading and its lines.</summary>
public sealed record FlybyBundleNotesSection(string Title, string Text);

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
    public static IReadOnlyList<FlybyBundleNotesSection> Sections(bool romanian, bool daylightGoes) => romanian
        ?
        [
            new(
                "CUM ESTE AȘEZATĂ SCENA",
                "Pozițiile sunt în metri spre est, spre nord și în sus față de mijlocul primului subiect al\n"
                + "survolului. Scena arată forma peșterilor și cum sunt așezate una față de alta, nu unde se\n"
                + "află pe hartă: nu conține coordonate, teren sau hartă."),
            new(
                "LUMINA",
                daylightGoes
                    ? "Lampa de pe cameră este aprinsă tot timpul. Soarele și cerul se sting cât timp camera se\n"
                      + "află sub terenul vizualizării 3D în care a fost redat survolul: scad în decurs de o secundă\n"
                      + "din clipa în care camera coboară sub teren și revin în secunda dinainte ca ea să iasă.\n"
                      + "Sub teren luminează doar lampa, care bate câțiva metri: ce privește camera de mai departe\n"
                      + "rămâne întunecat. Pentru a păstra lumina zilei, ștergeți din \"changes\", în\n"
                      + "scene-plan.json, schimbarea cu cheile \"energy\" ale luminii \"Sun\" și cheile\n"
                      + "\"worldStrength\"; pentru o lampă mai puternică, măriți \"energy\" la lumina \"Headlamp\"."
                    : "Lampa de pe cameră și soarele sunt aprinse tot timpul: în acest survol camera nu coboară\n"
                      + "sub terenul vizualizării 3D în care a fost redat."),
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
                daylightGoes
                    ? "The lamp on the camera is on throughout. The sun and the sky go out while the camera is under\n"
                      + "the ground of the 3D view the flyby was played in: they fade over a second from the moment\n"
                      + "the camera goes under, and come back over the second before it comes out. Under the ground\n"
                      + "only the lamp lights the scene, and it reaches a few metres: what the camera looks at from\n"
                      + "farther away stays dim. To keep the daylight, remove from \"changes\" in scene-plan.json the\n"
                      + "change with the \"energy\" keys of the light \"Sun\", and the \"worldStrength\" keys; for a\n"
                      + "stronger lamp, raise the \"energy\" of the light \"Headlamp\"."
                    : "The lamp on the camera and the sun are on throughout: in this flyby the camera does not go\n"
                      + "under the ground of the 3D view it was played in."),
        ];
}
