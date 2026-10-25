// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>
/// Makes, shows, withdraws and resolves the public links to survey uploads' results.
/// </summary>
/// <remarks>
/// <para>
/// A link is made when an upload is completed, if the installation makes links, the uploader's
/// account has not turned them off, the account completing it may hand the cave to people outside
/// the installation (the right a share link of the cave takes), and the cave carries no location
/// protection. When one of those fails no link is made, and the upload records which, so the
/// status page and the device can say why. Running an upload again keeps its link: the link names
/// the version, and the version is still the same.
/// </para>
/// <para>
/// The right to share is asked when the link is made, not on every read: a right is a fact about a
/// caller, and the public page has none. Protection, and the installation's switch, are asked again
/// on every read, and either one failing answers exactly as an invented token does.
/// </para>
/// <para>
/// The address is shown again — on the status page and in the device's poll — only to those who
/// may write to the cave, who are the ones who may also withdraw it.
/// </para>
/// </remarks>
public sealed class SurveyUploadPublicLinkService(
    SilexGisDbContext db,
    FeatureProtection protection,
    IDataProtectionProvider keys,
    IOptions<SurveyUploadOptions> options,
    TimeProvider clock)
{
    private const string Purpose = "SilexGis.SurveyUploadPublicLink";

    /// <summary>Whether the installation makes and serves public links at all.</summary>
    public bool InstallationOn => options.Value.PublicLinks;

    private IDataProtector Protector => keys.CreateProtector(Purpose);

    /// <summary>
    /// At completion: adds the upload's link to the tracked context, or records on the upload why
    /// it gets none. The caller's save commits either.
    /// </summary>
    public async Task MintAtCompletionAsync(
        IAccessService access, AccessContext ctx, SurveyUpload upload, Feature cave, CancellationToken ct)
    {
        var accountOn = await db.SurveyUploadPreferences.AsNoTracking()
            .Where(p => p.UserId == upload.RequestedBy)
            .Select(p => (bool?)p.PublicLinks)
            .FirstOrDefaultAsync(ct) ?? true;
        var refusal = await RefusalAsync(access, ctx, cave, accountOn, ct);

        upload.PublicLinkRefusal = refusal;
        if (refusal is null)
        {
            Add(upload.Id, ctx.UserId);
        }
    }

    /// <summary>
    /// Why the caller may not make a link for this cave now, or null when they may. The account
    /// preference is not asked: a link made by hand is a deliberate act of its own.
    /// </summary>
    public Task<string?> RefusalForHandAsync(IAccessService access, AccessContext ctx, Feature cave, CancellationToken ct) =>
        RefusalAsync(access, ctx, cave, accountOn: true, ct);

    private async Task<string?> RefusalAsync(
        IAccessService access, AccessContext ctx, Feature cave, bool accountOn, CancellationToken ct)
    {
        var early = SurveyUploadPublicLinks.Refusal(InstallationOn, accountOn, mayShare: true, caveProtected: false);
        if (early is not null)
        {
            return early;
        }

        var mayShare = (await access.DecideAsync(ctx, AccessAction.Share, cave, ct)).Allowed;
        var caveProtected = mayShare && !await FeaturePublication.PublishableCaveAsync(db, protection, cave.Id, ct);
        return SurveyUploadPublicLinks.Refusal(InstallationOn, accountOn, mayShare, caveProtected);
    }

    /// <summary>
    /// Replaces the upload's live link with a new one and returns the new token. The old one is
    /// revoked first, by a statement of its own, so the one-live-link rule never sees two; a
    /// failure between the two leaves the upload with no link, which is the safe side.
    /// </summary>
    public async Task<(string Token, SurveyUploadPublicLink Link)> ReplaceAsync(
        SurveyUpload upload, Guid createdBy, CancellationToken ct)
    {
        await RevokeLiveAsync(upload.Id, ct);
        var (token, link) = Add(upload.Id, createdBy);
        upload.PublicLinkRefusal = null;
        await db.SaveChangesAsync(ct);
        return (token, link);
    }

    /// <summary>Withdraws the upload's live link, if it has one; the upload then says it was withdrawn.</summary>
    public async Task RevokeAsync(SurveyUpload upload, CancellationToken ct)
    {
        var revoked = await RevokeLiveAsync(upload.Id, ct);
        if (revoked > 0 || upload.PublicLinkRefusal is null)
        {
            upload.PublicLinkRefusal = SurveyUploadPublicLinks.Revoked;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The link as the status answer shows it, and why there is none when there is none. The
    /// address goes only to a caller who may write to the cave; protection and the installation's
    /// switch are asked now, so a link that would not open is never shown as if it would.
    /// </summary>
    public async Task<(SurveyUploadPublicLinkDto? Link, string? Refusal)> StateAsync(
        SurveyUpload upload, bool writable, CancellationToken ct)
    {
        var live = await db.SurveyUploadPublicLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.SurveyUploadId == upload.Id && l.RevokedAt == null, ct);
        if (live is null)
        {
            return (null, upload.PublicLinkRefusal);
        }

        if (!InstallationOn)
        {
            return (null, SurveyUploadPublicLinks.InstallationOff);
        }

        if (!await FeaturePublication.PublishableCaveAsync(db, protection, upload.CaveFeatureId, ct))
        {
            return (null, SurveyUploadPublicLinks.ProtectedCave);
        }

        return writable && Reveal(live) is { } token
            ? (new SurveyUploadPublicLinkDto(SurveyUploadLinks.PublicPage(token), live.CreatedAt), null)
            : (null, null);
    }

    /// <summary>
    /// The upload and cave a presented token opens now, or null — for a token that is malformed,
    /// unknown or revoked, an installation that has turned links off, an upload or cave that is
    /// gone, and a cave protected now. One answer for all of them.
    /// </summary>
    public async Task<(SurveyUpload Upload, Feature Cave)?> ResolveAsync(string? token, CancellationToken ct)
    {
        if (!InstallationOn || !SurveyUploadPublicLinks.IsPlausible(token))
        {
            return null;
        }

        var hash = SurveyUploadPublicLinks.Hash(token!);
        var uploadId = await db.SurveyUploadPublicLinks.AsNoTracking()
            .Where(l => l.TokenHash == hash && l.RevokedAt == null)
            .Select(l => (Guid?)l.SurveyUploadId)
            .FirstOrDefaultAsync(ct);
        var upload = uploadId is null
            ? null
            : await db.SurveyUploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uploadId, ct);
        if (upload is null || !await FeaturePublication.PublishableCaveAsync(db, protection, upload.CaveFeatureId, ct))
        {
            return null;
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == upload.CaveFeatureId && f.Kind == FeatureKind.Cave && f.DeletedAt == null, ct);
        return cave is null ? null : (upload, cave);
    }

    private (string Token, SurveyUploadPublicLink Link) Add(Guid uploadId, Guid createdBy)
    {
        var token = SurveyUploadPublicLinks.NewToken();
        var link = new SurveyUploadPublicLink
        {
            SurveyUploadId = uploadId,
            TokenHash = SurveyUploadPublicLinks.Hash(token),
            TokenProtected = Protector.Protect(token),
            CreatedBy = createdBy,
        };
        db.SurveyUploadPublicLinks.Add(link);
        return (token, link);
    }

    private Task<int> RevokeLiveAsync(Guid uploadId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.SurveyUploadPublicLinks
            .Where(l => l.SurveyUploadId == uploadId && l.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RevokedAt, now).SetProperty(l => l.UpdatedAt, now), ct);
    }

    /// <summary>
    /// The token, decrypted, or null when the keys it was encrypted with are gone — the link still
    /// opens, and making a new one is how its address is had again.
    /// </summary>
    private string? Reveal(SurveyUploadPublicLink link)
    {
        try
        {
            return Protector.Unprotect(link.TokenProtected);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
