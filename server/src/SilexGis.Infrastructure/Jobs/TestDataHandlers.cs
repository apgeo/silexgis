// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.TestData;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Runs a queued test-data run. A failed run is not retried: it is removed and started again.</summary>
public sealed class TestDataGenerateHandler(TestDataRunner runner) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.TestDataGenerate;

    public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
        runner.RunAsync(TestDataService.RunIdOf(job), ct);
}

/// <summary>Removes a test-data run's rows.</summary>
public sealed class TestDataRemoveHandler(TestDataRemoval removal) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.TestDataRemove;

    public Task ExecuteAsync(ProcessingJob job, CancellationToken ct) =>
        removal.RemoveAsync(TestDataService.RunIdOf(job), ct);
}
