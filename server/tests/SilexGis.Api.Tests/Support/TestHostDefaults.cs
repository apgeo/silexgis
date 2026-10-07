// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SilexGis.Api.Tests.Support;

internal static class TestHostDefaults
{
    /// <summary>
    /// Stops every host this process builds from watching its configuration files for changes.
    ///
    /// A host boot opens an inotify instance per configuration source and holds it until the host
    /// is collected. The number of instances is capped per user
    /// (<c>fs.inotify.max_user_instances</c>), and a run that builds hosts concurrently multiplies
    /// the demand by the number of classes in flight. Once the cap is reached,
    /// <c>WebApplication.CreateBuilder</c> throws <c>IOException: The configured user limit … on
    /// the number of inotify instances has been reached</c> — every test in the class failing in
    /// its constructor, before any assertion, which reads as a broken class rather than as a
    /// resource limit. Editors and file watchers on the same machine share that budget, so the
    /// headroom is not this process's to predict. Nothing under test edits its own configuration
    /// mid-run, so the watching buys nothing.
    ///
    /// The double underscore is the environment-variable spelling of the nested key and is
    /// required; <c>DOTNET_hostBuilder:reloadConfigOnChange</c> is silently ignored.
    /// </summary>
    [ModuleInitializer]
    internal static void DisableConfigurationReload() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

    /// <summary>
    /// Takes the Testcontainers reaper out of the run, and takes responsibility for the container
    /// instead.
    ///
    /// <para>
    /// Ryuk removes a session's containers ten seconds after its client connection stops
    /// answering. That is a sound default for a suite that finishes in a minute on an idle
    /// machine, and a poor one here: this suite builds a host per test across eight threads on a
    /// box that routinely carries other people's suites, and a keepalive starved past ten seconds
    /// costs the whole run its database. It went four times: the visible result was a container
    /// removed — not stopped, removed — mid-run, and every remaining test failing on a socket. An
    /// unlabelled container started beside one of those runs was untouched, so the removal follows
    /// the Testcontainers label rather than the machine being short of anything.
    /// </para>
    /// <para>
    /// With the reaper gone nothing else would ever stop the container, so this stops it at
    /// process exit. A run killed outright still leaves one behind — one per killed run, named
    /// like any Testcontainers container and removed by the usual prune — which is the price of
    /// not having a watchdog that can shoot the run it is guarding.
    /// </para>
    /// </summary>
    [ModuleInitializer]
    internal static void OwnTheContainerRatherThanTheReaper()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // First, because it is quick and the next thing is not: the runner gives a finished
            // test process only a moment to leave, and stopping a container can outlast it.
            SayHowManyApplicationsWereBuilt();
            PostgresFixture.StopContainer();
        };
    }

    /// <summary>
    /// Leaves behind, where <c>SILEXGIS_TEST_HOST_STATS</c> names a file, how many applications
    /// this process built and how many times a test was handed one already running.
    ///
    /// <para>
    /// The two modes cannot be told apart from a run's result: fast mode that shared nothing — a
    /// misspelt switch, a change that made every factory look different from the last — is simply
    /// a slow green run. These two numbers are what says the mode did what it was asked, and the
    /// runner that deals the suite into several processes adds them up.
    /// </para>
    /// </summary>
    private static void SayHowManyApplicationsWereBuilt()
    {
        var path = Environment.GetEnvironmentVariable("SILEXGIS_TEST_HOST_STATS");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            // Why each application was built, beside the two totals, and the same per class: in
            // fast mode this is the list of what did not share, and so of where the time still goes.
            var account = new
            {
                mode = TestMode.Fast ? "fast" : "precise",
                built = SharedHosts.Built,
                borrowed = SharedHosts.Borrowed,
                stoppedEarly = SharedHosts.StoppedEarly,
                because = SharedHosts.BuiltBecause.OrderByDescending(reason => reason.Value)
                    .ToDictionary(reason => reason.Key, reason => reason.Value),
                classes = SharedHosts.ByClass.OrderBy(made => made.Key, StringComparer.Ordinal).ToDictionary(
                    made => made.Key,
                    made => new
                    {
                        built = made.Value.Built,
                        borrowed = made.Value.Borrowed,
                        because = made.Value.Because.ToDictionary(reason => reason.Key, reason => reason.Value),
                    }),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(account) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A number nobody could write down; not a reason to fail a run that has finished.
        }
    }
}
