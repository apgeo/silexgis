// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Encodings.Web;
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

    /// <summary>
    /// The written form the limit is counted in: compact, with only the escapes JSON itself needs
    /// (quotes, backslashes, control characters). The serializer's default also writes every
    /// character outside ASCII, and characters such as &lt; and +, as a six-character \u escape,
    /// which counted a Romanian layout name six times over and refused it while the browser — which
    /// counts what <c>JSON.stringify</c> writes — saw it well under the limit. Whether the sender
    /// escaped a character is not the size of what is stored either, so a request's own text is
    /// not counted as sent. Safe here because the text goes to a jsonb column and back out as
    /// JSON, never into a page.
    /// </summary>
    private static readonly JsonSerializerOptions Counted = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The length a preferences document is held to the limit by.</summary>
    public static int LengthOf(JsonNode? document) => document?.ToJsonString(Counted).Length ?? 0;

    /// <summary>The same count for a request's document, read without building it as an object.</summary>
    public static int LengthOf(JsonElement document) => JsonSerializer.Serialize(document, Counted).Length;

    /// <summary>The document as it is stored: the counted form, so what was measured is what is kept.</summary>
    public static string Written(JsonNode document) => document.ToJsonString(Counted);

    public UiPreferencesWriteRequestValidator()
    {
        RuleFor(x => x.Preferences)
            .Must(p => p.ValueKind == JsonValueKind.Object)
            .WithMessage("Preferences must be a JSON object.");
        RuleFor(x => x.Preferences)
            .Must(p => LengthOf(p) <= MaxRawLength)
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
    public const string InvalidCode = "preferences.invalid";

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
            string merged;
            try
            {
                merged = UiPreferencesWriteRequestValidator.Written(
                    JsonMergePatch.Apply(JsonNode.Parse(account.UiPreferences), patch)!);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // A member named twice in one object: JSON allows it to be written, but there is
                // no saying which of the two is meant, so the request is refused rather than
                // guessed at.
                return ApiProblems.BadRequest(InvalidCode, "A member is named twice.");
            }
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
