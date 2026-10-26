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
/// A link is made when an upload is completed, if the installation makes links, the upload or else
/// the uploader's account asks for one, the account completing it may hand the cave to people
/// outside the installation (the right a share link of the cave takes), and the cave carries no
/// location protection — or the upload acknowledged, explicitly, that it does. When one of those
/// fails no link is made, and the upload records which, so the status page and the device can say
/// why. Running an upload again keeps its link: the link names the version, and the version is
/// still the same.
/// </para>
/// <para>
/// The right to share is asked when the link is made, not on every read: a right is a fact about a
/// caller, and the public page has none. Protection, and the installation's switch, are asked again
/// on every read, and either one failing answers exactly as an invented token does — except that a
/// link made with the acknowledgement that the cave is protected keeps opening while it stays so.
/// </para>
/// <para>
/// A link opens either for anybody holding its address or only for a signed-in account of this
/// installation; which is chosen when it is made. The address is shown again — on the status page,
/// in the listing and in the device's poll — to everybody who may read the upload.
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
    /// it gets none. The caller's save commits either. An acknowledgement that the cave is protected
    /// is the uploader's, given when the upload was opened, and is recorded as such.
    /// </summary>
    public async Task MintAtCompletionAsync(
        IAccessService access, AccessContext ctx, SurveyUpload upload, Feature cave, CancellationToken ct)
    {
        var preference = await db.SurveyUploadPreferences.AsNoTracking()
            .Where(p => p.UserId == upload.RequestedBy)
            .Select(p => new { p.PublicLinks, p.DefaultLinkAudience })
            .FirstOrDefaultAsync(ct);
        var accountOn = preference?.PublicLinks ?? true;
        var accountAudience = preference?.DefaultLinkAudience ?? SurveyUploadLinkAudience.Public;

        var (mayShare, caveProtected) = InstallationOn && (upload.PublicLinkWanted ?? accountOn)
            ? await ShareAndProtectionAsync(access, ctx, cave, ct)
            : (true, false);
        var decision = SurveyUploadPublicLinks.AtCompletion(
            InstallationOn,
            accountOn,
            accountAudience,
            upload.PublicLinkWanted,
            upload.PublicLinkAudience,
            upload.PublicLinkAllowProtected,
            mayShare,
            caveProtected);

        upload.PublicLinkRefusal = decision.Refusal;
        if (decision.Refusal is null)
        {
            Add(upload.Id, ctx.UserId, decision.Audience,
                decision.ProtectedAcknowledged ? (upload.RequestedBy, upload.CreatedAt) : null);
        }
    }

    /// <summary>
    /// Why the caller may not make a link for this cave now, or null when they may, and whether the
    /// cave is protected (so a link made over an acknowledgement records it). The account preference
    /// is not asked: a link made by hand is a deliberate act of its own.
    /// </summary>
    public async Task<(string? Refusal, bool CaveProtected)> RefusalForHandAsync(
        IAccessService access, AccessContext ctx, Feature cave, bool allowProtected, CancellationToken ct)
    {
        if (!InstallationOn)
        {
            return (SurveyUploadPublicLinks.InstallationOff, false);
        }

        var (mayShare, caveProtected) = await ShareAndProtectionAsync(access, ctx, cave, ct);
        return (SurveyUploadPublicLinks.Refusal(InstallationOn, true, mayShare, caveProtected, allowProtected), caveProtected);
    }

    /// <summary>
    /// The audience a link made by hand opens for when the request does not say: the one the link it
    /// replaces had, else the one the upload asked for, else the caller's default.
    /// </summary>
    public async Task<SurveyUploadLinkAudience> AudienceForHandAsync(SurveyUpload upload, Guid callerId, CancellationToken ct)
    {
        var live = await db.SurveyUploadPublicLinks.AsNoTracking()
            .Where(l => l.SurveyUploadId == upload.Id && l.RevokedAt == null)
            .Select(l => (SurveyUploadLinkAudience?)l.Audience)
            .FirstOrDefaultAsync(ct);
        return live
            ?? upload.PublicLinkAudience
            ?? await db.SurveyUploadPreferences.AsNoTracking()
                .Where(p => p.UserId == callerId)
                .Select(p => (SurveyUploadLinkAudience?)p.DefaultLinkAudience)
                .FirstOrDefaultAsync(ct)
            ?? SurveyUploadLinkAudience.Public;
    }

    private async Task<(bool MayShare, bool CaveProtected)> ShareAndProtectionAsync(
        IAccessService access, AccessContext ctx, Feature cave, CancellationToken ct)
    {
        var mayShare = (await access.DecideAsync(ctx, AccessAction.Share, cave, ct)).Allowed;
        var caveProtected = mayShare && !await FeaturePublication.PublishableCaveAsync(db, protection, cave.Id, ct);
        return (mayShare, caveProtected);
    }

    /// <summary>
    /// Replaces the upload's live link with a new one and returns the new token. The old one is
    /// revoked first, by a statement of its own, so the one-live-link rule never sees two; a
    /// failure between the two leaves the upload with no link, which is the safe side. A link made
    /// over a protected cave records who acknowledged it, and when.
    /// </summary>
    public async Task<(string Token, SurveyUploadPublicLink Link)> ReplaceAsync(
        SurveyUpload upload, Guid createdBy, SurveyUploadLinkAudience audience, bool protectedAcknowledged, CancellationToken ct)
    {
        await RevokeLiveAsync(upload.Id, ct);
        var (token, link) = Add(upload.Id, createdBy, audience,
            protectedAcknowledged ? (createdBy, clock.GetUtcNow()) : null);
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
    /// The link as the status answer shows it, and why there is none when there is none.
    /// Protection and the installation's switch are asked now, so a link that would not open is
    /// never shown as if it would. <paramref name="showAddress"/> false keeps the address back from
    /// a caller who may follow the upload but not see where the cave is.
    /// </summary>
    public async Task<(SurveyUploadPublicLinkDto? Link, string? Refusal)> StateAsync(
        SurveyUpload upload, bool showAddress, CancellationToken ct)
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

        var caveProtected = !await FeaturePublication.PublishableCaveAsync(db, protection, upload.CaveFeatureId, ct);
        if (!SurveyUploadPublicLinks.Opens(caveProtected, live.ProtectedAcknowledged))
        {
            return (null, SurveyUploadPublicLinks.ProtectedCave);
        }

        return showAddress && ToDto(live) is { } dto ? (dto, null) : (null, null);
    }

    /// <summary>
    /// The links that open now for each of the given uploads, for a listing whose rows the caller
    /// may read; an upload with none is absent.
    /// </summary>
    public async Task<Dictionary<Guid, SurveyUploadPublicLinkDto>> OpenLinksAsync(
        IReadOnlyCollection<(Guid UploadId, Guid CaveId)> uploads, CancellationToken ct)
    {
        if (!InstallationOn || uploads.Count == 0)
        {
            return [];
        }

        var ids = uploads.Select(u => u.UploadId).ToList();
        var live = await db.SurveyUploadPublicLinks.AsNoTracking()
            .Where(l => ids.Contains(l.SurveyUploadId) && l.RevokedAt == null)
            .ToListAsync(ct);
        if (live.Count == 0)
        {
            return [];
        }

        var caveOf = uploads.ToDictionary(u => u.UploadId, u => u.CaveId);
        var publishable = await FeaturePublication.PublishableCaveIdsAsync(
            db, protection, [.. live.Select(l => caveOf[l.SurveyUploadId]).Distinct()], ct);
        var open = new Dictionary<Guid, SurveyUploadPublicLinkDto>();
        foreach (var link in live)
        {
            if (SurveyUploadPublicLinks.Opens(!publishable.Contains(caveOf[link.SurveyUploadId]), link.ProtectedAcknowledged)
                && ToDto(link) is { } dto)
            {
                open[link.SurveyUploadId] = dto;
            }
        }

        return open;
    }

    /// <summary>
    /// The upload and cave a presented token opens now, and who it opens for — or null for a token
    /// that is malformed, unknown or revoked, an installation that has turned links off, an upload
    /// or cave that is gone, and a cave protected now without the link having been made knowing it.
    /// One answer for all of them; the audience is the caller's to apply, after this.
    /// </summary>
    public async Task<(SurveyUpload Upload, Feature Cave, SurveyUploadLinkAudience Audience)?> ResolveAsync(
        string? token, CancellationToken ct)
    {
        if (!InstallationOn || !SurveyUploadPublicLinks.IsPlausible(token))
        {
            return null;
        }

        var hash = SurveyUploadPublicLinks.Hash(token!);
        var link = await db.SurveyUploadPublicLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == hash && l.RevokedAt == null, ct);
        var upload = link is null
            ? null
            : await db.SurveyUploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == link.SurveyUploadId, ct);
        if (link is null || upload is null)
        {
            return null;
        }

        var caveProtected = !await FeaturePublication.PublishableCaveAsync(db, protection, upload.CaveFeatureId, ct);
        if (!SurveyUploadPublicLinks.Opens(caveProtected, link.ProtectedAcknowledged))
        {
            return null;
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == upload.CaveFeatureId && f.Kind == FeatureKind.Cave && f.DeletedAt == null, ct);
        return cave is null ? null : (upload, cave, link.Audience);
    }

    /// <summary>The link as answered to a reader, or null when its address cannot be had again.</summary>
    private SurveyUploadPublicLinkDto? ToDto(SurveyUploadPublicLink link) =>
        Reveal(link) is { } token
            ? new SurveyUploadPublicLinkDto(SurveyUploadLinks.PublicPage(token), link.CreatedAt, link.Audience, link.ProtectedAcknowledged)
            : null;

    private (string Token, SurveyUploadPublicLink Link) Add(
        Guid uploadId, Guid createdBy, SurveyUploadLinkAudience audience, (Guid By, DateTimeOffset At)? acknowledgement)
    {
        var token = SurveyUploadPublicLinks.NewToken();
        var link = new SurveyUploadPublicLink
        {
            SurveyUploadId = uploadId,
            TokenHash = SurveyUploadPublicLinks.Hash(token),
            TokenProtected = Protector.Protect(token),
            CreatedBy = createdBy,
            Audience = audience,
            ProtectedAcknowledgedBy = acknowledgement?.By,
            ProtectedAcknowledgedAt = acknowledgement?.At,
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
