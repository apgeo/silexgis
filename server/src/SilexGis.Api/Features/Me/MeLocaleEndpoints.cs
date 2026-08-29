// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Me;

/// <summary>The caller's regional settings: the language they read in, and the zone they live in.</summary>
public sealed record MeLocaleDto(string Language, string? TimeZone);

/// <summary>
/// A language tag and an IANA time zone name, sent together because the browser knows both at the
/// same moment and asking twice would mean two round trips for one choice.
/// </summary>
public sealed record MeLocaleWriteRequest(string Language, string? TimeZone);

public sealed class MeLocaleWriteRequestValidator : AbstractValidator<MeLocaleWriteRequest>
{
    public MeLocaleWriteRequestValidator()
    {
        // A language tag rather than an allow-list, so shipping a third translation needs no
        // server change. Matches the rule the profile save already applies to the same column.
        RuleFor(x => x.Language)
            .NotEmpty()
            .MaximumLength(10)
            .Matches("^[a-z]{2}(-[A-Z]{2})?$")
            .WithMessage("The language must be a language tag such as 'en' or 'ro'.");

        // Shape only, not membership of the zone database: the server's copy of that database
        // may be older than the browser's, and rejecting a zone this host has not heard of would
        // fail a language change for a reason that has nothing to do with language. The name may
        // be a single word: a browser on a machine set to UTC, and one hardened against
        // fingerprinting, both report exactly "UTC", and requiring a region prefix would fail the
        // whole save — language included — for that entirely ordinary population.
        RuleFor(x => x.TimeZone)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9+_-]+(/[A-Za-z0-9+_-]+){0,2}$")
            .WithMessage("The time zone must be an IANA zone name such as 'Europe/Bucharest' or 'UTC'.")
            .When(x => !string.IsNullOrEmpty(x.TimeZone));
    }
}

/// <summary>
/// Where the browser tells the server which language to write to this person, and where it lives.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the profile save, which is a full-DTO last-write-wins replace: a language switch
/// happens in the application shell, far from any open profile form, and routing it through that
/// save would let a stale form overwrite it — or overwrite the form with stale fields. This
/// endpoint touches nothing but the regional settings.
/// </para>
/// <para>
/// The language is what every message this account receives is written in. Until something wrote
/// it, every account was English and the Romanian wording of every template was unreachable.
/// </para>
/// <para>
/// The time zone is stored beside it because this is the only moment the browser volunteers one,
/// and rules about a person's own day — the hours a message may not interrupt them, above all —
/// are wrong by an hour for half the year without it. It stays optional: an account whose browser
/// will not say keeps nothing, and whatever reads the zone has to have an answer for nothing.
/// </para>
/// </remarks>
public static class MeLocaleEndpoints
{
    public static RouteGroupBuilder MapMeLocaleEndpoints(this RouteGroupBuilder api)
    {
        var locale = api.MapGroup("/me/locale").WithTags("Me");

        locale.MapGet("/", GetAsync)
            .WithSummary("The language and time zone stored for the caller.");
        locale.MapPut("/", UpdateAsync)
            .WithValidation<MeLocaleWriteRequest>()
            .WithSummary("Stores the language the caller reads in and the time zone they read it in. Sending no zone leaves the stored one alone rather than clearing it.");

        return api;
    }

    private static async Task<Results<Ok<MeLocaleDto>, UnauthorizedHttpResult>> GetAsync(
        IUserContextAccessor userAccessor, SilexGisDbContext db, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The context accessor answering does not prove the row is still there: an account
        // deleted between the two reads must give the ordinary refusal, not an unhandled throw.
        var stored = await db.Users.AsNoTracking()
            .Where(u => u.Id == user.UserId)
            .Select(u => new { u.Locale, u.TimeZone })
            .FirstOrDefaultAsync(ct);

        return stored is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeLocaleDto(stored.Locale, stored.TimeZone));
    }

    private static async Task<Results<Ok<MeLocaleDto>, UnauthorizedHttpResult>> UpdateAsync(
        MeLocaleWriteRequest request,
        IUserContextAccessor userAccessor,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var account = await db.Users.FirstOrDefaultAsync(u => u.Id == user.UserId, ct);
        if (account is null)
        {
            return TypedResults.Unauthorized();
        }

        account.Locale = request.Language;

        // A caller that sends no zone is a browser that would not name one, not somebody asking
        // to forget theirs. Overwriting a known zone with nothing on every language change would
        // make the column empty for anyone whose browser goes quiet about it once.
        if (!string.IsNullOrWhiteSpace(request.TimeZone))
        {
            account.TimeZone = request.TimeZone;
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new MeLocaleDto(account.Locale, account.TimeZone));
    }
}
