// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` at design time — never connects to a real database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SilexGisDbContext>
{
    public SilexGisDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql("Host=localhost;Database=silexgis_design_time", o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options);
}
