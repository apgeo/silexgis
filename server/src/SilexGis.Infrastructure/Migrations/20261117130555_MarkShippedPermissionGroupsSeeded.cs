using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MarkShippedPermissionGroupsSeeded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shipped permission groups are now seeded once per installation, and a settings row
            // records which ones an installation has been given. An installation that already has
            // permission groups has been through the seeder on every start until now, so it is
            // marked as given all six — including any an operator has since deleted, which is the
            // point: a group deleted before this update must not come back with it. A fresh
            // database has no groups here, gets no row, and the seeder writes it on first start.
            migrationBuilder.Sql("""
                INSERT INTO app_settings (key, value, created_at, updated_at)
                SELECT 'seededPermissionGroups',
                       '["administrators","all-users","caving-group-managers","editors","full-administrators","reviewers"]'::jsonb,
                       now(), now()
                WHERE EXISTS (SELECT 1 FROM permission_groups)
                ON CONFLICT (key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM app_settings WHERE key = 'seededPermissionGroups';");
        }
    }
}
