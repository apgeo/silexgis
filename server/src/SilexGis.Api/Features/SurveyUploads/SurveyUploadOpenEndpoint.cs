// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

public static partial class SurveyUploadEndpoints
{
    private static async Task<Results<Created<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> OpenAsync(
        SurveyUploadOpenRequest request,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        IOptions<SurveyUploadOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var settings = options.Value;
        if (!Sources.Contains(request.Source.Trim().ToLowerInvariant()))
        {
            return ApiProblems.BadRequest(
                SourceUnsupportedCode, $"Accepted survey tools: {string.Join(", ", Sources)}.");
        }

        var named = (request.Cave is null ? 0 : 1) + (request.CaveSurvey is null ? 0 : 1) + (request.NewCave is null ? 0 : 1);
        if (named > 1)
        {
            return ApiProblems.BadRequest(
                CaveAmbiguousCode, "Name the cave once: an existing cave, an existing survey, or a new cave.");
        }

        var claims = request.Files.Select(f => new SurveyUploadFileClaim(f.Name, f.Size, f.Role)).ToList();
        if (SurveyUploadFiles.Problem(claims, settings.FileCap, settings.FileCountCap) is { } fileProblem)
        {
            return ApiProblems.BadRequest(SurveyUploadFiles.InvalidCode, fileProblem.Detail, "file", fileProblem.File);
        }

        var run = await RunOptionsAsync(db, settings, ctx.UserId, request.Pipeline, ct);
        if (run.Problem is { } runProblem)
        {
            return runProblem;
        }

        var title = SurveyCaveNaming.TitleOf(request.SurveyTitle, request.SurveyName)!;
        var surveyKey = CaveSurveyMatching.KeyOf(request.SurveyName);
        const SurveyOrigin origin = SurveyOrigin.TopoDroid;

        Feature cave;
        CaveSurvey? survey = null;
        var createdCave = false;

        if (request.CaveSurvey is { } surveyRef)
        {
            survey = await db.CaveSurveys.FirstOrDefaultAsync(s => s.Id == surveyRef.Id, ct);
            var surveyCave = survey is null ? null : await CaveAsync(db, survey.CaveFeatureId, ct);
            if (survey is null || surveyCave is null
                || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, surveyCave, ct))
            {
                return ApiProblems.NotFound(SurveyNotFoundCode);
            }

            if (!await SurveyModelAccess.WritableAsync(access, ctx, surveyCave, ct))
            {
                return ApiProblems.Forbidden();
            }

            cave = surveyCave;
        }
        else if (request.Cave is { } caveRef)
        {
            var existing = await CaveAsync(db, caveRef.Id, ct);
            if (existing is null || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, existing, ct))
            {
                return ApiProblems.NotFound(CaveNotFoundCode);
            }

            if (!await SurveyModelAccess.WritableAsync(access, ctx, existing, ct))
            {
                return ApiProblems.Forbidden();
            }

            cave = existing;
            var keys = await db.CaveSurveys.AsNoTracking()
                .Where(s => s.CaveFeatureId == cave.Id)
                .Select(s => new CaveSurveyKey(s.Id, s.Source, s.ExternalKey))
                .ToListAsync(ct);
            var match = CaveSurveyMatching.Match(null, origin, request.SurveyName, keys);
            if (match is { Kind: CaveSurveyMatchKind.ByExternalKey, SurveyId: { } matched })
            {
                survey = await db.CaveSurveys.FirstAsync(s => s.Id == matched, ct);
            }
        }
        else
        {
            if (!settings.CaveCreation)
            {
                return request.NewCave is null
                    ? ApiProblems.BadRequest(CaveRequiredCode, "This server makes no caves from uploads; name the cave.")
                    : ApiProblems.Forbidden(CaveCreationDisabledCode, "This server makes no caves from uploads; name the cave.");
            }

            var made = await CreateCaveAsync(db, writer, ctx, user, settings, request.NewCave, title, ct);
            if (made.Problem is { } caveProblem)
            {
                return caveProblem;
            }

            cave = made.Cave!;
            createdCave = true;
        }

        // The exports with no survey format become documents of the cave; whether the uploader
        // may make them, and on what terms, is known now and is better said before any byte is sent.
        if (request.Files.Any(f => SurveySourceFormats.ClaimedKind(f.Name) is null)
            && DocumentAudience(ctx, cave, request.DocumentsVisibility).RefusalCode is { } documentsRefused)
        {
            return DocumentAudienceProblem(documentsRefused);
        }

        var now = clock.GetUtcNow();
        DateTimeOffset? latest = null;
        if (survey is null)
        {
            survey = new CaveSurvey
            {
                CaveFeatureId = cave.Id,
                Title = title,
                Source = origin,
                ExternalKey = surveyKey,
                Description = request.Description?.Trim() is { Length: > 0 } note ? note : null,
            };
            db.CaveSurveys.Add(survey);
        }
        else
        {
            latest = await db.SurveyUploads.AsNoTracking()
                .Where(u => u.CaveSurveyId == survey.Id)
                .MaxAsync(u => (DateTimeOffset?)u.VersionAt, ct);
        }

        var batch = new UploadBatch
        {
            Source = UploadSource.SurveyUpload,
            Status = UploadBatchStatus.Open,
            StartedByUserId = ctx.UserId,
            Label = title.Length > 200 ? title[..200] : title,
            SourceDescription = ClientDescription(request.Client),
        };
        db.UploadBatches.Add(batch);

        var upload = new SurveyUpload
        {
            CaveFeatureId = cave.Id,
            CaveSurveyId = survey.Id,
            UploadBatchId = batch.Id,
            Source = origin,
            ClientName = Trimmed(request.Client?.Name),
            ClientVersion = Trimmed(request.Client?.Version),
            SurveyTitle = title,
            SurveyName = surveyKey,
            Description = Trimmed(request.Description),
            VersionAt = SurveyVersionStamp.Next(now, latest),
            CreatedCave = createdCave,
            DocumentsVisibility = request.DocumentsVisibility,
            PublicLinkWanted = request.PublicLink?.Wanted,
            PublicLinkAudience = request.PublicLink?.Audience,
            PublicLinkAllowProtected = request.PublicLink?.AllowProtected ?? false,
            Options = JsonSerializer.Serialize(run.Options, JsonSerializerOptions.Web),
            RequestedBy = ctx.UserId,
            ExpiresAt = now + settings.UploadSession,
        };
        db.SurveyUploads.Add(upload);

        foreach (var file in request.Files)
        {
            db.SurveyUploadFiles.Add(new SurveyUploadFile
            {
                SurveyUploadId = upload.Id,
                Name = file.Name,
                Role = file.Role,
                DeclaredSize = file.Size,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another upload of the same new survey, or of the same survey in the same second,
            // landed first. Nothing of this one was saved; sent again, it finds what the other
            // made and becomes the next version of it.
            return ApiProblems.Conflict(BusyCode, "Another upload of this survey arrived at the same moment; send it again.");
        }

        var dto = await ToDtoAsync(db, tokens, links, options.Value, upload, cave, writable: true, ct);
        return TypedResults.Created($"/api/v1/survey-uploads/{upload.Id}", dto);
    }

    private sealed record MadeCave(Feature? Cave, ProblemHttpResult? Problem);

    /// <summary>
    /// Makes the cave an upload goes to when it names none: on the uploader's ordinary right to
    /// create caves, bound and opened as the request (or the uploader's club) says, named after
    /// the survey. Tracked, not saved — it lands in the same save as the upload, or not at all.
    /// </summary>
    private static async Task<MadeCave> CreateCaveAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        AccessContext ctx,
        UserContext user,
        SurveyUploadOptions settings,
        SurveyUploadNewCave? asked,
        string title,
        CancellationToken ct)
    {
        var profile = await ProfileDirectory.ResolveOneAsync(db, user, user.UserId, ct);
        var audience = SurveyCaveVisibility.Resolve(
            asked?.Visibility, asked?.CavingGroupId, user.CavingGroupIds, profile?.CavingClubId);
        if (audience.RefusalCode is { } refused)
        {
            return new MadeCave(null, ApiProblems.BadRequest(
                refused, "Name a caving group the account belongs to, or choose another visibility."));
        }

        if (audience.CavingGroupId is { } groupId
            && !(ctx.IsFullAdmin
                ? await db.CavingGroups.AnyAsync(g => g.Id == groupId, ct)
                : CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, groupId)))
        {
            return new MadeCave(null, ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode));
        }

        var facts = await CreateContext.ParentCreateFactsAsync(
            db, ctx, null, audience.CavingGroupId, FeatureKind.Cave, ct);
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, facts
            ?? AccessTargetFacts.ForCreate(null, [], audience.CavingGroupId, FeatureKind.Cave)))
        {
            return new MadeCave(null, ApiProblems.Forbidden(CreateRules.ForbiddenCode));
        }

        long caveTypeId;
        if (asked?.CaveTypeId is { } chosen)
        {
            if (!await db.CaveTypes.AnyAsync(t => t.Id == chosen, ct))
            {
                return new MadeCave(null, ApiProblems.BadRequest(CaveTypeUnknownCode, "This installation has no such cave type."));
            }

            caveTypeId = chosen;
        }
        else if (await DefaultCaveTypeIdAsync(db, settings, ct) is { } fallback)
        {
            caveTypeId = fallback;
        }
        else
        {
            return new MadeCave(null, ApiProblems.BadRequest(CaveTypeUnknownCode, "This installation has no cave types."));
        }

        // A name the caver typed is taken as written. A name made from the survey's title gets a
        // number when a cave the uploader can see already has it — compared among those caves
        // only, so the number never reveals a cave kept from them.
        var name = Trimmed(asked?.Name) ?? await UnclaimedNameAsync(db, ctx, title, ct);

        var feature = new Feature
        {
            OwnerUserId = ctx.UserId,
            Name = name,
            Visibility = audience.Visibility,
            CavingGroupId = audience.CavingGroupId,
            // Safe to set directly on create, as the web's form does: the write service works out
            // the cave's effective protection while it establishes the cave, so the entrance the
            // survey's fixed station places later is protected from its first second.
            LocationProtected = asked?.LocationProtected ?? false,
        };
        var cave = new Cave { CaveTypeId = caveTypeId };
        feature.Cave = cave;

        try
        {
            await writer.CreateCaveAsync(feature, cave, [], ct);
        }
        catch (FeatureWriteException ex)
        {
            return new MadeCave(null, ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors)));
        }

        return new MadeCave(feature, null);
    }

    private static async Task<string> UnclaimedNameAsync(
        SilexGisDbContext db, AccessContext ctx, string title, CancellationToken ct)
    {
        var numbered = title + " (";
        var visibleNames = await db.Features.AsNoTracking()
            .Where(f => f.Kind == FeatureKind.Cave && f.DeletedAt == null && f.Name != null
                && (f.Name == title || f.Name.StartsWith(numbered)))
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Select(f => f.Name!)
            .ToListAsync(ct);
        return SurveyCaveNaming.Unclaimed(title, visibleNames);
    }

    private static string? Trimmed(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? ClientDescription(SurveyUploadClientInfo? client)
    {
        var text = string.Join(' ', new[] { Trimmed(client?.Name), Trimmed(client?.Version) }.OfType<string>());
        return text.Length == 0 ? null : text;
    }
}
