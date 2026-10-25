// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Surveys;

/// <summary>What one output of a compiled upload is stored as.</summary>
public enum SurveyArtefactStorage
{
    /// <summary>A Therion model, kept as the cave's survey model and read into stations and legs.</summary>
    LinePlot,

    /// <summary>A Survex model, kept as the cave's survey model for download and the viewer, never read.</summary>
    Survex,

    /// <summary>A drawing, a table or a track, kept as a document attached to the cave.</summary>
    Document,
}

/// <summary>How one output of the catalogue is named and stored.</summary>
/// <param name="Output">The output's catalogue name.</param>
/// <param name="Extension">
/// What follows the version stamp in the stored file's name — the file extension, with a word in
/// front of it where two outputs share an extension (<c>plan.pdf</c>, <c>extended.pdf</c>).
/// </param>
/// <param name="Label">What the output is, in the title of the document it becomes.</param>
/// <param name="Storage">What it is stored as.</param>
/// <param name="MediaType">The media type the stored file is served with.</param>
public sealed record SurveyArtefactKind(
    string Output, string Extension, string Label, SurveyArtefactStorage Storage, string MediaType);

/// <summary>
/// The outputs a compiled upload stores, how each is named, and which versions of a survey keep
/// theirs.
/// </summary>
/// <remarks>
/// Every name carries the upload's version stamp (see <see cref="SurveyVersionStamp"/>), so two
/// versions of one survey never store a file under the same name, and anyone holding a downloaded
/// file can tell which upload it came from.
/// </remarks>
public static class SurveyUploadArtefacts
{
    /// <summary>The kind of the compiler's log in an upload's artefact list.</summary>
    public const string LogKind = "log";

    /// <summary>The kind of the generated compiler configuration in an upload's file list.</summary>
    public const string ConfigKind = "thconfig";

    /// <summary>The kind of the generated survey that wraps the uploaded one.</summary>
    public const string WrapperKind = "wrapper";

    private static readonly IReadOnlyDictionary<string, SurveyArtefactKind> Kinds = new[]
    {
        new SurveyArtefactKind("lox", "lox", "model", SurveyArtefactStorage.LinePlot, "application/octet-stream"),
        new SurveyArtefactKind("3d", "3d", "Survex model", SurveyArtefactStorage.Survex, "application/octet-stream"),
        new SurveyArtefactKind("plan-pdf", "plan.pdf", "plan", SurveyArtefactStorage.Document, "application/pdf"),
        new SurveyArtefactKind("extended-pdf", "extended.pdf", "extended elevation", SurveyArtefactStorage.Document, "application/pdf"),
        new SurveyArtefactKind("plan-svg", "plan.svg", "plan (SVG)", SurveyArtefactStorage.Document, "image/svg+xml"),
        new SurveyArtefactKind("extended-svg", "extended.svg", "extended elevation (SVG)", SurveyArtefactStorage.Document, "image/svg+xml"),
        new SurveyArtefactKind("kml", "kml", "KML", SurveyArtefactStorage.Document, "application/vnd.google-earth.kml+xml"),
        new SurveyArtefactKind("dxf", "dxf", "DXF", SurveyArtefactStorage.Document, "image/vnd.dxf"),
        new SurveyArtefactKind("sql", "sql", "SQL database", SurveyArtefactStorage.Document, "application/sql"),
        new SurveyArtefactKind("csv", "csv", "station table", SurveyArtefactStorage.Document, "text/csv"),
        new SurveyArtefactKind("survey-list-html", "surveys.html", "survey list", SurveyArtefactStorage.Document, "text/html"),
        new SurveyArtefactKind("continuation-list-html", "continuations.html", "continuation list", SurveyArtefactStorage.Document, "text/html"),
        // Therion writes a shapefile as a directory of files; it is stored as one archive of them.
        new SurveyArtefactKind("shp", "shp.zip", "shapefile", SurveyArtefactStorage.Document, "application/zip"),
    }.ToDictionary(k => k.Output, StringComparer.OrdinalIgnoreCase);

    /// <summary>How the output named <paramref name="output"/> is stored; null for a name outside the catalogue.</summary>
    public static SurveyArtefactKind? KindOf(string output) => Kinds.GetValueOrDefault(output.Trim());

    /// <summary>
    /// Whether an output row may still be listed. A model output is kept as a survey model, and
    /// once somebody has deleted that model on the web the row stops pointing at it while the file
    /// it was read from stays in the store; the row is then not listed any more — on the signed-in
    /// status nor on the public page — rather than handing out a model the cave's writers removed.
    /// </summary>
    public static bool Listable(string? output, Guid? surveyModelId) =>
        surveyModelId is not null
        || output is null
        || KindOf(output)?.Storage is not (SurveyArtefactStorage.LinePlot or SurveyArtefactStorage.Survex);

    /// <summary>The stored file name of an output of one version of a survey.</summary>
    public static string FileName(string surveyName, DateTimeOffset version, SurveyArtefactKind kind) =>
        SurveyVersionStamp.ArtefactName(surveyName, version, kind.Extension);

    /// <summary>The stored file name of the compiler's log of one version.</summary>
    public static string LogFileName(string surveyName, DateTimeOffset version) =>
        SurveyVersionStamp.ArtefactName(surveyName, version, "log");

    /// <summary>The stored file name of the configuration one version was compiled with.</summary>
    public static string ConfigFileName(string surveyName, DateTimeOffset version) =>
        SurveyVersionStamp.ArtefactName(surveyName, version, "thconfig");

    /// <summary>The stored file name of the survey generated around the uploaded one.</summary>
    public static string WrapperFileName(string surveyName, DateTimeOffset version) =>
        SurveyVersionStamp.ArtefactName(surveyName, version, "wrapper.th");

    /// <summary>The title of the document an output of one version becomes.</summary>
    public static string DocumentTitle(string surveyTitle, DateTimeOffset version, string label) =>
        SurveyVersionStamp.DocumentTitle($"{surveyTitle.Trim()} {label}", version);

    /// <summary>
    /// The uploads of one survey whose generated artefacts are pruned when the installation keeps
    /// the newest <paramref name="keep"/> versions' artefacts: every version holding artefacts
    /// after the newest <paramref name="keep"/> that hold them, newest first. Nothing when
    /// <paramref name="keep"/> is zero or less, which keeps every version's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Versions are ordered by their stamp, which is unique within a survey and increases in the
    /// order the uploads arrived. Only versions that hold artefacts are counted: a newer upload
    /// that was abandoned on the way, failed to compile or has not run yet does not take the
    /// place of the last version that produced something, so the survey is never left with no
    /// drawings because its latest attempt went wrong.
    /// </para>
    /// <para>
    /// A version that has not finished running is counted among the newest when it holds
    /// artefacts — a rerun — but is never itself pruned, so a run in progress cannot have its
    /// outputs taken from under it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid> Prunable(
        IEnumerable<(Guid UploadId, DateTimeOffset VersionAt, bool Finished, bool HasArtefacts)> versions, int keep)
    {
        if (keep <= 0)
        {
            return [];
        }

        return versions
            .Where(v => v.HasArtefacts)
            .OrderByDescending(v => v.VersionAt)
            .Skip(keep)
            .Where(v => v.Finished)
            .Select(v => v.UploadId)
            .ToList();
    }
}

/// <summary>
/// The coordinate system a compiler wrote its model in, as the EPSG code that names it, so the
/// model can be placed on the map when it is read.
/// </summary>
public static class TherionCoordinateSystems
{
    /// <summary>
    /// The EPSG code of a coordinate system as Therion names it in its log — <c>UTM35</c> (the
    /// northern zone), <c>UTM35N</c>, <c>UTM35S</c>, <c>EPSG:31700</c>, <c>lat-long</c> — or null
    /// for a name this cannot turn into a code. A survey fixed to no position has no output
    /// coordinate system at all, and its model's coordinates are local.
    /// </summary>
    public static int? EpsgOf(string? coordinateSystem)
    {
        if (string.IsNullOrWhiteSpace(coordinateSystem))
        {
            return null;
        }

        var cs = coordinateSystem.Trim();
        if (cs.StartsWith("epsg:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(cs.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var epsg)
            && epsg > 0)
        {
            return epsg;
        }

        if (cs.Equals("lat-long", StringComparison.OrdinalIgnoreCase)
            || cs.Equals("long-lat", StringComparison.OrdinalIgnoreCase))
        {
            return 4326;
        }

        if (cs.Length > 3 && cs.StartsWith("utm", StringComparison.OrdinalIgnoreCase))
        {
            var rest = cs.AsSpan(3);
            var south = false;
            if (rest[^1] is 'N' or 'n' or 'S' or 's')
            {
                south = rest[^1] is 'S' or 's';
                rest = rest[..^1];
            }

            if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var zone) && zone is >= 1 and <= 60)
            {
                return (south ? 32700 : 32600) + zone;
            }
        }

        return null;
    }
}
