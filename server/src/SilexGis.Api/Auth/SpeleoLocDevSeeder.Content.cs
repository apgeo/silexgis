// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ImageMagick;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Auth;

/// <summary>
/// The part of the development dataset a phone reads beyond features: photos, a trip recording, a
/// raster map, the people directory, and an account whose uploads wait for a reviewer.
/// </summary>
/// <remarks>
/// <para>
/// Each piece is laid out so that what the protection rules do to it is visible from the plain
/// account. Two photos hang on the open demo cave: one on it alone, which that account is handed
/// as stored, and one on it and on the protected cave too, which that account is handed without
/// its location — the protected cave is part of where the camera may have been, and that account
/// may not be told where the protected cave is. The raster map is of the open cave, with one pin on
/// an ordinary place and one on a place that is its own protection root, so the map arrives with
/// one pin and not two.
/// </para>
/// <para>
/// Every picture is drawn here, never taken from anywhere. The position stamped into the photos is
/// invented and lies near the invented demo caves.
/// </para>
/// <para>
/// Everything is owned by the administrator, never by the plain account: owning a row is one of
/// the two ways the exact-location rule is short-circuited, and an account that owned what it is
/// meant to be refused would demonstrate nothing. The one row the plain account is the source of
/// is a phone user it reported, because that is what the people directory hands back to the phone
/// that sent it.
/// </para>
/// </remarks>
public static partial class SpeleoLocDevSeeder
{
    /// <summary>The open demo cave the photos, the recording and the raster map hang on.</summary>
    public const string OpenCaveCode = "DEMO-0001";

    /// <summary>The demo cave whose position is protected.</summary>
    public const string ProtectedCaveCode = "DEMO-0002";

    /// <summary>A photo on the open cave alone: the plain account is handed it as stored.</summary>
    public const string OpenPhotoTitle = "Demo photo — open cave";

    /// <summary>
    /// A photo on the open cave and on the protected one: the plain account is handed it without
    /// its location, the administrator as stored.
    /// </summary>
    public const string SharedPhotoTitle = "Demo photo — open and protected caves";

    /// <summary>An ordinary place inside the open cave.</summary>
    public const string OpenPlaceName = "Demo marker A";

    /// <summary>A place inside the open cave that is its own protection root.</summary>
    public const string ProtectedPlaceName = "Demo marker B (protected)";

    public const string RasterTitle = "Demo plan";

    public const string RecordingTitle = "Demo trip recording";

    /// <summary>A person on the roster without an account, for the directory to list.</summary>
    public const string RosterOnlyCaverName = "Demo Caver";

    /// <summary>
    /// The phone's own identifier for a user of the SpeleoLoc app that the plain account's phone
    /// reported, linked to <see cref="RosterOnlyCaverName"/>. Fixed, so a client developer can
    /// recognise it in the directory's answer.
    /// </summary>
    public static readonly Guid PhoneUserId = new("0d5e0000-0000-7000-8000-00000000d0e1");

    /// <summary>
    /// An account whose uploads wait for a reviewer. It signs in with the plain account's
    /// password, so one value configures both; it is otherwise the plain account's twin — a Viewer
    /// in the same group, owning nothing — so that the mark is the only difference between them.
    /// </summary>
    public const string ReviewEmail = "review@dev.local";

    public const string ReviewDisplayName = "Demo Reviewed Member";

    private static async Task SeedSyncContentAsync(
        IServiceProvider services,
        SilexGisDbContext db,
        UserManager<SilexGisUser> userManager,
        Guid adminUserId,
        Guid memberUserId,
        Guid groupId,
        string password,
        CancellationToken ct)
    {
        var openCaveId = await CaveIdAsync(db, OpenCaveCode, ct);
        var protectedCaveId = await CaveIdAsync(db, ProtectedCaveCode, ct);

        // The demonstration caves come from the other seed command; without them there is nothing
        // to hang a photo or a map on, and the accounts below are still worth having.
        if (openCaveId is { } open && protectedCaveId is { } guarded)
        {
            var documents = services.GetRequiredService<DocumentWriteService>();
            var fileStore = services.GetRequiredService<IFileStore>();
            var writer = services.GetRequiredService<FeatureWriteService>();

            await EnsurePhotoAsync(db, documents, fileStore, adminUserId, OpenPhotoTitle, 45.5311, 25.4471, [open], ct);
            await EnsurePhotoAsync(
                db, documents, fileStore, adminUserId, SharedPhotoTitle, 45.5313, 25.4473, [open, guarded], ct);
            await db.SaveChangesAsync(ct);

            var openPlace = await EnsurePlaceAsync(db, writer, adminUserId, open, OpenPlaceName, false, ct);
            var protectedPlace = await EnsurePlaceAsync(
                db, writer, adminUserId, open, ProtectedPlaceName, true, ct);
            await db.SaveChangesAsync(ct);

            await EnsureRasterAsync(db, documents, fileStore, adminUserId, open, openPlace, protectedPlace, ct);
            await EnsureRecordingAsync(db, adminUserId, groupId, open, openPlace, ct);
            await db.SaveChangesAsync(ct);
        }

        var caverId = await EnsureRosterOnlyCaverAsync(db, groupId, ct);
        await EnsurePhoneUserAsync(db, memberUserId, caverId, ct);
        await db.SaveChangesAsync(ct);

        var reviewUserId = await EnsureReviewAccountAsync(db, userManager, password, ct);
        await EnsureMembershipAsync(db, groupId, reviewUserId, CavingGroupRole.Member, ct);
        await db.SaveChangesAsync(ct);
    }

    private static Task<Guid?> CaveIdAsync(SilexGisDbContext db, string code, CancellationToken ct) =>
        db.Caves.Where(c => c.IdentificationCode == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    private static async Task EnsurePhotoAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid adminUserId,
        string title,
        double latitude,
        double longitude,
        IReadOnlyList<Guid> featureIds,
        CancellationToken ct)
    {
        if (await db.Documents.AnyAsync(d => d.Title == title && d.OwnerUserId == adminUserId, ct))
        {
            return;
        }

        var bytes = PlacedJpeg(latitude, longitude);
        var made = documents.Create(
            await StoreContentAsync(fileStore, bytes, $"{Slug(title)}.jpg", "image/jpeg", FileKind.Image, ct),
            title: title,
            ownerUserId: adminUserId,
            uploadedBy: adminUserId);

        // Readable by every signed-in account, as the demo caves are: what differs between the two
        // accounts is the location the file carries, not whether they may see the photo.
        db.Documents.Local.Single(d => d.Id == made.Version.DocumentId).Visibility = Visibility.Public;
        db.PhotoDetails.Add(new PhotoDetails { DocumentId = made.Version.DocumentId, Caption = title });

        var order = 0;
        foreach (var featureId in featureIds)
        {
            db.Attachments.Add(new Attachment
            {
                FileId = made.File.Id,
                FeatureId = featureId,
                Role = AttachmentRole.PhotoInterior,
                SortOrder = order++,
                AddedBy = adminUserId,
            });
        }
    }

    private static async Task<Guid> EnsurePlaceAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        Guid adminUserId,
        Guid caveId,
        string name,
        bool locationProtected,
        CancellationToken ct)
    {
        var placeTypeId = await db.FeatureTypes.Where(t => t.Code == "cave_place").Select(t => t.Id).FirstAsync(ct);
        var existing = await db.Features
            .Where(f => f.Name == name && f.FeatureTypeId == placeTypeId && f.OwnerUserId == adminUserId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is { } id)
        {
            return id;
        }

        var place = new Feature
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            FeatureTypeId = placeTypeId,
            OwnerUserId = adminUserId,
            Visibility = Visibility.Authenticated,

            // Its own protection root, independently of the open cave around it — the case the
            // sync channel withholds per row rather than per cave.
            LocationProtected = locationProtected,
            Properties = """{"speleolocSchemaVersion":1}""",
        };
        await writer.CreateGenericAsync(place, [new ParentSpec(caveId, IsPrimary: true)], ct);
        return place.Id;
    }

    private static async Task EnsureRasterAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        Guid adminUserId,
        Guid caveId,
        Guid openPlaceId,
        Guid protectedPlaceId,
        CancellationToken ct)
    {
        if (await db.RasterMaps.AnyAsync(m => m.CaveFeatureId == caveId && m.Title == RasterTitle, ct))
        {
            return;
        }

        var bytes = PlainPng();
        var made = documents.Create(
            await StoreContentAsync(fileStore, bytes, "demo-plan.png", "image/png", FileKind.Image, ct),
            title: RasterTitle,
            ownerUserId: adminUserId,
            uploadedBy: adminUserId);
        db.Documents.Local.Single(d => d.Id == made.Version.DocumentId).Visibility = Visibility.Public;

        var map = new RasterMap
        {
            Id = Guid.CreateVersion7(),
            CaveFeatureId = caveId,
            Title = RasterTitle,
            MapType = RasterMapType.PlanView,
            DocumentId = made.Version.DocumentId,
            OwnerUserId = adminUserId,
        };
        db.RasterMaps.Add(map);
        var placePoints = new PlacePointStore(db);
        await placePoints.AddAsync(map, made.File, Guid.CreateVersion7(), openPlaceId, 60, 40, adminUserId, ct);
        await placePoints.AddAsync(map, made.File, Guid.CreateVersion7(), protectedPlaceId, 140, 90, adminUserId, ct);

        // The browser lists a cave's maps by the files hung on it with the map role.
        db.Attachments.Add(new Attachment
        {
            FileId = made.File.Id,
            FeatureId = caveId,
            Role = AttachmentRole.Map2d,
            Caption = RasterTitle,
            AddedBy = adminUserId,
        });
    }

    private static async Task EnsureRecordingAsync(
        SilexGisDbContext db, Guid adminUserId, Guid groupId, Guid caveId, Guid placeId, CancellationToken ct)
    {
        if (await db.TripRecordings.AnyAsync(r => r.CaveFeatureId == caveId && r.Title == RecordingTitle, ct))
        {
            return;
        }

        var started = new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);
        var recording = new TripRecording
        {
            Id = Guid.CreateVersion7(),
            CaveFeatureId = caveId,
            Title = RecordingTitle,
            Description = "An invented trip, recorded on a phone, for a client to read back.",
            StartedAt = started,
            EndedAt = started.AddHours(3),
            OwnerUserId = adminUserId,
            CavingGroupId = groupId,
            Visibility = Visibility.CavingGroup,
        };
        recording.Points.Add(new TripRecordingPoint
        {
            Id = Guid.CreateVersion7(),
            RecordingId = recording.Id,
            PlaceFeatureId = placeId,
            ScannedAt = started.AddMinutes(45),
            Notes = "Marker scanned on the way in.",
        });
        db.TripRecordings.Add(recording);
    }

    private static async Task<Guid> EnsureRosterOnlyCaverAsync(SilexGisDbContext db, Guid groupId, CancellationToken ct)
    {
        var caver = await db.Cavers.FirstOrDefaultAsync(c => c.FullName == RosterOnlyCaverName && c.UserId == null, ct);
        if (caver is null)
        {
            caver = new Caver { FullName = RosterOnlyCaverName, ShortName = "Demo C." };
            db.Cavers.Add(caver);
        }

        if (!await db.CavingGroupMemberships.AnyAsync(m => m.CavingGroupId == groupId && m.CaverId == caver.Id, ct))
        {
            db.CavingGroupMemberships.Add(new CavingGroupMembership
            {
                CavingGroupId = groupId,
                CaverId = caver.Id,
                Role = CavingGroupRole.Member,
            });
        }

        return caver.Id;
    }

    private static async Task EnsurePhoneUserAsync(
        SilexGisDbContext db, Guid memberUserId, Guid caverId, CancellationToken ct)
    {
        if (await db.DevicePersons.AnyAsync(p => p.DeviceUserId == PhoneUserId, ct))
        {
            return;
        }

        var seen = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
        db.DevicePersons.Add(new DevicePerson
        {
            DeviceUserId = PhoneUserId,
            Username = "democaver",
            FirstName = "Demo",
            LastName = "Caver",
            CaverId = caverId,
            LinkSource = DevicePersonLinkSource.Device,
            FirstSeenAt = seen,
            LastSeenAt = seen,
            UploadedByUserId = memberUserId,
        });
    }

    private static async Task<Guid> EnsureReviewAccountAsync(
        SilexGisDbContext db, UserManager<SilexGisUser> userManager, string password, CancellationToken ct)
    {
        if (await userManager.FindByEmailAsync(ReviewEmail) is { } existing)
        {
            if (!existing.UploadsNeedReview)
            {
                existing.UploadsNeedReview = true;
                _ = await userManager.UpdateAsync(existing);
            }

            return existing.Id;
        }

        var account = new SilexGisUser
        {
            UserName = ReviewEmail,
            Email = ReviewEmail,
            EmailConfirmed = true,
            DisplayName = ReviewDisplayName,
            UploadsNeedReview = true,
        };
        var result = await userManager.CreateAsync(account, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The development account under review could not be created: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        _ = await userManager.AddToRoleAsync(account, GlobalRoles.Viewer);
        CaverDirectory.CreateForNewAccount(db, account.Id, account.DisplayName, account.UserName, account.Email);
        await db.SaveChangesAsync(ct);
        return account.Id;
    }

    private static async Task<StoredContent> StoreContentAsync(
        IFileStore fileStore, byte[] bytes, string fileName, string mimeType, FileKind kind, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var path = await fileStore.SaveAsync(stream, Path.GetExtension(fileName), ct);
        return new StoredContent(path, fileName, mimeType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)), kind);
    }

    private static string Slug(string title) =>
        new([.. title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);

    /// <summary>
    /// A drawn picture with an invented GPS fix and an invented camera record — never a real
    /// photograph.
    /// </summary>
    private static byte[] PlacedJpeg(double latitude, double longitude)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 200, 150);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, ToDms(latitude));
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, ToDms(longitude));
        exif.SetValue(ExifTag.Make, "Invented Optics");
        exif.SetValue(ExifTag.Model, "Demo Body 1");
        image.SetProfile(exif);
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static byte[] PlainPng()
    {
        using var image = new MagickImage(MagickColors.Beige, 240, 160);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static Rational[] ToDms(double degrees)
    {
        var d = (uint)degrees;
        var minutesFull = (degrees - d) * 60d;
        var m = (uint)minutesFull;
        return [new Rational(d), new Rational(m), new Rational((minutesFull - m) * 60d)];
    }
}
