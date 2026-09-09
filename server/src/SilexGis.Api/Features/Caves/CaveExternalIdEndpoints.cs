// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Grottocenter;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>One identifier another register knows this cave by.</summary>
/// <param name="System">Which register, from the closed vocabulary.</param>
/// <param name="Value">The identifier, as that register writes it.</param>
/// <param name="Url">Where a person can read the entry, when the register has a page for it.</param>
public sealed record CaveExternalIdDto(string System, string Value, string? Url);

/// <summary>What to record, or what to clear.</summary>
/// <param name="Value">
/// The identifier. Empty or absent removes whatever was recorded for that register — the way
/// an identifier entered by mistake is taken back, and the reason this is one route rather
/// than a write and a separate delete.
/// </param>
public sealed record CaveExternalIdWriteRequest(string? Value = null);

public sealed class CaveExternalIdWriteRequestValidator : AbstractValidator<CaveExternalIdWriteRequest>
{
    public CaveExternalIdWriteRequestValidator() => RuleFor(r => r.Value).MaximumLength(200);
}

/// <summary>A cave the far end offered for the name it was asked about.</summary>
/// <param name="ExternalId">Its identifier there.</param>
/// <param name="Name">What it is called there.</param>
/// <param name="Country">Where it is, when the answer says.</param>
/// <param name="Url">The page to read before accepting the match.</param>
public sealed record GrottocenterCandidateDto(string ExternalId, string? Name, string? Country, string? Url);

/// <summary>Whether this installation does lookups at all, and what came back.</summary>
/// <param name="Configured">
/// False when an operator has not turned the integration on. Sent rather than implied, so a
/// screen can leave the button out instead of offering something that always fails.
/// </param>
/// <param name="Candidates">What the far end offered, best effort, never stored by asking.</param>
public sealed record GrottocenterLookupDto(bool Configured, IReadOnlyList<GrottocenterCandidateDto> Candidates);

/// <summary>
/// The numbers other registers know a cave by, and the one optional way of going to find one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recording an identifier is a write on the cave</b>, authorized against the cave feature
/// exactly as its entrances are: an external number is part of the cave's identity, and
/// somebody who may not edit the cave may not decide what it is called elsewhere either. A cave
/// the caller may not read answers "no such cave", so this route discloses nothing the cave list
/// does not.
/// </para>
/// <para>
/// <b>The lookup sends a name and nothing else.</b> Not the position — that is the one thing
/// that must not leave, and it would leave for every cave anybody ever pressed the button on,
/// including the ones whose location this installation exists to protect. And nothing that comes
/// back is kept beyond the identifier a person accepts: this application is not a mirror of
/// somebody else's register, and a copy taken at an unknown moment goes quietly stale while
/// still looking authoritative.
/// </para>
/// <para>
/// <b>The lookup is off unless an operator turned it on</b>, which is the shipped state. It is
/// the only place a signed-in person's action causes a request to a service nobody here
/// controls.
/// </para>
/// </remarks>
public static class CaveExternalIdEndpoints
{
    /// <summary>A register was named that is not one this application knows.</summary>
    public const string UnknownSystemCode = "external_id.unknown_system";

    public static RouteGroupBuilder MapCaveExternalIdEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("CaveExternalIds");

        caves.MapGet("/{caveId:guid}/external-ids", ListAsync)
            .WithSummary("The identifiers other registers know this cave by.");

        caves.MapPut("/{caveId:guid}/external-ids/{system}", WriteAsync)
            .WithValidation<CaveExternalIdWriteRequest>()
            .WithSummary(
                "Records the identifier one register knows this cave by, or clears it when the "
                + "value is empty (Write on the cave).");

        caves.MapPost("/{caveId:guid}/external-ids/grottocenter/lookup", LookupAsync)
            .WithSummary(
                "Asks grottocenter.org which of its caves match this cave's name. Sends the name "
                + "and nothing else, stores nothing, and answers that it is not configured when "
                + "the installation has not turned the integration on (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<CaveExternalIdDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        GrottocenterClient grottocenter,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await ReadableCaveAsync(db, access, ctx, caveId, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var rows = await db.FeatureExternalIds.AsNoTracking()
            .Where(x => x.FeatureId == caveId)
            .OrderBy(x => x.System)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(x => ToDto(x, grottocenter)).ToList());
    }

    private static async Task<Results<Ok<List<CaveExternalIdDto>>, ProblemHttpResult>> WriteAsync(
        Guid caveId,
        string system,
        CaveExternalIdWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        GrottocenterClient grottocenter,
        CancellationToken ct)
    {
        if (!ExternalIdSystems.TryParse(system, out var parsed))
        {
            return ApiProblems.BadRequest(
                UnknownSystemCode,
                $"'{system}' is not a register this application records identifiers for. "
                + $"Choose one of: {string.Join(", ", ExternalIdSystems.Codes)}.",
                "system",
                ExternalIdSystems.Codes.ToList());
        }

        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await ReadableCaveAsync(db, access, ctx, caveId, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var existing = await db.FeatureExternalIds
            .FirstOrDefaultAsync(x => x.FeatureId == caveId && x.System == parsed, ct);
        var value = request.Value?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            if (existing is not null)
            {
                db.FeatureExternalIds.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.FeatureExternalIds.Add(new FeatureExternalId
            {
                FeatureId = caveId,
                System = parsed,
                Value = value,
                CreatedBy = currentUser.UserId,
            });
        }
        else
        {
            existing.Value = value;
        }

        await db.SaveChangesAsync(ct);

        var rows = await db.FeatureExternalIds.AsNoTracking()
            .Where(x => x.FeatureId == caveId)
            .OrderBy(x => x.System)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(x => ToDto(x, grottocenter)).ToList());
    }

    private static async Task<Results<Ok<GrottocenterLookupDto>, ProblemHttpResult>> LookupAsync(
        Guid caveId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        GrottocenterClient grottocenter,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await ReadableCaveAsync(db, access, ctx, caveId, ct);
        if (cave is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // A write, although it writes nothing: it is the act of going to look somebody else's
        // cave up on this cave's behalf, and what it is for is recording the answer. Gating it
        // on reading would let anybody who can see the cave make this installation send its
        // name outward.
        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // Answered rather than refused, so a screen can say "this installation does not do
        // this" instead of showing a failure for a feature nobody turned on.
        if (!grottocenter.IsConfigured)
        {
            return TypedResults.Ok(new GrottocenterLookupDto(false, []));
        }

        try
        {
            var candidates = await grottocenter.SearchAsync(cave.Name ?? string.Empty, ct);
            return TypedResults.Ok(new GrottocenterLookupDto(
                true,
                candidates
                    .Select(c => new GrottocenterCandidateDto(c.ExternalId, c.Name, c.Country, c.Url))
                    .ToList()));
        }
        catch (GrottocenterException error)
        {
            return ApiProblems.BadRequest(error.Code, error.Message, "caveId", caveId.ToString());
        }
    }

    private static async Task<Feature?> ReadableCaveAsync(
        SilexGisDbContext db, IAccessService access, AccessContext? ctx, Guid caveId, CancellationToken ct)
    {
        var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
            ? cave
            : null;
    }

    private static CaveExternalIdDto ToDto(FeatureExternalId row, GrottocenterClient grottocenter) =>
        new(
            ExternalIdSystems.Code(row.System),
            row.Value,

            // Only one of the registers this application knows has a page to link to, and the
            // national cadastre's numbering is not a URL anywhere.
            row.System == ExternalIdSystem.Grottocenter ? grottocenter.EntryUrl(row.Value) : null);
}
