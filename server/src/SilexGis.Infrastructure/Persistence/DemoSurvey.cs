// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// The survey the demonstration data draws its tracked trip on: twelve stations nobody measured.
/// </summary>
/// <remarks>
/// <para>
/// Invented, and written from the numbers below rather than shipped as a file. A survey is the
/// most exact description of a cave there is, so the one a product hands to every installation
/// that asks for demonstration data cannot be a real cave's — and a file in the repository would
/// have to be taken on trust not to be one, where a dozen lines of numbers can be read.
/// </para>
/// <para>
/// The shape is the plainest that still has something to show: an entrance series, a gallery
/// sloping down to a pitch head, a pit of forty metres, and a meander leaving its foot. Heights
/// are metres about the entrance, so a station's depth is its height with the sign turned — which
/// is what lets the one place declared by depth below be checked against the file by eye.
/// </para>
/// <para>
/// The names are the ones the reports of the seeded trip are written against. They are whole in
/// this format — the name in the file is the name the viewer shows and the name a report holds —
/// so a report can be written before the file has been read, and a name changed here without the
/// reports is a marker that silently never appears. The test that reads the seeded survey with the
/// ordinary reader and looks every reported station up in it is what holds the two together.
/// </para>
/// </remarks>
internal static class DemoSurvey
{
    /// <summary>What the file is called, as an upload would have named it.</summary>
    public const string FileName = "pestera-demo-mare-invented.3d";

    /// <summary>The survey model's name: the file's, without its extension, as an upload gives it.</summary>
    public const string ModelName = "pestera-demo-mare-invented";

    /// <summary>The end of the gallery, where the party is first reported.</summary>
    public const string GalleryEnd = "demo.galerie.3";

    /// <summary>The head of the pit.</summary>
    public const string PitHead = "demo.put.0";

    /// <summary>The foot of the pit — the place the cave declares for the depth below.</summary>
    public const string PitFoot = "demo.put.3";

    /// <summary>How far below the entrance the foot of the pit is, in metres.</summary>
    public const decimal PitFootDepthM = 60m;

    private static readonly InventedSurveyFile.Station[] Stations =
    [
        new("demo.intrare.0", 0, 0, 0, Entrance: true),
        new("demo.intrare.1", 8, 2, -3),
        new("demo.intrare.2", 17, 3, -7),
        new("demo.galerie.1", 29, 8, -11),
        new("demo.galerie.2", 41, 10, -14),
        new(GalleryEnd, 52, 17, -18),
        new(PitHead, 58, 21, -20),
        new("demo.put.1", 59, 22, -38),
        new("demo.put.2", 60, 22, -52),
        new(PitFoot, 61, 23, -60),
        new("demo.meandru.1", 70, 28, -63),
        new("demo.meandru.2", 81, 30, -66),
    ];

    /// <summary>The file's bytes: the stations above joined end to end, with the walk-in part measured to its walls.</summary>
    public static byte[] Build()
    {
        var legs = new InventedSurveyFile.Leg[Stations.Length - 1];
        for (var i = 0; i < legs.Length; i++)
        {
            var from = Stations[i];
            var to = Stations[i + 1];
            legs[i] = new((from.X, from.Y, from.Z), (to.X, to.Y, to.Z));
        }

        // The six stations a person walks through, each with a width and a height, so the cave has
        // walls to draw as well as a line. The pit and the meander are left as a line: a survey
        // measured to its walls only in part is the ordinary kind.
        var walked = Stations[..6]
            .Select((s, i) => new InventedSurveyFile.CrossSection(s.Name, 1.2 + (i * 0.2), 1.0 + (i * 0.2), 1.5, 0.8))
            .ToArray();

        return InventedSurveyFile.Survex3d(Stations, legs, [walked]);
    }
}
