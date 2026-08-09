// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Domain.Settings;

namespace SilexGis.Api.Features.Me;

/// <summary>
/// The installation's starting interface arrangement, for somebody who has not made their own.
/// </summary>
public sealed record UiDefaultsDto(JsonElement Panel);

/// <summary>
/// What a screen looks like before anybody has arranged it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately its own route rather than a key inside the caller's preferences. That document is
/// the caller's, opaque and replaced wholesale by whoever writes it; folding an installation-wide
/// value into it would mean the server owning a key inside a document it promises not to read, and
/// the first client that saved its own subtree would delete the installation's.
/// </para>
/// <para>
/// These are defaults and only defaults. They fill in what a person has not chosen and never
/// overwrite what they have — the merge happens in the client, per key, so an administrator
/// publishing a section order does not thereby reset a width somebody set.
/// </para>
/// </remarks>
public static class UiDefaultsEndpoints
{
    public static RouteGroupBuilder MapUiDefaultsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/ui-defaults", GetAsync)
            .WithTags("Me")
            .WithSummary("The installation's starting interface arrangement; a default, never a policy.");

        return api;
    }

    private static async Task<Ok<UiDefaultsDto>> GetAsync(
        IAppSettingsService settings, CancellationToken ct)
    {
        var stored = await settings.GetInterfaceAsync(ct);
        return TypedResults.Ok(new UiDefaultsDto(Parse(stored.PanelDefaults)));
    }

    /// <summary>
    /// The stored document, or an empty one when it is missing or unreadable. An installation
    /// whose default cannot be parsed has published nothing — which is a working screen with the
    /// built-in arrangement, rather than a failed request on the way to every page.
    /// </summary>
    private static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(json);
            return parsed.ValueKind == JsonValueKind.Object
                ? parsed
                : JsonSerializer.Deserialize<JsonElement>("{}");
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }
}
