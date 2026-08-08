// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// The club's naming habits, configured once and reusable.
///
/// <para>
/// Rule sets carry no visibility of their own and every signed-in caller reads all of them: a
/// rule names no cave and holds no coordinate, and a club that could not show a neighbouring
/// club its rules could not hand them over either. What is governed is writing — your own set
/// is yours, and anything a group or the installation inherits is an administrator's, because
/// promoting a set changes what everybody else's next import proposes.
/// </para>
/// </summary>
public static class TermRuleEndpoints
{
    public static RouteGroupBuilder MapTermRuleEndpoints(this RouteGroupBuilder api)
    {
        var sets = api.MapGroup("/term-rule-sets").WithTags("TermRules");

        sets.MapGet("/", ListAsync)
            .WithSummary("Every rule set, with whether the caller may edit or delete each.");
        sets.MapGet("/effective", GetEffectiveAsync)
            .WithSummary("The set an import would run for the caller if they named none.");
        sets.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One rule set with its rules.");
        sets.MapPost("/", CreateAsync).WithValidation<TermRuleSetCreateRequest>()
            .WithSummary("Creates a set of the caller's own, optionally copied from another.");
        sets.MapPut("/{id:guid}", UpdateAsync).WithValidation<TermRuleSetWriteRequest>()
            .WithSummary("Replaces a set's name, description and rules.");
        sets.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a set. The shipped one is never deletable.");
        sets.MapPost("/{id:guid}/scope", SetScopeAsync).WithValidation<TermRuleSetScopeRequest>()
            .WithSummary("Moves a set between scopes or makes it a default (administrator).");
        sets.MapGet("/{id:guid}/export", ExportAsync)
            .WithSummary("The set as a file, for sending to another installation.");
        sets.MapPost("/import", ImportAsync).WithValidation<TermRuleSetImportRequest>()
            .WithSummary("Creates a set of the caller's own from an exported file.");

        return api;
    }

    private static async Task<Results<Ok<List<TermRuleSetDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await db.TermRuleSets.AsNoTracking()
            .OrderBy(s => s.Scope).ThenByDescending(s => s.IsDefault).ThenBy(s => s.Name)
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(s => ToDto(s, ctx)).ToList());
    }

    private static async Task<Results<Ok<EffectiveTermRuleSetDto>, UnauthorizedHttpResult>> GetEffectiveAsync(
        TermRuleSetStore store,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await store.ResolveDefaultAsync(ctx, ct);
        return TypedResults.Ok(new EffectiveTermRuleSetDto(
            set is null ? null : ToDetail(set, ctx),
            set?.Scope));
    }

    private static async Task<Results<Ok<TermRuleSetDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.TermRuleSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return set is null ? ApiProblems.NotFound("term_rules.not_found") : TypedResults.Ok(ToDetail(set, ctx));
    }

    private static async Task<Results<Created<TermRuleSetDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TermRuleSetCreateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        IReadOnlyList<TermRule> rules = request.Rules ?? [];
        if (request.CopyFromId is { } copyFrom)
        {
            // Copying is how somebody edits a set they do not own: the installation default
            // stays exactly as it shipped, and the copy is theirs to change.
            var source = await db.TermRuleSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == copyFrom, ct);
            if (source is null)
            {
                return ApiProblems.NotFound("term_rules.not_found");
            }

            rules = TermRuleSetStore.RulesOf(source);
        }

        if (Validate(request.Name, request.Description, rules) is { } problem)
        {
            return problem;
        }

        var set = new TermRuleSet
        {
            Name = request.Name.Trim(),
            Description = Blank(request.Description),
            Scope = TermRuleScope.User,
            OwnerUserId = ctx.UserId,
            // A caller's first set becomes the one their imports start from; making the second
            // one the default as well would be a silent change to a habit they already have.
            IsDefault = !await db.TermRuleSets
                .AnyAsync(s => s.Scope == TermRuleScope.User && s.OwnerUserId == ctx.UserId && s.IsDefault, ct),
            Rules = Document(request.Name, request.Description, rules),
        };

        db.TermRuleSets.Add(set);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/term-rule-sets/{set.Id}", ToDetail(set, ctx));
    }

    private static async Task<Results<Ok<TermRuleSetDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TermRuleSetWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.TermRuleSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        if (!TermRuleSetRules.MayEdit(ctx, set))
        {
            // Not a dead end: the client's next move is to copy the set, which is what the
            // owner asked for — editing a set you do not own gives you one of your own.
            return ApiProblems.Forbidden(TermRuleSetRules.NotEditableCode);
        }

        if (Validate(request.Name, request.Description, request.Rules) is { } problem)
        {
            return problem;
        }

        set.Name = request.Name.Trim();
        set.Description = Blank(request.Description);
        set.Rules = Document(request.Name, request.Description, request.Rules);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDetail(set, ctx));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.TermRuleSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        if (set.IsSeeded)
        {
            return ApiProblems.BadRequest(
                "term_rules.seeded_undeletable",
                "The shipped set is the fallback when no other set applies, so it cannot be deleted.");
        }

        if (!TermRuleSetRules.MayEdit(ctx, set))
        {
            return ApiProblems.Forbidden(TermRuleSetRules.NotEditableCode);
        }

        db.TermRuleSets.Remove(set);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Promotion, which is the whole of rule sharing: a set becomes a group's or the
    /// installation's, and the next import of everybody who inherits it proposes differently.
    /// Administrators only, and one default per scope instance, held by a partial unique index
    /// so two administrators promoting at once cannot both win.
    /// </summary>
    private static async Task<Results<Ok<TermRuleSetDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> SetScopeAsync(
        Guid id,
        TermRuleSetScopeRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.TermRuleSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        if (!TermRuleSetRules.MayPromote(ctx))
        {
            return ApiProblems.Forbidden(TermRuleSetRules.PromotionForbiddenCode);
        }

        if (request.CavingGroupId is { } groupId
            && !await db.CavingGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return ApiProblems.BadRequest("term_rules.group_not_found", "No such caving group.");
        }

        // Demote the incumbent first: one default per scope is a database constraint, and
        // clearing before setting is what stops a promotion failing on it.
        if (request.IsDefault)
        {
            await db.TermRuleSets
                .Where(s => s.Id != set.Id
                    && s.IsDefault
                    && s.Scope == request.Scope
                    && s.CavingGroupId == request.CavingGroupId
                    && (request.Scope != TermRuleScope.User || s.OwnerUserId == set.OwnerUserId))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsDefault, false), ct);
        }

        set.Scope = request.Scope;
        set.CavingGroupId = request.Scope == TermRuleScope.CavingGroup ? request.CavingGroupId : null;
        set.IsDefault = request.IsDefault;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDetail(set, ctx));
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await db.TermRuleSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        var document = new TermRuleDocument
        {
            Name = set.Name,
            Description = set.Description,
            Rules = TermRuleSetStore.RulesOf(set),
        };
        var bytes = Encoding.UTF8.GetBytes(ImportJson.Serialize(document));
        return TypedResults.File(bytes, "application/json", $"{Tag.Slugify(set.Name)}-rules.json");
    }

    private static async Task<Results<Created<TermRuleSetDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> ImportAsync(
        TermRuleSetImportRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var name = Blank(request.Name) ?? Blank(request.Document.Name) ?? "Imported rules";
        var rules = request.Document.Rules;
        if (Validate(name, request.Document.Description, rules) is { } problem)
        {
            return problem;
        }

        // An imported set arrives as the importer's own, never as anybody's default. A file
        // from another club is a proposal; making it the thing every import runs is a separate,
        // deliberate act.
        var set = new TermRuleSet
        {
            Name = name,
            Description = Blank(request.Document.Description),
            Scope = TermRuleScope.User,
            OwnerUserId = ctx.UserId,
            IsDefault = false,
            Rules = Document(name, request.Document.Description, rules),
        };

        db.TermRuleSets.Add(set);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/term-rule-sets/{set.Id}", ToDetail(set, ctx));
    }

    // ---------- helpers ----------

    private static ProblemHttpResult? Validate(string name, string? description, IReadOnlyList<TermRule> rules)
    {
        var errors = TermRuleValidation.Validate(new TermRuleDocument
        {
            Name = name,
            Description = description,
            Rules = rules,
        });
        return errors.Count == 0
            ? null
            : ApiProblems.BadRequest("term_rules.invalid", string.Join(" ", errors));
    }

    private static string Document(string name, string? description, IReadOnlyList<TermRule> rules) =>
        ImportJson.Serialize(new TermRuleDocument
        {
            Name = name.Trim(),
            Description = Blank(description),
            Rules = rules,
        });

    private static TermRuleSetDto ToDto(TermRuleSet set, AccessContext ctx) => set.ToDto(
        TermRuleSetStore.RulesOf(set).Count,
        TermRuleSetRules.MayEdit(ctx, set),
        TermRuleSetRules.MayDelete(ctx, set));

    private static TermRuleSetDetailDto ToDetail(TermRuleSet set, AccessContext ctx)
    {
        var rules = TermRuleSetStore.RulesOf(set);
        return new TermRuleSetDetailDto(
            set.ToDto(rules.Count, TermRuleSetRules.MayEdit(ctx, set), TermRuleSetRules.MayDelete(ctx, set)),
            rules);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
