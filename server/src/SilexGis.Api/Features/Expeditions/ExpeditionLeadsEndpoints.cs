// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// What a camp left open: the places its trips found a way on at, gathered by whether that way is
/// still going.
/// </summary>
/// <remarks>
/// <para>
/// This is a reading of what the trips already say and not a register of its own. A lead is a
/// place of the kind an exploring club keeps its question marks as, named by one of the camp's
/// member trips; nobody writes anything here, and there is nothing here to fall out of step with
/// the places themselves. Whether a way on is still going is a fact about the place and lives on
/// it, so the board reports what is true now rather than what some trip saw on some afternoon.
/// </para>
/// <para>
/// Read over <em>all</em> the trip roles at once. A club that recorded a continuation under
/// "visited" rather than under the obvious role has still recorded it, and a board that asked
/// about one role would drop it without saying so — which is exactly the failure that would go
/// unnoticed, because the missing lead is missing from the only list that would show it.
/// </para>
/// <para>
/// <strong>Every lead is checked one at a time before it leaves.</strong> A board is a bulk path,
/// and a list of undefended ways into caves sorted by how promising they look is the same species
/// of disclosure as a single coordinate — arguably a worse one, since it is ordered by how much
/// somebody would want it. A lead this caller may not place exactly is left off the board
/// altogether rather than shown with its position blurred: the position is not what is being
/// withheld, the place is. That is the same decision the camp's map takes about an entrance and
/// the trip takes about a cave it names.
/// </para>
/// <para>
/// No coordinate is served here at all, so nothing on the board needs a second rule about how much
/// of a position to show.
/// </para>
/// <para>
/// The route takes no filter. Narrowing by a stored property is a general mechanism, and the
/// board would be the first and only caller of half of one.
/// </para>
/// </remarks>
public static class ExpeditionLeadsEndpoints
{
    /// <summary>
    /// The kind of place a way on is recorded as. Resolved to an identity by code at request time,
    /// never carried as a number: the same row is numbered differently on a fresh installation
    /// than on one that grew into it. Taken from the shared constant the seeder writes the row
    /// under, because a board that cannot find the kind answers empty rather than failing — a
    /// second spelling of the code here would empty every camp's board silently.
    /// </summary>
    private const string ContinuationTypeCode = FeatureTypeSeeds.Continuation;

    /// <summary>
    /// Safety cap on the leads in one answer. Ordered by id before it bites, so a camp over the
    /// cap shows the same board every time it is opened rather than an arbitrary subset that moves
    /// under the reader — and the answer says it was capped.
    /// </summary>
    private const int MaxLeads = 2000;

    public static RouteGroupBuilder MapExpeditionLeadsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/expeditions").WithTags("Expeditions")
            .MapGet("/{id:guid}/leads", GetAsync)
            .WithSummary(
                "The open ways on that this camp's trips named, grouped by whether each is still "
                + "going. Read over every trip role, counted once per place, and limited to the "
                + "leads this caller may both read and place exactly. Takes no filter.");

        return api;
    }

    private static async Task<Results<Ok<ExpeditionLeadsDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var camp = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (camp is null || !(await access.DecideAsync(ctx, AccessAction.Read, camp, ct)).Allowed)
        {
            // The same words every other door into a camp uses, so an address cannot be probed for
            // the existence of a camp the caller is not admitted to.
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        var empty = new ExpeditionLeadsDto
        {
            ExpeditionId = id,
            Groups = [],
            Leads = 0,
            Truncated = false,
        };

        // The member trips out of the trips this caller may read — the same narrowing the camp's
        // map and its roll-up apply, and it has to be the same, or the board would report leads
        // found on trips the camp's own listing does not admit to having.
        var memberTripIds = db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == id)
            .Select(m => m.TripLogId);
        var tripIds = await db.TripLogs.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Where(x => memberTripIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (tripIds.Count == 0)
        {
            return TypedResults.Ok(empty);
        }

        var type = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == ContinuationTypeCode)
            .Select(t => new { t.Id, t.PropertiesSchema })
            .FirstOrDefaultAsync(ct);
        if (type is null)
        {
            // An installation whose taxonomy has not been seeded records no ways on at all, which
            // is an empty board rather than a failure.
            return TypedResults.Ok(empty);
        }

        // Distinct by place: two of the camp's trips naming the same way on, or one trip naming it
        // under two roles, is one lead. Every one of those is an ordinary thing for a club to
        // record, and counting the namings instead reads as a camp with twice the prospects.
        var leadIds = (await TripRoleLinks.PairsOfTypeForAsync(db, tripIds, type.Id, ct))
            .Select(pair => pair.FeatureId)
            .Distinct()
            .ToList();
        if (leadIds.Count == 0)
        {
            return TypedResults.Ok(empty);
        }

        // Two gates, in this order, exactly as every other listing of places applies them. The
        // first decides which rows this caller may read at all; the second decides which of those
        // they may place, and a lead they may not place is not named. A way on is a top-level
        // place with access control of its own, so it goes through both on its own row.
        var rows = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => leadIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.Properties })
            .OrderBy(f => f.Id)
            .Take(MaxLeads + 1)
            .ToListAsync(ct);
        var truncated = rows.Count > MaxLeads;
        if (truncated)
        {
            rows = [.. rows.Take(MaxLeads)];
        }

        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, [.. rows.Select(r => r.Id)], ct);
        var leads = rows
            .Where(row => !redacted.Contains(row.Id))
            .Select(row =>
            {
                var (state, grade, note) = Read(row.Properties);
                return (State: state, Lead: new ExpeditionLeadDto
                {
                    Id = row.Id,
                    Name = row.Name,
                    Grade = grade,
                    Note = note,
                });
            })
            .ToList();

        var order = StateOrder(type.PropertiesSchema);
        var groups = leads
            .GroupBy(x => x.State)
            .OrderBy(g => Rank(g.Key, order))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ExpeditionLeadGroupDto
            {
                State = g.Key,
                // Most promising first, on the register's own scale, with the ungraded after the
                // graded: a board is read to decide where to go next, and the answer to that is
                // the top of it.
                Leads =
                [
                    .. g.Select(x => x.Lead)
                        .OrderBy(lead => lead.Grade is null)
                        .ThenBy(lead => lead.Grade, StringComparer.Ordinal)
                        .ThenBy(lead => lead.Name, StringComparer.Ordinal)
                        .ThenBy(lead => lead.Id),
                ],
            })
            .ToList();

        return TypedResults.Ok(new ExpeditionLeadsDto
        {
            ExpeditionId = id,
            Groups = groups,
            Leads = leads.Count,
            Truncated = truncated,
        });
    }

    /// <summary>
    /// What one place says about itself. Every field is optional and a way on with none of them is
    /// ordinary — it was written down the evening it was found, by somebody who had not been down
    /// it yet.
    /// </summary>
    private static (string? State, string? Grade, string? Note) Read(string properties)
    {
        try
        {
            using var document = JsonDocument.Parse(properties);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null);
            }

            return (Text(document.RootElement, "state"), Text(document.RootElement, "grade"),
                Text(document.RootElement, "note"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// The order the states are declared in on the kind of place itself, so the board is ordered by
    /// the same vocabulary that defines it and an installation that has edited that vocabulary is
    /// ordered by its own. Anything unreadable leaves the order empty, and the board falls back to
    /// sorting the states by name — which is worse to read and never wrong.
    /// </summary>
    private static IReadOnlyList<string> StateOrder(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(schema);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("state", out var state)
                && state.ValueKind == JsonValueKind.Object
                && state.TryGetProperty("enum", out var values)
                && values.ValueKind == JsonValueKind.Array)
            {
                return
                [
                    .. values.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!),
                ];
            }
        }
        catch (JsonException)
        {
            // A schema somebody has edited into something unparseable orders nothing; it must not
            // take the board down with it.
        }

        return [];
    }

    /// <summary>
    /// Where a state sorts: the vocabulary's own order first, then anything an installation has
    /// added to it, then the leads nobody has said anything about — which belong last because they
    /// are the ones that have not been looked at rather than the ones that are finished.
    /// </summary>
    private static int Rank(string? state, IReadOnlyList<string> order)
    {
        if (state is null)
        {
            return int.MaxValue;
        }

        var index = order.ToList().IndexOf(state);
        return index >= 0 ? index : int.MaxValue - 1;
    }
}
