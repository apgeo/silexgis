// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>The caller's own choices for the surveys they upload from a device.</summary>
/// <param name="AlwaysOutputs">
/// Outputs every upload of this account produces on top of what the upload itself asks for, by
/// their names in the output catalogue, in catalogue order.
/// </param>
/// <param name="PublicLinks">
/// Whether this account's uploads get a public link to their results. The installation must allow
/// such links too, and a location-protected cave never gets one whatever this says.
/// </param>
public sealed record SurveyUploadPreferencesDto(IReadOnlyList<string> AlwaysOutputs, bool PublicLinks);

/// <summary>Replaces the caller's survey-upload choices as a whole.</summary>
/// <param name="AlwaysOutputs">Catalogue names; case does not matter and repeats are dropped.</param>
/// <param name="PublicLinks">Whether this account's uploads get public result links.</param>
public sealed record SurveyUploadPreferencesWriteRequest(IReadOnlyList<string> AlwaysOutputs, bool PublicLinks);

public sealed class SurveyUploadPreferencesWriteRequestValidator : AbstractValidator<SurveyUploadPreferencesWriteRequest>
{
    /// <summary>A guard against a list used as storage; the catalogue itself is far shorter.</summary>
    public const int MaxListed = 64;

    public SurveyUploadPreferencesWriteRequestValidator()
    {
        RuleFor(x => x.AlwaysOutputs).NotNull();
        RuleFor(x => x.AlwaysOutputs)
            .Must(list => list.Count <= MaxListed)
            .WithMessage("Too many outputs.")
            .When(x => x.AlwaysOutputs is not null);
    }
}

/// <summary>
/// The account's own survey-upload choices: outputs every upload produces, and whether uploads
/// get public result links. An account that never saved any has the defaults — nothing added, and
/// links on.
/// </summary>
/// <remarks>
/// Held as typed columns of their own rather than in the interface-preference document, because the
/// server acts on them. Not open to a personal access key: a device sends what one upload asks for,
/// and the standing choices are the account holder's to make on the web.
/// </remarks>
public static class MeSurveyUploadPreferenceEndpoints
{
    /// <summary>An output the catalogue does not have.</summary>
    public const string OutputUnknownCode = "survey_upload_preferences.output_unknown";

    public static RouteGroupBuilder MapMeSurveyUploadPreferenceEndpoints(this RouteGroupBuilder api)
    {
        var preferences = api.MapGroup("/me/survey-upload-preferences").WithTags("Me");

        preferences.MapGet("/", GetAsync)
            .WithSummary("The caller's survey-upload choices: outputs always produced, and whether results get a public link.");
        preferences.MapPut("/", PutAsync)
            .WithValidation<SurveyUploadPreferencesWriteRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Replaces the caller's survey-upload choices. 400 survey_upload_preferences.output_unknown names an output the catalogue does not have.");

        return api;
    }

    /// <summary>The defaults an account with no stored choices has.</summary>
    public static SurveyUploadPreferencesDto Defaults { get; } = new([], true);

    private static async Task<Results<Ok<SurveyUploadPreferencesDto>, UnauthorizedHttpResult>> GetAsync(
        IUserContextAccessor userAccessor, SilexGisDbContext db, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var stored = await db.SurveyUploadPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.UserId, ct);
        return TypedResults.Ok(stored is null ? Defaults : ToDto(stored));
    }

    private static async Task<Results<Ok<SurveyUploadPreferencesDto>, UnauthorizedHttpResult, ProblemHttpResult>> PutAsync(
        SurveyUploadPreferencesWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var unknown = SurveyOutputs.Unknown(request.AlwaysOutputs);
        if (unknown.Count > 0)
        {
            return ApiProblems.BadRequest(
                OutputUnknownCode, "Not an output this server can produce: " + string.Join(", ", unknown));
        }

        var stored = await db.SurveyUploadPreferences.FirstOrDefaultAsync(p => p.UserId == user.UserId, ct);
        if (stored is null)
        {
            stored = new SurveyUploadPreference { UserId = user.UserId };
            db.SurveyUploadPreferences.Add(stored);
        }

        // Stored in catalogue form: lower-case, each once, in catalogue order.
        stored.AlwaysOutputs = [.. SurveyOutputs.Effective(request.AlwaysOutputs, [], [], [])];
        stored.PublicLinks = request.PublicLinks;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (db.Entry(stored).State == EntityState.Added)
        {
            // Two first saves at once: the other one made the row, so this one writes over it.
            db.ChangeTracker.Clear();
            var existing = await db.SurveyUploadPreferences.FirstAsync(p => p.UserId == user.UserId, ct);
            existing.AlwaysOutputs = stored.AlwaysOutputs;
            existing.PublicLinks = stored.PublicLinks;
            await db.SaveChangesAsync(ct);
            stored = existing;
        }

        return TypedResults.Ok(ToDto(stored));
    }

    private static SurveyUploadPreferencesDto ToDto(SurveyUploadPreference p) =>
        new(SurveyOutputs.Effective(p.AlwaysOutputs, [], [], []), p.PublicLinks);
}
