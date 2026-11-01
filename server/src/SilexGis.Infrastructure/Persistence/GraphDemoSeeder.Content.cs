// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Persistence;

/// <summary>Names, codes and sizes of the invented network the graph demo lays down.</summary>
/// <remarks>
/// Every string here is visibly made up ("Graf" in each), so nobody can mistake the dataset for a
/// register of real caves, people or clubs, and so a lookup by these names can never match a row a
/// person typed in.
/// </remarks>
public static partial class GraphDemoSeeder
{
    public const string CaveCodePrefix = "GRAF-";

    public const int CaveCount = 150;

    public const int GenericCount = 60;

    public const int CaverCount = 40;

    /// <summary>
    /// Trips. More than the dataset's headline figure on purpose: one caver is on 120 of them to
    /// exercise "and N more", and a caver can be on a trip only once, so 120 trips for him needs
    /// at least that many trips to exist.
    /// </summary>
    public const int TripCount = 130;

    public const int BusyCaverTrips = 120;

    public const int ExpeditionCount = 6;

    public const int DocumentCount = 120;

    public const int ResLinkCount = 400;

    public const int TagCount = 30;

    public const int BusyTagItems = 100;

    public const int BusyCaveLinks = 150;

    public const string AlfaName = "Clubul Graf Alfa";

    public const string BetaName = "Clubul Graf Beta";

    public const string GamaName = "Clubul Graf Gama";

    public const string AlfaSlug = "clubul-graf-alfa";

    public const string BetaSlug = "clubul-graf-beta";

    public const string GamaSlug = "clubul-graf-gama";

    public const string ViewerEmail = "viewer@test.local";

    public const string EditorEmail = "editor@test.local";

    public const string RootCabinetName = "Arhiva Graf";

    public static string CaveCode(int i) => $"{CaveCodePrefix}{i:D4}";

    public static string CaveName(int i) => $"Peștera Graf {i:D3}";

    public static string GenericName(int i) => $"Obiect Graf {i:D3}";

    public static string CaverName(int i) => $"Speolog Graf {i:D2}";

    public static string TripTitle(int i) => $"Ieșire Graf {i:D3}";

    public static string ExpeditionName(int i) => $"Expediția Graf {i}";

    public static string DocumentTitle(int i) => $"Raport Graf {i:D3}";

    public static string TagName(int i) => $"graf-tag-{i:D2}";

    /// <summary>Fixed eight-character code, so a re-run finds the link it wrote.</summary>
    public static string LinkCode(int i) => $"GL{i:D6}";

    /// <summary>
    /// The three vocabulary entries of the network's own: (code, name, directed, inverse name).
    /// </summary>
    internal static readonly (string Code, string Name, bool Directed, string? Inverse)[] CustomRelations =
    [
        ("graf-supersedes", "Supersedes", true, "Superseded by"),
        ("graf-shares-water", "Shares water with", false, null),
        ("graf-same-expedition-find", "Found on the same expedition", false, null),
    ];

    internal static readonly string[] CabinetNames =
    [
        "Fond Graf A", "Fond Graf B", "Fond Graf C",
        "Seria Graf A1", "Seria Graf A2", "Seria Graf B1", "Seria Graf B2", "Seria Graf C1", "Seria Graf C2",
    ];
}
