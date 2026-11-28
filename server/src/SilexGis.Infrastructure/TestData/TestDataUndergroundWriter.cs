// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// Writes the underground of the caves whose survey a run keeps: the survey as a Survex image
/// file, read on the ordinary path as an upload is (stations, legs, passages, walls, centreline
/// and measures come from the one reader), its source as a Survex text file, the survey it
/// belongs to, the cave's deepest point and a landmark as depth places, and a confirmed reading of
/// its levels.
/// </summary>
public static class TestDataUndergroundWriter
{
    /// <summary>Caves whose survey is written per save and per scope.</summary>
    public const int CaveChunk = 25;

    public static async Task WriteAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore files,
        TestDataSession session,
        IReadOnlyList<PlannedCave> caves,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(caves);
        var owner = session.OwnerUserId;
        var written = new List<string>();
        try
        {
            await WriteCavesAsync(db, documents, new RecordingStore(files, written), session, caves, owner, ct);
        }
        catch
        {
            // The rows of this chunk roll back with its transaction; the bytes it already put in
            // the store would stay there with nothing naming them, so they go too.
            foreach (var path in written)
            {
                try
                {
                    await files.DeleteAsync(path, CancellationToken.None);
                }
                catch (IOException)
                {
                    // Nothing more can be done for a file the store will not delete; the run fails anyway.
                }
            }

            throw;
        }
    }

    private static async Task WriteCavesAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore files,
        TestDataSession session,
        IReadOnlyList<PlannedCave> caves,
        Guid owner,
        CancellationToken ct)
    {
        foreach (var cave in caves)
        {
            if (cave is not { WithSurvey: true, Network: { } network } || !session.Caves.TryGetValue(cave.Index, out var caveId))
            {
                continue;
            }

            var main = cave.Entrances[0];
            var last = network.Surveys[^1].Date;
            var survey = new CaveSurvey
            {
                CaveFeatureId = caveId,
                Title = $"Topografia {cave.Name}",
                Source = SurveyOrigin.Other,
                Description = Footer(session, $"Cartată în {network.Surveys.Count} ture, între {network.Surveys[0].Date.Year} și {last.Year}."),
            };
            db.CaveSurveys.Add(survey);

            // Most surveys as Survex image files, every fourth as a Therion one, so both readers run.
            var therion = TakesTherionFile(cave);
            var image = therion ? Lox(network) : Survex3d(network);
            var model = new SurveyModel
            {
                CaveFeatureId = caveId,
                Name = $"{cave.Name} ({last.Year})",
                FileId = (await StoreAsync(documents, files, owner, image, Slug(cave.Name) + (therion ? ".lox" : ".3d"), "application/octet-stream", FileKind.Survey, ct)).File.Id,
                Format = therion ? SurveyModelFormat.Lox : SurveyModelFormat.Survex3d,
                Description = Footer(session, "Topografie inventată de generatorul de date de test."),
                SurveyedAt = last,
                CaveSurveyId = survey.Id,

                // The survey's zero is the main entrance, so that is where the reader is told it lies.
                Anchor = new Point(main.Lon, main.Lat) { SRID = 4326 },
                AnchorHeightM = main.AltitudeM,
            };
            db.SurveyModels.Add(model);
            session.SurveyModels[cave.Index] = model.Id;

            // Its reading is queued for the worker, as an upload's is: what a survey is read into
            // comes from the one reader, on the one path. Asked for by nobody, so nobody is told
            // when each of a run's hundreds of readings finishes.
            SurveyModelReading.QueueFirst(db, model, requestedBy: null);
            await SurveyModelCurrency.TakeIfUnclaimedAsync(db, model, ct);

            var source = Svx(cave, network);
            var sourceFile = await StoreAsync(documents, files, owner, source, Slug(cave.Name) + ".svx", "text/plain", FileKind.Document, ct);
            db.SurveySources.Add(new SurveySource
            {
                CaveFeatureId = caveId,
                DocumentId = sourceFile.Version.DocumentId,
                Kind = SurveySourceKind.SurvexSource,
                Name = $"Sursa topografiei {cave.Name}",
                OriginalFileName = Slug(cave.Name) + ".svx",
                CaveSurveyId = survey.Id,
            });

            // The deepest point and, in a cave large enough to have one, a landmark on the way to it.
            var named = network.Stations.Where(s => s.Name.Length > 0).ToList();
            var deepest = named.MinBy(s => s.Z)!;
            if (deepest.Z < -2)
            {
                db.CaveDepthPlaces.Add(new CaveDepthPlace
                {
                    CaveFeatureId = caveId,
                    DepthM = Math.Round((decimal)-deepest.Z, 1),
                    ViewerStationName = deepest.Name,
                    PlaceLabel = "Punctul cel mai adânc",
                    IsLandmark = true,
                });
            }

            // A cave keeps one place per depth, so the landmark is the widest station at a depth of its own.
            var landmark = named.Where(s => !s.Entrance && Math.Round(s.Z, 1) != Math.Round(deepest.Z, 1)).MaxBy(s => s.Left + s.Right);
            if (named.Count > 40 && landmark is not null)
            {
                db.CaveDepthPlaces.Add(new CaveDepthPlace
                {
                    CaveFeatureId = caveId,
                    DepthM = Math.Round((decimal)-landmark.Z, 1),
                    ViewerStationName = landmark.Name,
                    PlaceLabel = "Sala Mare",
                    Note = "Cea mai largă sală a peșterii.",
                });
            }

            // A reading of the cave's levels that somebody confirmed: upper and lower halves of its height.
            if (network.DepthM >= 20)
            {
                var top = main.AltitudeM + network.TopZ;
                var bottom = main.AltitudeM + network.BottomZ;
                var middle = Math.Round((top + bottom) / 2, 1);
                db.CaveLevelBands.Add(new CaveLevelBands
                {
                    CaveFeatureId = caveId,
                    ConfirmedBy = owner,
                    Bands = JsonSerializer.Serialize(new[]
                    {
                        new { FromM = Math.Round(bottom, 1), ToM = middle, Label = (string?)"Nivelul inferior" },
                        new { FromM = middle, ToM = Math.Round(top, 1), Label = (string?)"Nivelul superior" },
                    }),
                    Note = "Nivelurile propuse de generatorul de date de test.",
                });
            }

        }

        await session.SaveAndRegisterAsync(db, ct);
    }

    /// <summary>Whether a surveyed cave's survey is written as a Therion file rather than a Survex one: every fourth.</summary>
    public static bool TakesTherionFile(PlannedCave cave) => cave is not null && cave.Index % 4 == 1;

    /// <summary>
    /// The survey as a Therion file: its stations and the legs between them. The format joins legs
    /// to named stations only and carries no dates, so the wall shots and the days stay out.
    /// </summary>
    public static byte[] Lox(CaveNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var stations = network.Stations
            .Where(s => s.Name.Length > 0)
            .Select(s => new InventedSurveyFile.Station(s.Name, s.X, s.Y, s.Z, s.Entrance))
            .ToList();
        var legs = network.Legs.Where(l => !l.Splay).Select(l =>
        {
            var a = network.Stations[l.From];
            var b = network.Stations[l.To];
            return new InventedSurveyFile.Leg((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
        }).ToList();
        return InventedSurveyFile.Lox(stations, legs);
    }

    /// <summary>The survey as a Survex image file: its stations, its legs with the day each was drawn, and its passages.</summary>
    public static byte[] Survex3d(CaveNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var stations = network.Stations
            .Where(s => s.Name.Length > 0)
            .Select(s => new InventedSurveyFile.Station(s.Name, s.X, s.Y, s.Z, s.Entrance))
            .ToList();
        var legs = network.Legs.Select(l =>
        {
            var a = network.Stations[l.From];
            var b = network.Stations[l.To];
            return new InventedSurveyFile.Leg((a.X, a.Y, a.Z), (b.X, b.Y, b.Z), l.Splay, network.Surveys[l.Survey].Date);
        }).ToList();
        return InventedSurveyFile.Survex3d(stations, legs, Passages(network));
    }

    /// <summary>The passages as runs of cross-sections: each run follows legs end to end until the chain breaks.</summary>
    private static List<InventedSurveyFile.CrossSection[]> Passages(CaveNetwork network)
    {
        var runs = new List<InventedSurveyFile.CrossSection[]>();
        var current = new List<int>();
        foreach (var leg in network.Legs.Where(l => !l.Splay))
        {
            if (current.Count > 0 && current[^1] != leg.From)
            {
                Flush();
            }

            if (current.Count == 0)
            {
                current.Add(leg.From);
            }

            current.Add(leg.To);
        }

        Flush();
        return runs;

        void Flush()
        {
            if (current.Count >= 2)
            {
                runs.Add([.. current.Select(i => network.Stations[i])
                    .Select(s => new InventedSurveyFile.CrossSection(s.Name, s.Left, s.Right, s.Up, s.Down))]);
            }

            current.Clear();
        }
    }

    /// <summary>
    /// The survey's source as a Survex text file: one block per survey with its date, legs as
    /// tape, compass and clino, and the passage sizes — what the surveyors would have typed in.
    /// </summary>
    public static byte[] Svx(PlannedCave cave, CaveNetwork network)
    {
        ArgumentNullException.ThrowIfNull(cave);
        ArgumentNullException.ThrowIfNull(network);
        var text = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        text.Append("; ").Append(cave.Name).Append(" — topografie inventată de generatorul de date de test\n");
        text.Append("*begin ").Append(Slug(cave.Name).Replace('-', '_')).Append('\n');
        text.Append("*entrance ").Append(network.Stations[0].Name).Append('\n');
        for (var s = 0; s < network.Surveys.Count; s++)
        {
            var legs = network.Legs.Where(l => l.Survey == s && !l.Splay).ToList();
            if (legs.Count == 0)
            {
                continue;
            }

            text.Append("\n*date ").Append(network.Surveys[s].Date.ToString("yyyy.MM.dd", c)).Append('\n');
            text.Append("*data normal from to tape compass clino\n");
            foreach (var leg in legs)
            {
                var a = network.Stations[leg.From];
                var b = network.Stations[leg.To];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var dz = b.Z - a.Z;
                var tape = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                var compass = ((Math.Atan2(dx, dy) * 180 / Math.PI) + 360) % 360;
                var clino = tape == 0 ? 0 : Math.Asin(dz / tape) * 180 / Math.PI;
                text.Append(c, $"{a.Name}\t{b.Name}\t{tape:0.00}\t{compass:0.0}\t{clino:0.0}\n");
            }
        }

        text.Append("*end ").Append(Slug(cave.Name).Replace('-', '_')).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>
    /// Puts the bytes in the file store and records them as a document, as an upload is recorded.
    /// The bytes are written before the rows, which name them; a chunk that fails takes its bytes
    /// back (see <see cref="WriteAsync"/>).
    /// </summary>
    private static async Task<DocumentFile> StoreAsync(
        DocumentWriteService documents,
        IFileStore files,
        Guid owner,
        byte[] bytes,
        string fileName,
        string mimeType,
        FileKind kind,
        CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var path = await files.SaveAsync(stream, Path.GetExtension(fileName), ct);
        return documents.Create(
            new StoredContent(path, fileName, mimeType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)), kind),
            Path.GetFileNameWithoutExtension(fileName),
            owner,
            owner);
    }

    /// <summary>The file store, noting the path of everything saved through it.</summary>
    private sealed class RecordingStore(IFileStore inner, List<string> saved) : IFileStore
    {
        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
        {
            var path = await inner.SaveAsync(content, extension, ct);
            saved.Add(path);
            return path;
        }

        public Task<IExclusiveContentWriter> OpenExclusiveAsync(string storagePath, CancellationToken ct = default) =>
            inner.OpenExclusiveAsync(storagePath, ct);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) => inner.OpenReadAsync(storagePath, ct);

        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => inner.DeleteAsync(storagePath, ct);

        public string GetAbsolutePath(string storagePath) => inner.GetAbsolutePath(storagePath);
    }

    private static string Footer(TestDataSession session, string text) =>
        session.Profile.Marking.DescriptionFooter ? $"{text}\n\n{TestDataTextRo.Footer(session.Profile.Marking.Tag)}" : text;

    private static string Slug(string name) => Tag.Slugify(name);
}
