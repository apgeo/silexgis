// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Api.Features.TestData;

/// <summary>
/// The generator from the command line, for an operator: the same service, runner and removal the
/// page uses, run in this process rather than queued. It follows the deployment's half of the
/// switch only — the operator is who sets that half — and writes for the bootstrap administrator.
/// Prints counts, never contents.
/// </summary>
public static class TestDataCommand
{
    /// <param name="bootstrapAdminId">The bootstrap administrator the rows are generated for; null when there is none.</param>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, Guid? bootstrapAdminId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("TestData");
        var options = provider.GetRequiredService<IOptions<TestDataOptions>>().Value;
        if (!options.Allowed)
        {
            logger.LogError("The test-data generator is not allowed on this deployment (SILEXGIS__TestData__Allowed=false)");
            return 2;
        }

        if (bootstrapAdminId is not { } adminId)
        {
            logger.LogError("The test-data generator requires a bootstrap admin (set SILEXGIS__Admin__Email/Password)");
            return 2;
        }

        var service = provider.GetRequiredService<TestDataService>();
        if (args.Contains("remove-test-data"))
        {
            return await RemoveAsync(provider, service, adminId, args, logger);
        }

        var profile = ReadProfile(args, logger);
        if (profile is null)
        {
            return 2;
        }

        var problems = TestDataProfileRules.Problems(profile, options.MaxCaves);
        if (problems.Count > 0)
        {
            logger.LogError("The profile cannot run: {Problems}", string.Join(" ", problems));
            return 2;
        }

        var result = await service.StartAsync(
            profile, adminId, args.Contains("--alongside-existing-data"), queueJob: false, CancellationToken.None);
        switch (result.Outcome)
        {
            case TestDataStartOutcome.RunInProgress:
                logger.LogError("A test-data run or removal is already going");
                return 3;
            case TestDataStartOutcome.ExistingDataNeedsConfirmation:
                logger.LogError(
                    "The database holds {Caves} caves, {Trips} trips and {Documents} documents no run created; "
                    + "add --alongside-existing-data to generate beside them",
                    result.Existing.Caves, result.Existing.Trips, result.Existing.Documents);
                return 3;
        }

        var run = result.Run!;
        await provider.GetRequiredService<TestDataRunner>().RunAsync(run.Id, CancellationToken.None);
        var db = provider.GetRequiredService<SilexGisDbContext>();
        var finished = await db.TestDataRuns.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        logger.LogInformation("Test-data run {RunId}: {Status}, {Counts}", run.Id, finished.Status, finished.Counts);
        return finished.Status == TestDataRunStatus.Succeeded ? 0 : 1;
    }

    private static async Task<int> RemoveAsync(
        IServiceProvider provider, TestDataService service, Guid adminId, string[] args, ILogger logger)
    {
        var index = Array.IndexOf(args, "remove-test-data");
        if (index + 1 >= args.Length || !Guid.TryParse(args[index + 1], out var runId))
        {
            logger.LogError("remove-test-data needs the run's id");
            return 2;
        }

        if (!await service.RequestRemovalAsync(runId, adminId, queueJob: false, CancellationToken.None))
        {
            logger.LogError("Run {RunId} does not exist, is still going, or is already removed", runId);
            return 3;
        }

        await provider.GetRequiredService<TestDataRemoval>().RemoveAsync(runId, CancellationToken.None);
        logger.LogInformation("Test-data run {RunId} removed", runId);
        return 0;
    }

    private static TestDataProfile? ReadProfile(string[] args, ILogger logger)
    {
        var name = Value(args, "--profile") ?? TestDataProfiles.DefaultName;
        TestDataProfile profile;
        if (TestDataProfiles.All.TryGetValue(name, out var shipped))
        {
            profile = shipped;
        }
        else if (File.Exists(name))
        {
            try
            {
                profile = TestDataProfile.FromJson(File.ReadAllText(name));
            }
            catch (JsonException e)
            {
                logger.LogError("The profile file is not a valid profile: {Message}", e.Message);
                return null;
            }
        }
        else
        {
            logger.LogError(
                "--profile must be {Names} or the path of a profile file", string.Join(", ", TestDataProfiles.All.Keys));
            return null;
        }

        if (Value(args, "--seed") is { } seed)
        {
            if (!long.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                logger.LogError("--seed must be a whole number");
                return null;
            }

            profile = profile with { Seed = parsed };
        }

        return profile;
    }

    private static string? Value(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
