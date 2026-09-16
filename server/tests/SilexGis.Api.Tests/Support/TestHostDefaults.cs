// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;

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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => PostgresFixture.StopContainer();
    }
}
