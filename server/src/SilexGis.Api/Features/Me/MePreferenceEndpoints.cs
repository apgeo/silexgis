// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

public sealed record UiPreferencesDto(JsonElement Preferences);

public sealed record UiPreferencesWriteRequest(JsonElement Preferences);

public sealed class UiPreferencesWriteRequestValidator : AbstractValidator<UiPreferencesWriteRequest>
{
    /// <summary>Room for the appearance settings many times over, but not for using this as storage.</summary>
    private const int MaxRawLength = 8000;

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
/// </remarks>
public static class MePreferenceEndpoints
{
    public static RouteGroupBuilder MapMePreferenceEndpoints(this RouteGroupBuilder api)
    {
        var preferences = api.MapGroup("/me/preferences").WithTags("Me");

        preferences.MapGet("/", GetAsync)
            .WithSummary("The caller's stored interface preferences.");
        preferences.MapPut("/", UpdateAsync)
            .WithValidation<UiPreferencesWriteRequest>()
            .WithSummary("Replaces the caller's stored interface preferences.");

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

    private static async Task<Results<Ok<UiPreferencesDto>, UnauthorizedHttpResult>> UpdateAsync(
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

        var account = await db.Users.FirstAsync(u => u.Id == user.UserId, ct);
        account.UiPreferences = request.Preferences.GetRawText();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new UiPreferencesDto(JsonSerializer.Deserialize<JsonElement>(account.UiPreferences)));
    }
}
