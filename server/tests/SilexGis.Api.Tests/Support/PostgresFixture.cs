// SPDX-License-Identifier: AGPL-3.0-or-later
using Testcontainers.PostgreSql;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// One PostGIS container for the whole test collection. The image matches
/// the deployment database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithDatabase("silexgis_test")
        .WithUsername("silexgis")
        .WithPassword("silexgis")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
