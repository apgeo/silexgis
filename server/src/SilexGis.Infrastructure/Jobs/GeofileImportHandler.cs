// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.GeofileImport"/> jobs.</summary>
public sealed record GeofileImportPayload(Guid GeofileId);

/// <summary>
/// Materializes an uploaded geofile into geofile_features rows (geometries normalized
/// to 4326 by the vector reader) and stamps count/bbox/status on the geofile.
/// Re-import is safe: previous rows are replaced.
/// </summary>
public sealed class GeofileImportHandler(
    SilexGisDbContext db,
    IFileStore fileStore,
    IVectorIO vectorIO) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.GeofileImport;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<GeofileImportPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty geofile-import payload.");

        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == payload.GeofileId, ct)
            ?? throw new InvalidOperationException($"Geofile {payload.GeofileId} no longer exists.");
        var file = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == geofile.FileId, ct)
            ?? throw new InvalidOperationException($"Stored file {geofile.FileId} no longer exists.");

        geofile.ImportStatus = GeofileImportStatus.Importing;
        await db.SaveChangesAsync(ct);

        try
        {
            var dataset = vectorIO.Read(
                fileStore.GetAbsolutePath(file.StoragePath), geofile.Format, ReadSourceOptions(geofile));

            await db.GeofileFeatures.Where(f => f.GeofileId == geofile.Id).ExecuteDeleteAsync(ct);
            await GeodataSql.BulkInsertFeaturesAsync(db, geofile.Id, dataset.Features, ct);

            var envelope = new Envelope();
            foreach (var feature in dataset.Features)
            {
                envelope.ExpandToInclude(feature.Geom.EnvelopeInternal);
            }

            geofile.FeatureCount = dataset.Features.Count;
            geofile.Srid = dataset.SourceSrid ?? geofile.Srid;
            geofile.Bbox = ToBboxPolygon(envelope);
            geofile.ImportStatus = GeofileImportStatus.Imported;
            geofile.ImportError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            geofile.ImportStatus = GeofileImportStatus.Failed;
            geofile.ImportError = e is VectorIOException ? e.Message : "Import failed unexpectedly.";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The stored parse options, or none. Unreadable options are treated as absent rather than
    /// as a failure: the reader's own detection is a working answer, and refusing to read a
    /// file because a settings blob got mangled would be a worse outcome than reading it the
    /// way the file describes itself.
    /// </summary>
    private static GeofileSourceOptions? ReadSourceOptions(Geofile geofile)
    {
        if (string.IsNullOrWhiteSpace(geofile.SourceOptions))
        {
            return null;
        }

        try
        {
            return ImportJson.Deserialize<GeofileSourceOptions>(geofile.SourceOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Envelope → 4326 polygon; degenerate envelopes get a hair of width.</summary>
    private static Polygon ToBboxPolygon(Envelope envelope)
    {
        if (envelope.Width == 0 || envelope.Height == 0)
        {
            envelope.ExpandBy(0.00001);
        }

        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        return (Polygon)factory.ToGeometry(envelope);
    }
}
