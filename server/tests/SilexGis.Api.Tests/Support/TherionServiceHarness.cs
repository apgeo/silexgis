// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Formats.Tar;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SilexGis.Infrastructure.Surveys;
using SilexGis.TherionService;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The survey compiler service, run in this process on a test server with the compiler a test
/// gives it, and a work directory of its own that is removed afterwards.
/// </summary>
internal sealed class TherionServiceHarness : IAsyncDisposable, IHttpClientFactory
{
    private TherionServiceHarness(WebApplication app, string workDir)
    {
        App = app;
        WorkDir = workDir;
    }

    public WebApplication App { get; }

    /// <summary>Where the service makes its runs.</summary>
    public string WorkDir { get; }

    public static async Task<TherionServiceHarness> StartAsync(
        ISurveyCompiler compiler, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var work = Path.Combine(TestScratch.Root, $"silexgis-test-therionservice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var app = TherionServiceHost.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            var values = new Dictionary<string, string?> { ["SurveyCompiler:WorkDir"] = work };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                values[key] = value;
            }

            builder.Configuration.AddInMemoryCollection(values);
            builder.Services.Replace(ServiceDescriptor.Singleton(compiler));
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        return new TherionServiceHarness(app, work);
    }

    /// <summary>A client of the service, at the address an installation would name.</summary>
    public HttpClient Client() =>
        new(App.GetTestServer().CreateHandler()) { BaseAddress = new Uri("http://therion:3100/") };

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

    /// <summary>A tar of the given entries: a name with text, or a name with a link target when the text starts with <c>-&gt;</c>.</summary>
    public static byte[] Tar(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                if (text.StartsWith("->", StringComparison.Ordinal))
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, name) { LinkName = text[2..] });
                    continue;
                }

                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)),
                });
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Every entry of a tar, by name, with its text; links and directories are named with an empty text.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ReadTarAsync(Stream tar)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = new TarReader(tar);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            var text = "";
            if (entry.DataStream is { } data)
            {
                using var content = new StreamReader(data);
                text = await content.ReadToEndAsync();
            }

            entries[entry.Name] = text;
        }

        return entries;
    }
}
