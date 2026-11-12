// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.TestData;

/// <summary>Whether the generator may be used now: the deployment allows it and the administrators have it on.</summary>
public sealed class TestDataAvailability(IOptions<TestDataOptions> options, IAppSettingsService settings)
{
    public bool DeploymentAllows => options.Value.Allowed;

    public int MaxCaves => options.Value.MaxCaves;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) =>
        options.Value.Allowed && (await settings.GetTestDataAsync(ct)).Enabled;
}
