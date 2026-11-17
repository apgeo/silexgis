// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SilexGis.Infrastructure.Analysis;
using SilexGis.WhiteboxService;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The analysis service, run in this process on a test server with the program a test gives it,
/// and a work directory of its own that is removed afterwards.
/// </summary>
internal sealed class WhiteboxServiceHarness : IAsyncDisposable, IHttpClientFactory
{
    private WhiteboxServiceHarness(WebApplication app, string workDir)
    {
        App = app;
        WorkDir = workDir;
    }

    public WebApplication App { get; }

    /// <summary>Where the service makes its runs.</summary>
    public string WorkDir { get; }

    public static async Task<WhiteboxServiceHarness> StartAsync(
        IWhiteboxTools tools, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var work = Path.Combine(TestScratch.Root, $"silexgis-test-whiteboxservice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var app = WhiteboxServiceHost.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            var values = new Dictionary<string, string?> { ["WhiteboxService:WorkDir"] = work };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                values[key] = value;
            }

            builder.Configuration.AddInMemoryCollection(values);
            builder.Services.Replace(ServiceDescriptor.Singleton(tools));
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        return new WhiteboxServiceHarness(app, work);
    }

    /// <summary>A client of the service, at the address an installation would name.</summary>
    public HttpClient Client() =>
        new(App.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://whitebox:3200/") };

    HttpClient IHttpClientFactory.CreateClient(string name) => Client();

    /// <summary>What is left in the work directory: nothing, once every run has ended.</summary>
    public IReadOnlyList<string> Leftovers() => Directory.EnumerateFileSystemEntries(WorkDir).ToList();

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            Directory.Delete(WorkDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
