// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// WhiteboxTools' local mode and the switch that chooses it: what the installation's settings
/// select, the rules every run is held to, the sandboxed command a run starts, runs of a shell
/// script standing in for the program, and — where the real program is installed — a geomorphon
/// run over an invented surface.
/// </summary>
public sealed class LocalWhiteboxToolsTests : IDisposable
{
    private readonly string root = Path.Combine(TestScratch.Root, $"silexgis-test-whitebox-local-{Guid.NewGuid():N}");
    private readonly string run;

    public LocalWhiteboxToolsTests()
    {
        run = Path.Combine(root, "run");
        Directory.CreateDirectory(run);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, null, typeof(NoWhiteboxTools))]
    [InlineData("none", null, typeof(NoWhiteboxTools))]
    [InlineData("LOCAL", null, typeof(LocalWhiteboxTools))]
    [InlineData("service", "http://whitebox:3200", typeof(ServiceWhiteboxTools))]
    public void The_mode_chooses_how_the_program_runs_and_none_is_the_default(string? mode, string? url, Type expected)
    {
        using var provider = Services(mode, url);

        var tools = provider.GetRequiredService<IWhiteboxTools>();

        tools.ShouldBeOfType(expected);
        if (expected == typeof(NoWhiteboxTools))
        {
            tools.IsAvailable.ShouldBeFalse();
            tools.UnavailableReason.ShouldBe(WhiteboxUnavailable.NotConfigured);
            Should.Throw<WhiteboxUnavailableException>(() =>
                tools.RunAsync(new WhiteboxRunRequest(run, "Geomorphons", [], TimeSpan.FromMinutes(1)), CancellationToken.None));
        }
    }

    [Theory]
    [InlineData("loacl", null, "must be 'none', 'local' or 'service'")]
    [InlineData("service", null, "SILEXGIS__Analysis__Whitebox__Url")]
    [InlineData("service", "whitebox:3200", "SILEXGIS__Analysis__Whitebox__Url")]
    [InlineData("service", "file:///etc/passwd", "SILEXGIS__Analysis__Whitebox__Url")]
    public void A_setting_the_application_cannot_honour_stops_it_at_start_naming_the_setting(string mode, string? url, string named)
    {
        using var provider = Services(mode, url, register: false);

        var refused = Should.Throw<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<WhiteboxOptions>>().Value);

        refused.Message.ShouldContain(named);
    }

    [Fact]
    public void A_missing_program_or_sandbox_makes_local_mode_unavailable_with_its_reason()
    {
        var missing = new LocalWhiteboxTools(
            Options.Create(new WhiteboxOptions { Mode = "local", WhiteboxPath = Path.Combine(root, "nowhere", "whitebox_tools") }),
            NullLogger<LocalWhiteboxTools>.Instance);
        missing.IsAvailable.ShouldBeFalse();
        missing.UnavailableReason.ShouldBe(WhiteboxUnavailable.ToolMissing);
        missing.Version.ShouldBeNull();

        Assert.SkipUnless(OperatingSystem.IsLinux(), "Bubblewrap is looked for on Linux only.");
        var unsandboxed = new LocalWhiteboxTools(
            Options.Create(new WhiteboxOptions
            {
                Mode = "local",
                WhiteboxPath = Script("whitebox_tools", "exit 0"),
                BwrapPath = Path.Combine(root, "nowhere", "bwrap"),
            }),
            NullLogger<LocalWhiteboxTools>.Instance);
        unsandboxed.IsAvailable.ShouldBeFalse();
        unsandboxed.UnavailableReason.ShouldBe(WhiteboxUnavailable.SandboxMissing);
    }

    [Theory]
    [InlineData("Geomorphons", "dem.tif", "forms.tif", "50", null)]
    [InlineData("Slope", "dem.tif", "forms.tif", "50", "not a tool this application runs")]
    [InlineData("InstallWbExtension", "dem.tif", "forms.tif", "50", "not a tool this application runs")]
    [InlineData("Geomorphons", "/etc/passwd", "forms.tif", "50", "not a plain file name")]
    [InlineData("Geomorphons", "../dem.tif", "forms.tif", "50", "not a plain file name")]
    [InlineData("Geomorphons", "dem.tif", "sub/forms.tif", "50", "not a plain file name")]
    [InlineData("Geomorphons", "dem.tif", "forms.tif", "50 --wd=/", "not a number")]
    [InlineData("Geomorphons", "dem.tif", "forms.tif", "NaN", "not a number")]
    [InlineData("Geomorphons", "dem.tif", "DEM.TIF", "50", "named by two arguments")]
    public void Every_run_is_held_to_the_rules(string tool, string input, string output, string search, string? problem)
    {
        WhiteboxArgument[] arguments =
        [
            WhiteboxArgument.Input("dem", input),
            WhiteboxArgument.Output("output", output),
            new("search", WhiteboxArgumentKind.Value, search),
        ];

        var said = WhiteboxRequestRules.Problem(tool, arguments);

        if (problem is null)
        {
            said.ShouldBeNull();
        }
        else
        {
            said.ShouldNotBeNull().ShouldContain(problem);
        }

        // A run that writes nothing, a repeated parameter and a switch with a value are refused too.
        WhiteboxRequestRules.Problem("Geomorphons", [WhiteboxArgument.Input("dem", "dem.tif")]).ShouldNotBeNull();
        WhiteboxRequestRules.Problem("Geomorphons",
            [WhiteboxArgument.Output("output", "a.tif"), WhiteboxArgument.Output("output", "b.tif")]).ShouldNotBeNull();
        WhiteboxRequestRules.Problem("Geomorphons",
            [WhiteboxArgument.Output("output", "a.tif"), new("forms", WhiteboxArgumentKind.Flag, "x")]).ShouldNotBeNull();
        // Only the tool's own parameters, of their own kinds: the program reads anything that begins
        // like one of its global options (--cd, --wd) as that option.
        WhiteboxRequestRules.Problem("Geomorphons",
            [WhiteboxArgument.Output("output", "a.tif"), WhiteboxArgument.Number("cdx", 5)]).ShouldNotBeNull().ShouldContain("no value parameter 'cdx'");
        WhiteboxRequestRules.Problem("Geomorphons",
            [WhiteboxArgument.Output("output", "a.tif"), WhiteboxArgument.Switch("wd")]).ShouldNotBeNull();
        WhiteboxRequestRules.Problem("Geomorphons",
            [WhiteboxArgument.Output("output", "a.tif"), WhiteboxArgument.Input("search", "b.tif")]).ShouldNotBeNull();
    }

    [Fact]
    public void The_command_runs_the_program_in_the_sandbox_with_limits_full_paths_and_none_of_the_servers_environment()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The sandbox is bubblewrap's, on Linux.");
        var program = Script("whitebox_tools", "exit 0");
        var settings = new WhiteboxOptions { MaxMemoryBytes = 1L << 30, MaxOutputFileBytes = 1L << 20 };
        var runner = new SandboxedWhiteboxRunner(program, "/usr/bin/bwrap", settings);

        var start = runner.Command(run, "Geomorphons",
            [WhiteboxArgument.Input("dem", "dem.tif"), WhiteboxArgument.Output("output", "f.tif"), WhiteboxArgument.Number("search", 25), WhiteboxArgument.Switch("forms")],
            TimeSpan.FromMinutes(10));
        var arguments = start.ArgumentList.ToList();

        // No shell anywhere: each wrapper and the program get an argument list.
        start.UseShellExecute.ShouldBeFalse();
        Path.GetFileName(start.FileName).ShouldBe("prlimit");
        arguments.ShouldContain("--as=1073741824");
        arguments.ShouldContain("--fsize=1048576");
        arguments.ShouldContain("--unshare-all");
        arguments.ShouldContain("--clearenv");
        arguments.ShouldContain("--die-with-parent");
        // Its own directory read-only (it reads its settings there); the run the one writable place.
        var programDirectory = Path.GetDirectoryName(program)!;
        Windowed(arguments, "--ro-bind", programDirectory, programDirectory).ShouldBeTrue();
        Windowed(arguments, "--bind", run, run).ShouldBeTrue();
        arguments.Count(a => a == "--bind").ShouldBe(2); // the run, and its scratch as /tmp
        // The program, then its arguments, files as full paths inside the run.
        arguments.SkipWhile(a => a != program).ShouldBe(
        [
            program, "--run=Geomorphons",
            $"--dem={Path.Combine(run, "dem.tif")}", $"--output={Path.Combine(run, "f.tif")}", "--search=25", "--forms",
        ]);
        arguments.ShouldNotContain(a => a.StartsWith("--wd", StringComparison.Ordinal));
        // Nothing of this process's environment, which in a real server holds the database password.
        start.Environment.Keys.Order(StringComparer.Ordinal).ShouldBe(["LANG", "PATH"]);

        // A run directory the program would read wrongly is refused rather than handed over.
        var mangled = Path.Combine(root, "a=b");
        Directory.CreateDirectory(mangled);
        Should.Throw<InvalidOperationException>(() =>
            runner.Command(mangled, "Geomorphons", [WhiteboxArgument.Output("output", "f.tif")], TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task A_stand_in_run_writes_its_output_and_a_link_or_a_missing_output_is_not_success()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Stands in for the program with a shell script.");
        File.WriteAllText(Path.Combine(run, "dem.tif"), "elevation");
        var program = Script("whitebox_tools",
            """
            for a in "$@"; do
              case "$a" in
                --output=*) out="${a#--output=}" ;;
                --dem=*) dem="${a#--dem=}" ;;
              esac
            done
            mode=$(cat "$(dirname "$dem")/mode" 2>/dev/null)
            if [ "$mode" = link ]; then ln -s /etc/passwd "$out"; exit 0; fi
            if [ "$mode" = none ]; then echo "nothing written" >&2; exit 0; fi
            echo "env=${SILEXGIS__Db__ConnectionString:-unset}"
            cat "$dem" > "$out"
            """);
        var tools = Local(program);

        var made = await tools.RunAsync(Request(), CancellationToken.None);
        made.Succeeded.ShouldBeTrue();
        made.OutputFiles.ShouldBe(["forms.tif"]);
        File.ReadAllText(Path.Combine(run, "forms.tif")).ShouldBe("elevation");
        made.OutputTail.ShouldContain("env=unset");

        File.Delete(Path.Combine(run, "forms.tif"));
        File.WriteAllText(Path.Combine(run, "mode"), "link");
        var linked = await tools.RunAsync(Request(), CancellationToken.None);
        linked.Succeeded.ShouldBeFalse();
        linked.OutputFiles.ShouldBeEmpty();
        // The link is gone, so nothing downstream reads through it.
        new FileInfo(Path.Combine(run, "forms.tif")).LinkTarget.ShouldBeNull();
        File.Exists(Path.Combine(run, "forms.tif")).ShouldBeFalse();

        File.WriteAllText(Path.Combine(run, "mode"), "none");
        var silent = await tools.RunAsync(Request(), CancellationToken.None);
        silent.Succeeded.ShouldBeFalse();
        silent.OutputTail.ShouldContain("nothing written");

        // An output that is already there is refused before anything starts.
        File.WriteAllText(Path.Combine(run, "forms.tif"), "earlier");
        await Should.ThrowAsync<ArgumentException>(() => tools.RunAsync(Request(), CancellationToken.None));
        File.ReadAllText(Path.Combine(run, "forms.tif")).ShouldBe("earlier");
    }

    [Fact]
    public async Task A_run_past_its_time_is_stopped_and_reported_as_a_timeout()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Stands in for the program with a shell script.");
        File.WriteAllText(Path.Combine(run, "dem.tif"), "elevation");
        var tools = Local(Script("whitebox_tools", "exec sleep 120"));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Should.ThrowAsync<TimeoutException>(() =>
            tools.RunAsync(Request() with { Timeout = TimeSpan.FromSeconds(1) }, CancellationToken.None));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void The_version_is_the_programs_own_first_line_and_nothing_else()
    {
        SandboxedWhiteboxRunner.VersionOf("WhiteboxTools v2.4.0 (c) Dr. John Lindsay 2017-2023").ShouldBe("WhiteboxTools 2.4.0");
        SandboxedWhiteboxRunner.VersionOf("whitebox v2.4.0").ShouldBeNull();
        SandboxedWhiteboxRunner.VersionOf("WhiteboxTools v2.4.0;rm").ShouldBeNull();
        SandboxedWhiteboxRunner.VersionOf(null).ShouldBeNull();

        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Stands in for the program with a shell script.");
        SandboxedWhiteboxRunner.ProbeVersion(Script("answers", "echo 'WhiteboxTools v2.4.0 (c) Dr. John Lindsay 2017-2023'"))
            .ShouldBe("WhiteboxTools 2.4.0");
        SandboxedWhiteboxRunner.ProbeVersion(Script("silent", "exec sleep 60"), TimeSpan.FromSeconds(1)).ShouldBeNull();
    }

    /// <summary>
    /// The real program over an invented bowl: a surface that falls towards its centre from every
    /// side. Its centre is a pit (class 10 of the ten common landforms) and every cell is one of the
    /// ten classes.
    /// </summary>
    [WhiteboxFact]
    public async Task The_real_program_classifies_an_invented_bowl_with_a_pit_at_its_centre()
    {
        const int size = 41;
        var dem = Path.Combine(run, "dem.tif");
        GdalRuntime.Configure();
        using (var dataset = Gdal.GetDriverByName("GTiff").Create(dem, size, size, 1, DataType.GDT_Float32, null))
        {
            // Ten-metre cells in a projected grid: the search distance and flatness are metric.
            dataset.SetGeoTransform([500_000, 10, 0, 5_100_000, 0, -10]);
            using (var reference = new SpatialReference(null))
            {
                reference.ImportFromEPSG(32635);
                reference.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);
            }

            var heights = new float[size * size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    var dx = column - (size / 2);
                    var dy = row - (size / 2);
                    // Rising with the square of the distance, so every line of sight from the centre
                    // curves upwards. A cone would not do: along a straight line the highest and the
                    // lowest angle of view are the same, and WhiteboxTools 2.4.0 then calls the tip
                    // of an inverted cone a footslope (8), not a pit.
                    heights[(row * size) + column] = (float)(500 + (0.5 * ((dx * dx) + (dy * dy))));
                }
            }

            using var band = dataset.GetRasterBand(1);
            band.WriteRaster(0, 0, size, size, heights, size, size, 0, 0);
            dataset.FlushCache();
        }

        var tools = new LocalWhiteboxTools(
            Options.Create(new WhiteboxOptions { Mode = "local", WhiteboxPath = WhiteboxFactAttribute.Program ?? "" }),
            NullLogger<LocalWhiteboxTools>.Instance);
        tools.IsAvailable.ShouldBeTrue(tools.UnavailableReason);
        tools.Version.ShouldNotBeNull().ShouldStartWith("WhiteboxTools ");

        var result = await tools.RunAsync(
            new WhiteboxRunRequest(run, "Geomorphons",
            [
                WhiteboxArgument.Input("dem", "dem.tif"),
                WhiteboxArgument.Output("output", "forms.tif"),
                WhiteboxArgument.Number("search", 15),
                WhiteboxArgument.Number("threshold", 0),
                WhiteboxArgument.Switch("forms"),
            ], TimeSpan.FromMinutes(2)),
            CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.OutputTail);
        using var forms = Gdal.Open(Path.Combine(run, "forms.tif"), Access.GA_ReadOnly);
        forms.RasterXSize.ShouldBe(size);
        var classes = new double[size * size];
        using (var band = forms.GetRasterBand(1))
        {
            band.ReadRaster(0, 0, size, size, classes, size, size, 0, 0);
        }

        classes[((size / 2) * size) + (size / 2)].ShouldBe(10);
        var inside = classes.Where((_, i) => i / size is > 0 and < size - 1 && i % size is > 0 and < size - 1);
        inside.ShouldAllBe(c => c >= 1 && c <= 10);
    }

    private WhiteboxRunRequest Request() =>
        new(run, "Geomorphons", [WhiteboxArgument.Input("dem", "dem.tif"), WhiteboxArgument.Output("output", "forms.tif")], TimeSpan.FromMinutes(1));

    /// <summary>Local mode with the given program, sandboxed where bubblewrap is installed.</summary>
    private static LocalWhiteboxTools Local(string program)
    {
        var sandboxed = OperatingSystem.IsLinux() && ExecutableSearch.OnPath("bwrap") is not null;
        var tools = new LocalWhiteboxTools(
            Options.Create(new WhiteboxOptions { Mode = "local", WhiteboxPath = program, AllowUnsandboxed = !sandboxed }),
            NullLogger<LocalWhiteboxTools>.Instance);
        tools.IsAvailable.ShouldBeTrue(tools.UnavailableReason);
        return tools;
    }

    private static ServiceProvider Services(string? mode, string? url, bool register = true)
    {
        var values = new Dictionary<string, string?>();
        if (mode is not null)
        {
            values["Analysis:Whitebox:Mode"] = mode;
        }

        if (url is not null)
        {
            values["Analysis:Whitebox:Url"] = url;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (register)
        {
            services.AddWhiteboxTools(configuration);
        }
        else
        {
            services.AddOptions<WhiteboxOptions>().Bind(configuration.GetSection(WhiteboxOptions.SectionName));
            services.AddSingleton<IValidateOptions<WhiteboxOptions>, WhiteboxOptionsValidator>();
        }

        return services.BuildServiceProvider();
    }

    private string Script(string name, string body)
    {
        var directory = Path.Combine(root, "bin-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static bool Windowed(IReadOnlyList<string> arguments, params string[] window) =>
        Enumerable.Range(0, arguments.Count - window.Length + 1).Any(i => arguments.Skip(i).Take(window.Length).SequenceEqual(window));
}
