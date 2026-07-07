// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SilexGis.Api.Tests.Support;

/// <summary>Boots the real application against the test PostGIS container (migrations + seed run on start).</summary>
public sealed class SilexGisApiFactory(string connectionString, IDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Db:ConnectionString", connectionString);
        builder.UseSetting("Db:AutoMigrate", "true");
        // The OIDC client registration is (re)seeded from PublicUrl on startup and the DB is
        // shared across factories — every test factory must use the TestServer origin.
        builder.UseSetting("PublicUrl", "http://localhost");
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
