// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Users;

/// <summary>One kind of row that keeps an account from being deleted, and how many of it there are.</summary>
public sealed record UserDeleteBlocker(string Kind, int Count);

/// <summary>
/// What an account still owns that has no sensible new owner. Deleting such an account used to
/// reach the database's foreign-key refusal and answer 500; the endpoint now counts these first and
/// refuses with a coded 409 instead.
/// </summary>
/// <remarks>
/// <para>
/// Every foreign key to the account table that restricts deletion has exactly one entry here, and a
/// test walks the EF model to prove it: a new owned kind that is not added to this list fails that
/// test instead of becoming a 500 in front of an administrator.
/// </para>
/// <para>
/// Soft-deleted rows are counted. A trip or a feature in the trash still carries its owner and still
/// holds the foreign key, so leaving it out would turn "you have two trips to hand over" into a
/// database error the moment the last live one was moved.
/// </para>
/// <para>
/// No names, titles or identifiers of the blocking rows are returned: the answer is a kind and a
/// number, which is enough to know what to hand over and never discloses what a protected cave or a
/// private trip is called to somebody who may not read it.
/// </para>
/// </remarks>
public static class UserDeleteBlockers
{
    /// <summary>The problem code for a refused deletion.</summary>
    public const string Code = "user.delete_blocked";

    /// <summary>One entry per kind: the entity, the columns that point at the account, and the count.</summary>
    public sealed record Entry(
        string Kind,
        Type Entity,
        string[] Columns,
        Func<SilexGisDbContext, Guid, CancellationToken, Task<int>> Count);

    // Every query ignores the global filters: they hide soft-deleted rows, which still hold the key.
    public static readonly IReadOnlyList<Entry> Entries =
    [
        new("albums", typeof(Album), ["OwnerUserId"],
            (db, id, ct) => db.Albums.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("cave_level_bands", typeof(CaveLevelBands), ["ConfirmedBy"],
            (db, id, ct) => db.CaveLevelBands.IgnoreQueryFilters().CountAsync(x => x.ConfirmedBy == id, ct)),
        new("cave_qr_publications", typeof(CaveQrPublication), ["PublishedBy"],
            (db, id, ct) => db.CaveQrPublications.IgnoreQueryFilters().CountAsync(x => x.PublishedBy == id, ct)),
        new("checklists", typeof(Checklist), ["OwnerUserId"],
            (db, id, ct) => db.Checklists.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("documents", typeof(Document), ["OwnerUserId"],
            (db, id, ct) => db.Documents.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("events", typeof(Event), ["OwnerUserId"],
            (db, id, ct) => db.Events.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("expeditions", typeof(Expedition), ["OwnerUserId"],
            (db, id, ct) => db.Expeditions.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("features", typeof(Feature), ["OwnerUserId"],
            (db, id, ct) => db.Features.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("feature_shares", typeof(FeatureShare), ["CreatedBy"],
            (db, id, ct) => db.FeatureShares.IgnoreQueryFilters().CountAsync(x => x.CreatedBy == id, ct)),
        new("geofiles", typeof(Geofile), ["OwnerUserId"],
            (db, id, ct) => db.Geofiles.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("georeferenced_maps", typeof(GeoreferencedMap), ["OwnerUserId"],
            (db, id, ct) => db.GeoreferencedMaps.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("import_batches", typeof(ImportBatch), ["ConfirmedByUserId"],
            (db, id, ct) => db.ImportBatches.IgnoreQueryFilters().CountAsync(x => x.ConfirmedByUserId == id, ct)),
        new("raster_maps", typeof(RasterMap), ["OwnerUserId"],
            (db, id, ct) => db.RasterMaps.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("survey_uploads", typeof(SurveyUpload), ["RequestedBy"],
            (db, id, ct) => db.SurveyUploads.IgnoreQueryFilters().CountAsync(x => x.RequestedBy == id, ct)),
        // Two columns of one table: a row that names the account twice is still one row to hand over.
        new("survey_upload_public_links", typeof(SurveyUploadPublicLink), ["CreatedBy", "ProtectedAcknowledgedBy"],
            (db, id, ct) => db.SurveyUploadPublicLinks.IgnoreQueryFilters()
                .CountAsync(x => x.CreatedBy == id || x.ProtectedAcknowledgedBy == id, ct)),
        new("trips", typeof(TripLog), ["OwnerUserId"],
            (db, id, ct) => db.TripLogs.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("trip_recordings", typeof(TripRecording), ["OwnerUserId"],
            (db, id, ct) => db.TripRecordings.IgnoreQueryFilters().CountAsync(x => x.OwnerUserId == id, ct)),
        new("trip_tracking_shares", typeof(TripTrackingShare), ["CreatedBy"],
            (db, id, ct) => db.TripTrackingShares.IgnoreQueryFilters().CountAsync(x => x.CreatedBy == id, ct)),
        new("upload_batches", typeof(UploadBatch), ["StartedByUserId"],
            (db, id, ct) => db.UploadBatches.IgnoreQueryFilters().CountAsync(x => x.StartedByUserId == id, ct)),
    ];

    /// <summary>The kinds that hold the account, in list order, each with a non-zero count.</summary>
    public static async Task<IReadOnlyList<UserDeleteBlocker>> FindAsync(
        SilexGisDbContext db, Guid userId, CancellationToken ct)
    {
        var found = new List<UserDeleteBlocker>();
        foreach (var entry in Entries)
        {
            var count = await entry.Count(db, userId, ct);
            if (count > 0)
            {
                found.Add(new UserDeleteBlocker(entry.Kind, count));
            }
        }

        return found;
    }
}
