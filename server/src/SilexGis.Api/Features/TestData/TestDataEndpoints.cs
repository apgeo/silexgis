// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Features.TestData;

/// <param name="DeploymentAllows">The deployment's half of the switch (TestData:Allowed).</param>
/// <param name="Enabled">The administrators' half (the testData settings section).</param>
/// <param name="MaxCaves">The most caves one run may ask for here.</param>
/// <param name="Profiles">The shipped profiles by name, each a complete profile document.</param>
/// <param name="Existing">What the database holds that no run created; a run beside it needs confirming.</param>
/// <param name="Runs">The runs, newest first.</param>
/// <param name="Tables">Every table and how test data reaches it.</param>
public sealed record TestDataOverviewDto(
    bool DeploymentAllows,
    bool Enabled,
    int MaxCaves,
    IReadOnlyDictionary<string, TestDataProfile> Profiles,
    TestDataExistingDataDto Existing,
    IReadOnlyList<TestDataRunDto> Runs,
    IReadOnlyList<TestDataTableDto> Tables);

public sealed record TestDataExistingDataDto(int Caves, int Trips, int Documents);

/// <param name="Counts">Rows written, by kind. Counts only.</param>
/// <param name="Notes">What the run could not do, and why.</param>
public sealed record TestDataRunDto(
    Guid Id,
    TestDataRunStatus Status,
    string? Phase,
    int ProgressDone,
    int ProgressTotal,
    long Seed,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<string> Notes,
    string? Error,
    bool AlongsideExistingData,
    Guid? RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? CancelRequestedAt,
    DateTimeOffset? RemovedAt,
    TestDataProfile Profile);

/// <param name="Part">The part of the generator that writes the table (geography, people, underground, activity, documents, geodata, accounts); null for derived and exempt tables.</param>
public sealed record TestDataTableDto(string Table, TestDataTableRole Role, string? Part, string Note);

public sealed record TestDataColumnCoverageDto(string Column, long NonNull);

public sealed record TestDataTableCoverageDto(string Table, long Rows, IReadOnlyList<TestDataColumnCoverageDto> Columns);

/// <param name="Profile">The profile to run; null runs the shipped profile named by <paramref name="ProfileName"/>.</param>
/// <param name="ProfileName">A shipped profile's name (small, default, large); used when no profile is given.</param>
/// <param name="ConfirmAlongsideExistingData">
/// Generate even though the database holds caves, trips or documents no run created. Without it
/// such a request is refused with testdata.existing_data_confirm_required and the counts.
/// </param>
public sealed record TestDataStartRequest(
    TestDataProfile? Profile,
    string? ProfileName,
    bool ConfirmAlongsideExistingData);

public sealed class TestDataStartRequestValidator : AbstractValidator<TestDataStartRequest>
{
    public TestDataStartRequestValidator()
    {
        RuleFor(x => x.ProfileName)
            .Must(name => name is null || TestDataProfiles.All.ContainsKey(name))
            .WithMessage($"profileName must be one of: {string.Join(", ", TestDataProfiles.All.Keys)}.");
    }
}

/// <param name="Confirm">Must be true: removal deletes everything the run wrote, for good.</param>
public sealed record TestDataRemoveRequest(bool Confirm);

public sealed class TestDataRemoveRequestValidator : AbstractValidator<TestDataRemoveRequest>;

/// <summary>
/// The test-data generator. Full administrators only, and only while both halves of its switch
/// are on: otherwise every route answers 404 testdata.disabled, so an installation that does not
/// offer it shows no trace of it.
/// </summary>
public static class TestDataEndpoints
{
    public static RouteGroupBuilder MapTestDataEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/admin/test-data").WithTags("Admin");

        group.MapGet("/", OverviewAsync)
            .WithSummary("The generator's state: its switch, the shipped profiles, the runs, and what the database already holds.");
        group.MapPost("/runs", StartAsync).WithValidation<TestDataStartRequest>()
            .WithSummary("Queues a run that generates invented test data from a profile.")
            .WithDescription(
                "Refused with testdata.existing_data_confirm_required (and the counts) when the database holds caves, trips "
                + "or documents no run created, until confirmAlongsideExistingData is true; with testdata.run_in_progress "
                + "while another run or removal is going; with testdata.profile_invalid and the problems for a profile "
                + "that cannot run.");
        group.MapGet("/runs/{id:guid}", RunAsync)
            .WithSummary("One run: its progress, what it wrote (counts), and what it could not do.");
        group.MapGet("/runs/{id:guid}/coverage", CoverageAsync)
            .WithSummary("How many rows of each table the run accounts for, and how many of them fill each column. Counts only.");
        group.MapPost("/runs/{id:guid}/cancel", CancelAsync)
            .WithSummary("Asks a waiting or running run to stop between two items. What it wrote stays listed and can be removed.");
        group.MapPost("/runs/{id:guid}/remove", RemoveAsync).WithValidation<TestDataRemoveRequest>()
            .WithSummary("Queues the removal of everything a finished run wrote, by its registry. Needs confirm=true.");
        return api;
    }

    private static async Task<Results<Ok<TestDataOverviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> OverviewAsync(
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        TestDataService service,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        var runs = await db.TestDataRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct);
        var existing = await service.ExistingDataAsync(ct);
        return TypedResults.Ok(new TestDataOverviewDto(
            availability.DeploymentAllows,
            true,
            Math.Min(availability.MaxCaves, TestDataProfileRules.AbsoluteMaxCaves),
            TestDataProfiles.All,
            new TestDataExistingDataDto(existing.Caves, existing.Trips, existing.Documents),
            [.. runs.Select(ToDto)],
            [.. TestDataTables.All.OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new TestDataTableDto(t.Key, t.Value.Role, t.Value.Part, t.Value.Note))]));
    }

    private static async Task<Results<Accepted<TestDataRunDto>, UnauthorizedHttpResult, ProblemHttpResult>> StartAsync(
        TestDataStartRequest request,
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        TestDataService service,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        var profile = request.Profile ?? TestDataProfiles.All[request.ProfileName ?? TestDataProfiles.DefaultName];
        var problems = TestDataProfileRules.Problems(profile, availability.MaxCaves);
        if (problems.Count > 0)
        {
            return ApiProblems.BadRequest(
                "testdata.profile_invalid", string.Join(" ", problems), "problems", problems);
        }

        var result = await service.StartAsync(profile, ctx.UserId, request.ConfirmAlongsideExistingData, queueJob: true, ct);
        return result.Outcome switch
        {
            TestDataStartOutcome.RunInProgress => ApiProblems.Conflict(
                "testdata.run_in_progress", "A test-data run or removal is already going; wait for it or cancel it."),
            TestDataStartOutcome.ExistingDataNeedsConfirmation => ApiProblems.Conflict(
                "testdata.existing_data_confirm_required",
                "The database holds data no test-data run created; confirm to generate beside it.",
                "existing",
                new TestDataExistingDataDto(result.Existing.Caves, result.Existing.Trips, result.Existing.Documents)),
            _ => TypedResults.Accepted($"/api/v1/admin/test-data/runs/{result.Run!.Id}", ToDto(result.Run)),
        };
    }

    private static async Task<Results<Ok<TestDataRunDto>, UnauthorizedHttpResult, ProblemHttpResult>> RunAsync(
        Guid id,
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        var run = await db.TestDataRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? ApiProblems.NotFound("testdata.run_not_found") : TypedResults.Ok(ToDto(run));
    }

    private static async Task<Results<Ok<List<TestDataTableCoverageDto>>, UnauthorizedHttpResult, ProblemHttpResult>> CoverageAsync(
        Guid id,
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        if (!await db.TestDataRuns.AnyAsync(r => r.Id == id, ct))
        {
            return ApiProblems.NotFound("testdata.run_not_found");
        }

        var report = await TestDataCoverageSql.MeasureAsync(db, id, ct);
        return TypedResults.Ok(report
            .Select(t => new TestDataTableCoverageDto(
                t.Table, t.Rows, [.. t.Columns.Select(c => new TestDataColumnCoverageDto(c.Column, c.NonNull))]))
            .ToList());
    }

    private static async Task<Results<Ok<TestDataRunDto>, UnauthorizedHttpResult, ProblemHttpResult>> CancelAsync(
        Guid id,
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        TestDataService service,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        if (!await db.TestDataRuns.AnyAsync(r => r.Id == id, ct))
        {
            return ApiProblems.NotFound("testdata.run_not_found");
        }

        if (!await service.CancelAsync(id, ct))
        {
            return ApiProblems.Conflict("testdata.run_not_cancellable", "Only a waiting or running run can be cancelled.");
        }

        var run = await db.TestDataRuns.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        return TypedResults.Ok(ToDto(run));
    }

    private static async Task<Results<Accepted<TestDataRunDto>, UnauthorizedHttpResult, ProblemHttpResult>> RemoveAsync(
        Guid id,
        TestDataRemoveRequest request,
        IAccessContextAccessor accessAccessor,
        TestDataAvailability availability,
        TestDataService service,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        var (ctx, refusal) = await GateAsync(accessAccessor, availability, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (refusal is not null)
        {
            return refusal;
        }

        if (!request.Confirm)
        {
            return ApiProblems.BadRequest("testdata.confirm_required", "Removing a run needs confirm=true.");
        }

        if (!await db.TestDataRuns.AnyAsync(r => r.Id == id, ct))
        {
            return ApiProblems.NotFound("testdata.run_not_found");
        }

        if (!await service.RequestRemovalAsync(id, ctx.UserId, queueJob: true, ct))
        {
            return ApiProblems.Conflict(
                "testdata.run_not_removable", "Only a finished, failed or cancelled run can be removed, and only once.");
        }

        var run = await db.TestDataRuns.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        return TypedResults.Accepted($"/api/v1/admin/test-data/runs/{id}", ToDto(run));
    }

    /// <summary>
    /// The one gate of every route: signed in (a null context), then the generator switched on (404
    /// otherwise, so a deployment that does not offer it shows no trace of it), then a full
    /// administrator.
    /// </summary>
    private static async Task<(AccessContext? Context, ProblemHttpResult? Refusal)> GateAsync(
        IAccessContextAccessor accessAccessor, TestDataAvailability availability, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return (null, null);
        }

        if (!await availability.IsAvailableAsync(ct))
        {
            return (ctx, ApiProblems.NotFound("testdata.disabled"));
        }

        return (ctx, ctx.IsFullAdmin ? null : ApiProblems.Forbidden("access.forbidden"));
    }

    private static TestDataRunDto ToDto(TestDataRun run) => new(
        run.Id,
        run.Status,
        run.Phase,
        run.ProgressDone,
        run.ProgressTotal,
        run.Seed,
        JsonSerializer.Deserialize<Dictionary<string, int>>(run.Counts) ?? [],
        JsonSerializer.Deserialize<List<string>>(run.Notes) ?? [],
        run.Error,
        run.AlongsideExistingData,
        run.RequestedBy,
        run.CreatedAt,
        run.StartedAt,
        run.FinishedAt,
        run.CancelRequestedAt,
        run.RemovedAt,
        TestDataProfile.FromJson(run.Profile));
}
