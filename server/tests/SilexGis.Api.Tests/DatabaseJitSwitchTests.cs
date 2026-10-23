// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation switch that turns PostgreSQL's JIT compiler off for every connection the
/// application opens — and leaves it as the server has it by default.
/// </summary>
public sealed class DatabaseJitSwitchTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task By_default_a_connection_keeps_the_server_s_jit_setting()
    {
        using var factory = new SilexGisApiFactory(postgres.ConnectionString);
        (await JitAsync(factory)).ShouldBe("on");
    }

    [Fact]
    public async Task Switched_off_every_connection_runs_without_jit_for_the_raw_statements_and_for_ef_alike()
    {
        using var factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string?> { ["Db:DisableJit"] = "true" },
            ownHost: true);

        (await JitAsync(factory)).ShouldBe("off");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        (await db.Database.SqlQueryRaw<string>("SELECT current_setting('jit') AS \"Value\"").SingleAsync()).ShouldBe("off");
    }

    [Fact]
    public void The_switch_keeps_options_already_in_the_connection_string()
    {
        DependencyInjection.WithJitSwitch("Host=h;Database=d", disableJit: false).ShouldBe("Host=h;Database=d");
        DependencyInjection.WithJitSwitch("Host=h;Database=d", disableJit: true).ShouldContain("-c jit=off");
        var both = DependencyInjection.WithJitSwitch("Host=h;Database=d;Options=-c search_path=public", disableJit: true);
        both.ShouldContain("search_path=public");
        both.ShouldContain("-c jit=off");
    }

    private static async Task<string> JitAsync(SilexGisApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            return await connection.ExecuteScalarAsync<string>("SHOW jit") ?? string.Empty;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
