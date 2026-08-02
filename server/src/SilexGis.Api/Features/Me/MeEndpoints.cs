// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// The account holder's own profile: read, save, and the avatar.
/// </summary>
/// <remarks>
/// Everything here is scoped to the caller by construction — there is no id in any route — so
/// no permission check applies beyond being signed in. What other people may see of this
/// profile is decided elsewhere, by the visibility settings saved here.
/// </remarks>
public static class MeEndpoints
{
    /// <summary>Big enough for a photo straight off a phone, small enough not to be a file store.</summary>
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private static readonly string[] AllowedAvatarExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me");

        me.MapGet("/", GetAsync)
            .WithSummary("Profile, settings and addresses of the authenticated caller.");
        me.MapPut("/", UpdateAsync)
            .WithValidation<MeUpdateRequest>()
            .WithSummary("Saves the caller's profile fields and per-field visibility choices.");

        me.MapPost("/avatar", UploadAvatarAsync)
            .DisableAntiforgery() // bearer-token API; no cookie-form surface to forge
            .WithSummary("Uploads an avatar image for the caller, replacing any previous one.");
        me.MapPut("/avatar", SetAvatarPresetAsync)
            .WithValidation<AvatarPresetRequest>()
            .WithSummary("Picks one of the built-in avatars for the caller.");
        me.MapDelete("/avatar", RemoveAvatarAsync)
            .WithSummary("Removes the caller's avatar, uploaded or built-in.");

        api.MapGet("/avatar-presets", PresetsAsync)
            .WithTags("Me")
            .WithSummary("Ids of the built-in avatars a user may choose.");

        return api;
    }

    private static Ok<AvatarPresetsDto> PresetsAsync() =>
        TypedResults.Ok(new AvatarPresetsDto(AvatarPresets.Ids));

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(MeMapping.ToDto(
            user, await AddressesAsync(db, user.Id, ct), tokens));
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> UpdateAsync(
        MeUpdateRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // A full-DTO save: an omitted field clears it.
        user.FirstName = Trimmed(request.FirstName);
        user.LastName = Trimmed(request.LastName);
        user.DisplayName = Trimmed(request.DisplayName);
        user.Bio = Trimmed(request.Bio);
        user.PhoneNumber = Trimmed(request.PhoneNumber);
        user.CavingClubId = request.CavingClubId;
        user.Locale = request.Locale;

        user.RealNameVisibility = request.Visibility.RealName;
        user.BioVisibility = request.Visibility.Bio;
        user.EmailVisibility = request.Visibility.Email;
        user.PhoneVisibility = request.Visibility.Phone;
        user.CavingClubVisibility = request.Visibility.CavingClub;
        user.AddressVisibility = request.Visibility.Address;
        user.AddressPointVisibility = request.Visibility.AddressPoint;

        // The user is tracked by this same scoped context, so one save persists the changes and
        // lets the timestamp interceptor stamp them. Never mixed with UserManager.UpdateAsync.
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(MeMapping.ToDto(
            user, await AddressesAsync(db, user.Id, ct), tokens));
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAvatarAsync(
        IFormFile file,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Deliberately no editor check: managing your own profile is not authoring content, so a
        // Viewer may set an avatar even though they may not upload files generally.
        if (file.Length == 0 || file.Length > MaxAvatarBytes)
        {
            return ApiProblems.BadRequest("me.avatar_too_large", "Pick an image under 5 MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || !AllowedAvatarExtensions.Contains(extension))
        {
            return ApiProblems.BadRequest("me.avatar_invalid", "Pick a PNG, JPEG, WebP or GIF image.");
        }

        var previousId = user.AvatarFileId;
        var (storagePath, sha256, mimeType) = await SaveAvatarAsync(file, fileStore, ct);
        var stored = documents.Create(
            new StoredContent(
                storagePath,
                Path.GetFileName(file.FileName),
                mimeType,
                file.Length,
                sha256,
                FileKind.Image,
                // Never read the EXIF capture point of an avatar: for a selfie that point is
                // the user's home, and stored photo points are published on the map.
                Geom: null),
            Path.GetFileName(file.FileName),
            user.Id,
            user.Id).File;

        user.AvatarFileId = stored.Id;
        user.AvatarPreset = null;

        // The replaced avatar is nobody's document any more — it goes whole.
        var removable = await RemovableAvatarFileAsync(db, previousId, ct);
        IReadOnlyList<StoredFile> purged = removable is null
            ? []
            : await documents.DeleteDocumentOfFileAsync(removable.Id, ct);

        await db.SaveChangesAsync(ct);

        foreach (var removed in purged)
        {
            await fileStore.DeleteAsync(removed.StoragePath, ct);
        }

        return TypedResults.Ok(MeMapping.ToDto(
            user, await AddressesAsync(db, user.Id, ct), tokens));
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> SetAvatarPresetAsync(
        AvatarPresetRequest request,
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        await ClearUploadedAvatarAsync(db, documents, fileStore, user, ct);
        user.AvatarPreset = request.Preset;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(MeMapping.ToDto(
            user, await AddressesAsync(db, user.Id, ct), tokens));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> RemoveAvatarAsync(
        ClaimsPrincipal principal,
        UserManager<SilexGisUser> userManager,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        await ClearUploadedAvatarAsync(db, documents, fileStore, user, ct);
        user.AvatarPreset = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    internal static async Task<List<UserAddress>> AddressesAsync(
        SilexGisDbContext db, Guid userId, CancellationToken ct) =>
        await db.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Drops the user's uploaded avatar and deletes its blob. Saves, because the file row must be
    /// gone before its content is: a missing blob behind a live row would 500 on every read.
    /// </summary>
    private static async Task ClearUploadedAvatarAsync(
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        SilexGisUser user,
        CancellationToken ct)
    {
        var removable = await RemovableAvatarFileAsync(db, user.AvatarFileId, ct);
        user.AvatarFileId = null;
        if (removable is null)
        {
            return;
        }

        var purged = await documents.DeleteDocumentOfFileAsync(removable.Id, ct);
        await db.SaveChangesAsync(ct);
        foreach (var removed in purged)
        {
            await fileStore.DeleteAsync(removed.StoragePath, ct);
        }
    }

    /// <summary>
    /// The previous avatar's file row, when it is safe to delete. An avatar that someone has
    /// since attached to an entity belongs to that entity now and is left alone.
    /// </summary>
    private static async Task<StoredFile?> RemovableAvatarFileAsync(
        SilexGisDbContext db, Guid? fileId, CancellationToken ct)
    {
        if (fileId is null)
        {
            return null;
        }

        if (await db.Attachments.AsNoTracking().AnyAsync(a => a.FileId == fileId.Value, ct))
        {
            return null;
        }

        return await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == fileId.Value, ct);
    }

    /// <summary>
    /// Writes the upload and reports its digest and type. Deliberately a private copy of the
    /// file slice's own helper rather than a shared one: byte-shovelling is trivial code, and
    /// slices do not reach into each other's internals.
    /// </summary>
    private static async Task<(string StoragePath, string Sha256, string MimeType)> SaveAvatarAsync(
        IFormFile file, IFileStore fileStore, CancellationToken ct)
    {
        await using var source = file.OpenReadStream();
        var storagePath = await fileStore.SaveAsync(source, Path.GetExtension(file.FileName), ct);

        await using var written = await fileStore.OpenReadAsync(storagePath, ct);
        var hash = await SHA256.HashDataAsync(written, ct);

        var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        return (storagePath, Convert.ToHexStringLower(hash), mimeType);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
