// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record UiPreferencesDto(JsonElement Preferences);

/// <summary>
/// The members to change: each named member replaces the stored one (an object member is merged
/// member by member), a null removes it, and every member not named is kept.
/// </summary>
public sealed record UiPreferencesWriteRequest(JsonElement Preferences);

public sealed class UiPreferencesWriteRequestValidator : AbstractValidator<UiPreferencesWriteRequest>
{
    /// <summary>Room for the appearance settings many times over, but not for using this as storage.</summary>
    public const int MaxRawLength = 8000;

    public UiPreferencesWriteRequestValidator()
    {
        RuleFor(x => x.Preferences)
            .Must(p => p.ValueKind == JsonValueKind.Object)
            .WithMessage("Preferences must be a JSON object.");
        RuleFor(x => x.Preferences)
            .Must(p => p.GetRawText().Length <= MaxRawLength)
            .WithMessage("Preferences are too large.")
            .When(x => x.Preferences.ValueKind == JsonValueKind.Object);
    }
}

/// <summary>
/// The caller's interface preferences — appearance, density, reduced motion — so a choice made on
/// one machine follows them to the next.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately an opaque JSON object: the client owns the key schema and the server never reads
/// or branches on its contents. This is the one genuinely open-ended part of the settings area,
/// which is why it is stored the way a saved map view's configuration is rather than as columns.
/// Do not add server-side meaning to any key here — anything the server must understand belongs
/// in a typed column instead.
/// </para>
/// <para>
/// The client also keeps a local copy, because the interface has to be painted before any request
/// completes. This endpoint is what makes the choice travel; the local copy is what makes it
/// instant.
/// </para>
/// <para>
/// A write merges rather than replaces, so a page writes only the members it owns and two
/// independent writers cannot destroy each other's settings — which a whole-document write did
/// the moment a second writer existed.
/// </para>
/// </remarks>
public static class MePreferenceEndpoints
{
    public static RouteGroupBuilder MapMePreferenceEndpoints(this RouteGroupBuilder api)
    {
        var preferences = api.MapGroup("/me/preferences").WithTags("Me");

        preferences.MapGet("/", GetAsync)
            .WithSummary("The caller's stored interface preferences.");
        preferences.MapPatch("/", MergeAsync)
            .WithValidation<UiPreferencesWriteRequest>()
            .WithSummary(
                "Merges members into the caller's stored interface preferences (JSON merge patch: a "
                + "null removes a member); members not named are kept. 400 preferences.too_large "
                + "when the merged document would exceed the limit.");

        return api;
    }

    private static async Task<Results<Ok<UiPreferencesDto>, UnauthorizedHttpResult>> GetAsync(
        IUserContextAccessor userAccessor, SilexGisDbContext db, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var stored = await db.Users.AsNoTracking()
            .Where(u => u.Id == user.UserId)
            .Select(u => u.UiPreferences)
            .FirstAsync(ct);

        return TypedResults.Ok(new UiPreferencesDto(JsonSerializer.Deserialize<JsonElement>(stored)));
    }

    public const string TooLargeCode = "preferences.too_large";

    /// <summary>How often a merge is retried when another write to the same account landed first.</summary>
    private const int MergeAttempts = 3;

    private static async Task<Results<Ok<UiPreferencesDto>, UnauthorizedHttpResult, ProblemHttpResult>> MergeAsync(
        UiPreferencesWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var patch = JsonNode.Parse(request.Preferences.GetRawText());
        for (var attempt = 1; ; attempt++)
        {
            var account = await db.Users.FirstAsync(u => u.Id == user.UserId, ct);
            var merged = JsonMergePatch.Apply(JsonNode.Parse(account.UiPreferences), patch)!.ToJsonString();
            if (merged.Length > UiPreferencesWriteRequestValidator.MaxRawLength)
            {
                return ApiProblems.BadRequest(TooLargeCode, "Preferences are too large.");
            }

            account.UiPreferences = merged;
            // The account row's concurrency stamp is what makes two merges at once safe: each
            // write moves it, so the later of two writes that read the same document is refused
            // and merges again over what the earlier one stored, instead of writing over it.
            account.ConcurrencyStamp = Guid.NewGuid().ToString();
            try
            {
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new UiPreferencesDto(JsonSerializer.Deserialize<JsonElement>(merged)));
            }
            catch (DbUpdateConcurrencyException) when (attempt < MergeAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
