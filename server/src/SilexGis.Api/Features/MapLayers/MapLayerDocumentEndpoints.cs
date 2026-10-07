// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapLayers;

/// <summary>
/// One map background, as the page where an administrator decides which of them a document may
/// copy shows it.
/// </summary>
/// <param name="InDocuments">Whether a document may copy it now — what the catalogue publishes.</param>
/// <param name="CatalogueDefault">What the installation's catalogue file says of it.</param>
/// <param name="Choice">
/// What an administrator decided here: <c>on</c>, <c>off</c>, or <c>default</c> while the
/// catalogue's answer stands.
/// </param>
/// <param name="CanBeCopied">
/// Whether the source could carry the mark at all. False for one with no credit to write under
/// a picture, which can be switched off and never on.
/// </param>
public sealed record MapBackgroundDto(
    long Id,
    string Name,
    string? GroupName,
    string? Attribution,
    bool InDocuments,
    bool CatalogueDefault,
    MapLayerDocumentChoice Choice,
    bool CanBeCopied);

/// <summary>An administrator's answer about one background.</summary>
public sealed record MapBackgroundChoiceRequest(MapLayerDocumentChoice Choice);

public sealed class MapBackgroundChoiceRequestValidator : AbstractValidator<MapBackgroundChoiceRequest>
{
    public MapBackgroundChoiceRequestValidator() => RuleFor(x => x.Choice).IsInEnum();
}

/// <summary>
/// Which map backgrounds the documents this installation produces may copy, as an administrator
/// decides it.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue file says what ships, and it goes on being the answer for every source nobody
/// here has decided about — a new installation starts from it, and a correction to it reaches
/// every such source on the next start. What is written here is kept beside the catalogue's
/// answer rather than over it, so that the two can be told apart for as long as both exist: a
/// start never overwrites an administrator's decision, and taking a decision back returns the
/// source to whatever the catalogue says now, not to what it said when the decision was made.
/// </para>
/// <para>
/// Governed like the installation's other settings — reading needs Read over them and deciding
/// needs Write — because that is what this is: a statement, on the installation's behalf, that a
/// provider's terms allow its tiles to leave in a file. Each decision is written to the audit
/// trail with who made it.
/// </para>
/// <para>
/// Only the backgrounds the installation publishes are listed or accepted. A source that is
/// switched off, or withheld for want of its key, is drawn by nothing, and a decision about it
/// would be a decision about a picture nobody can make.
/// </para>
/// </remarks>
public static class MapLayerDocumentEndpoints
{
    /// <summary>The audit trail's name for a catalogue entry.</summary>
    private const string AuditType = "MapLayer";

    public static RouteGroupBuilder MapMapLayerDocumentEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/map-backgrounds").WithTags("Admin");

        admin.MapGet("/", ListAsync)
            .WithSummary(
                "The map backgrounds this installation publishes, each with whether a document may "
                + "copy it, what the catalogue says and what an administrator decided.");
        admin.MapPut("/{id:long}/in-documents", ChooseAsync)
            .WithValidation<MapBackgroundChoiceRequest>()
            .WithSummary(
                "Decides whether documents may copy one background: on, off, or default to follow "
                + "the catalogue again. Kept across restarts.");

        return api;
    }

    private static async Task<Results<Ok<List<MapBackgroundDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var rows = await Backgrounds(db).AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<MapBackgroundDto>, UnauthorizedHttpResult, ProblemHttpResult>> ChooseAsync(
        long id,
        MapBackgroundChoiceRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        // A layer that is not published is answered as one that is not there: it is on no list
        // this route's reader was shown.
        var layer = await db.MapLayers.FirstOrDefaultAsync(l => l.Id == id && l.Enabled, ct);
        if (layer is null)
        {
            return ApiProblems.NotFound("map_layer.not_found");
        }

        var obstacle = MapLayerDocumentRules.ObstacleTo(layer.IsBase, layer.Attribution);
        if (obstacle == MapLayerDocumentObstacle.NotABackground)
        {
            return ApiProblems.Conflict(
                MapLayerDocumentRules.NotABackgroundCode,
                "A document's picture is drawn over one background, and this layer is an overlay.");
        }

        // Switching off, or going back to the catalogue, is always possible; only saying yes
        // needs the credit that would be written under the picture.
        if (request.Choice == MapLayerDocumentChoice.On && obstacle == MapLayerDocumentObstacle.NoAttribution)
        {
            return ApiProblems.Conflict(
                MapLayerDocumentRules.AttributionRequiredCode,
                "This source has no attribution to write under a picture, so it cannot be copied into a document.");
        }

        var before = (Choice: layer.InDocumentsChoice, Effective: Effective(layer));
        layer.InDocumentsChoice = MapLayerDocumentRules.Stored(request.Choice);
        if (before.Choice != layer.InDocumentsChoice)
        {
            // Written by hand: a catalogue entry is rewritten from the file on every start, and
            // recording those would bury the one kind of change to it that a person made.
            db.Set<AuditEntry>().Add(new AuditEntry
            {
                UserId = ctx.UserId,
                Action = AuditActions.Updated,
                EntityType = AuditType,
                EntityId = layer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Changes = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, object?>>
                {
                    ["InDocumentsChoice"] = new()
                    {
                        ["old"] = ChoiceName(before.Choice),
                        ["new"] = ChoiceName(layer.InDocumentsChoice),
                    },
                    ["InDocuments"] = new() { ["old"] = before.Effective, ["new"] = Effective(layer) },
                }),
            });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToDto(layer));
    }

    /// <summary>The backgrounds this installation publishes: what a document could be drawn over.</summary>
    private static IQueryable<MapLayer> Backgrounds(SilexGisDbContext db) =>
        db.MapLayers.Where(l => l.Enabled && l.IsBase);

    private static bool Effective(MapLayer layer) =>
        MapLayerDocumentRules.Effective(layer.InDocuments, layer.InDocumentsChoice);

    private static string ChoiceName(bool? stored) =>
        JsonNamingPolicy.CamelCase.ConvertName(MapLayerDocumentRules.ChoiceOf(stored).ToString());

    private static MapBackgroundDto ToDto(MapLayer layer) => new(
        layer.Id,
        layer.Name,
        layer.GroupName,
        layer.Attribution,
        Effective(layer),
        layer.InDocuments,
        MapLayerDocumentRules.ChoiceOf(layer.InDocumentsChoice),
        MapLayerDocumentRules.ObstacleTo(layer.IsBase, layer.Attribution) is null);
}
