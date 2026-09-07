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
}
