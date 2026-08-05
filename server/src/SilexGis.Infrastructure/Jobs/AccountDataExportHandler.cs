// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Payload contract for <see cref="ProcessingJobKinds.AccountDataExport"/> jobs.</summary>
public sealed record AccountDataExportPayload(Guid ExportId);

/// <summary>
/// Packages one user's personal data into a zip of JSON documents.
/// </summary>
/// <remarks>
/// <para>
/// Personal data only: the profile and its visibility choices, addresses, notification and
/// interface preferences, caving group memberships, and an inventory of the content the user owns —
/// identifiers, names and dates, never geometry.
/// </para>
/// <para>
/// Coordinates are deliberately excluded. Cave locations may only be emitted after the location
/// protection rules are applied with the subject's current grants, and those rules live in a
/// layer this one may not reference. Rather than ship an unprotected dump or break the layering,
/// the archive stays to personal data — the content itself is already exportable through the
/// export endpoints, where the protection does run.
/// </para>
/// </remarks>
public sealed class AccountDataExportHandler(SilexGisDbContext db, IFileStore fileStore) : IProcessingJobHandler
{
    /// <summary>How long an archive stays downloadable before it may be swept.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions WriteOptions =
        new(JsonSerializerOptions.Web) { WriteIndented = true };

    public string Kind => ProcessingJobKinds.AccountDataExport;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AccountDataExportPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty account-data-export payload.");

        var export = await db.AccountDataExports.FirstOrDefaultAsync(e => e.Id == payload.ExportId, ct)
            ?? throw new InvalidOperationException($"Account data export {payload.ExportId} no longer exists.");

        export.Status = AccountExportStatus.Running;
        await db.SaveChangesAsync(ct);

        try
        {
            // The subject comes from the stored row, never from the payload.
            var (storagePath, sizeBytes) = await BuildArchiveAsync(export.UserId, ct);

            export.StoragePath = storagePath;
            export.SizeBytes = sizeBytes;
            export.Status = AccountExportStatus.Ready;
            export.Error = null;
            export.CompletedAt = DateTimeOffset.UtcNow;
            export.ExpiresAt = DateTimeOffset.UtcNow.Add(Retention);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            export.Status = AccountExportStatus.Failed;
            export.Error = e.Message;
            export.CompletedAt = DateTimeOffset.UtcNow;
            // Recorded even when the job was cancelled, or the row would sit on Running forever.
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(string StoragePath, long SizeBytes)> BuildArchiveAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);

        var addresses = await db.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

        var notifications = await db.UserNotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        // Membership belongs to the person, so it is read through their caver row — which is
        // also the account's own roster entry, and part of what this export owes them.
        var caver = await db.Cavers.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);

        var cavingGroups = caver is null
            ? []
            : await db.CavingGroupMemberships.AsNoTracking()
                .Where(m => m.CaverId == caver.Id)
                .Join(db.CavingGroups.AsNoTracking(), m => m.CavingGroupId, g => g.Id, (m, g) => new { g.Id, g.Name, m.Role, m.CreatedAt })
                .ToListAsync(ct);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(archive, "account.json", new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.EmailConfirmed,
                user.FirstName,
                user.LastName,
                user.DisplayName,
                user.Bio,
                user.PhoneNumber,
                user.CavingClubId,
                user.Locale,
                user.AvatarPreset,
                user.CreatedAt,
                user.UpdatedAt,
            }, ct);

            await WriteEntryAsync(archive, "visibility.json", new
            {
                RealName = user.RealNameVisibility.ToString(),
                Bio = user.BioVisibility.ToString(),
                Email = user.EmailVisibility.ToString(),
                Phone = user.PhoneVisibility.ToString(),
                CavingClub = user.CavingClubVisibility.ToString(),
                Address = user.AddressVisibility.ToString(),
                AddressPoint = user.AddressPointVisibility.ToString(),
            }, ct);

            await WriteEntryAsync(archive, "addresses.json", addresses.Select(a => new
            {
                a.Id,
                a.Label,
                a.Country,
                a.City,
                a.AddressText,
                Longitude = a.Geom?.X,
                Latitude = a.Geom?.Y,
                a.CreatedAt,
            }), ct);

            await WriteEntryAsync(archive, "preferences.json", new
            {
                NotificationEmail = user.NotifyEmailEnabled,
                Digest = user.NotifyDigest.ToString(),
                Categories = notifications.Select(p => new { Category = p.Category.ToString(), p.Enabled }),
                Interface = JsonSerializer.Deserialize<JsonElement>(user.UiPreferences),
            }, ct);

            await WriteEntryAsync(archive, "caving-groups.json", cavingGroups.Select(g => new
            {
                g.Id,
                g.Name,
                Role = g.Role.ToString(),
                JoinedAt = g.CreatedAt,
            }), ct);

            if (caver is not null)
            {
                await WriteEntryAsync(archive, "caver.json", new
                {
                    caver.Id,
                    caver.FullName,
                    caver.Email,
                    caver.Phone,
                    caver.Notes,
                }, ct);
            }

            await WriteEntryAsync(archive, "content.json", await ContentInventoryAsync(userId, ct), ct);
            await WriteEntryAsync(archive, "reading.json", await ReadingHistoryAsync(userId, ct), ct);
            await WriteReadmeAsync(archive, ct);
        }

        buffer.Position = 0;
        var storagePath = await fileStore.SaveAsync(buffer, ".zip", ct);
        return (storagePath, buffer.Length);
    }

    /// <summary>
    /// What the user has authored, as identifiers and names. No geometry — see the note on the
    /// handler for why coordinates cannot be packaged here.
    /// </summary>
    private async Task<object> ContentInventoryAsync(Guid userId, CancellationToken ct) => new
    {
        // One inventory over the supertype covers caves, entrances, centerlines and every
        // generic kind (the soft-delete query filter hides deleted rows).
        Features = await db.Features.AsNoTracking()
            .Where(f => f.OwnerUserId == userId)
            .Select(f => new { f.Id, Kind = f.Kind.ToString(), f.Name, f.CreatedAt })
            .ToListAsync(ct),
        TripLogs = await db.TripLogs.AsNoTracking()
            .Where(t => t.OwnerUserId == userId)
            .Select(t => new { t.Id, t.Title, t.TripDate, t.CreatedAt })
            .ToListAsync(ct),
        Geofiles = await db.Geofiles.AsNoTracking()
            .Where(g => g.OwnerUserId == userId)
            .Select(g => new { g.Id, g.Name, g.CreatedAt })
            .ToListAsync(ct),
        MapViews = await db.MapViews.AsNoTracking()
            .Where(v => v.OwnerUserId == userId)
            .Select(v => new { v.Id, v.Name, v.CreatedAt })
            .ToListAsync(ct),
        // Uploader identity lives on the revision a file belongs to, so the personal-data
        // export reaches it through that join rather than through the file row.
        Files = await (from file in db.StoredFiles.AsNoTracking()
                       join version in db.DocumentVersions.AsNoTracking()
                           on file.DocumentVersionId equals version.Id
                       where version.UploadedBy == userId
                       select new { file.Id, file.OriginalName, file.SizeBytes, file.CreatedAt })
            .ToListAsync(ct),
    };

    /// <summary>
    /// The record this installation holds of the user having taken a copy of a document.
    /// It is data about them rather than about the documents, so a request for a copy of
    /// their own data owes it to them even though nobody asked for it to be collected.
    /// Identifiers, times and titles only — the documents themselves are not reproduced,
    /// for the same reason the rest of this archive names records rather than copying them.
    /// </summary>
    private async Task<object> ReadingHistoryAsync(Guid userId, CancellationToken ct) =>
        await (from read in db.FileAccessEvents.AsNoTracking()
               join document in db.Documents.AsNoTracking() on read.DocumentId equals document.Id
               where read.UserId == userId
               orderby read.Id descending
               select new { read.At, read.DocumentId, document.Title, read.FileId })
            .ToListAsync(ct);

    private static async Task WriteEntryAsync(ZipArchive archive, string name, object value, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, WriteOptions, ct);
    }

    private static async Task WriteReadmeAsync(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(
            """
            This archive holds the personal data SilexGIS stores about your account:

              account.json      profile fields and the address you sign in with
              visibility.json   who you chose to show each profile field to
              addresses.json    your saved addresses, with coordinates where you set one
              preferences.json  notification and interface settings
              caver.json          your entry in the roster of people
              caving-groups.json  the caving groups you belong to
              content.json      what you have authored: identifiers, names and dates
              reading.json      when you took a copy of a document, and which one

            The records you authored are not reproduced here in full. Use the export options on
            the caves, features and trip pages for those — they apply the rules that protect
            sensitive locations, which a bulk copy of the database could not.
            """), ct);
    }
}
