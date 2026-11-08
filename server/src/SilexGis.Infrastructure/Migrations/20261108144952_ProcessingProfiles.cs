using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProcessingProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processing_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    origin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_profiles", x => x.id);
                    table.CheckConstraint("ck_processing_profiles_group_scope", "(scope = 1) = (caving_group_id is not null)");
                    table.CheckConstraint("ck_processing_profiles_personal_owner", "scope <> 2 or owner_user_id is not null");
                    table.ForeignKey(
                        name: "fk_processing_profiles_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_processing_profiles_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scan_step_defaults",
                columns: table => new
                {
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    software_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    native_text = table.Column<string>(type: "text", nullable: true),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scan_step_defaults", x => x.step_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processing_profiles_caving_group_id",
                table: "processing_profiles",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_profiles_owner_user_id",
                table: "processing_profiles",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_profiles_step_scope",
                table: "processing_profiles",
                columns: new[] { "step_id", "scope" });

            // The scan-profiles right is seeded to the Administrators group, but the seeder only
            // ever fills a group it creates: on a database whose groups exist already the new
            // domain would never arrive, and nobody but a full administrator could manage shared
            // processing profiles. So it is carried over here, once, at the moment the domain
            // appears — every action at the whole-installation scope, as the seeder gives the group
            // every other domain — and only where the group exists and holds no entry for the
            // domain yet. On a fresh database the group does not exist yet and the seeder creates
            // it with the entry.
            migrationBuilder.Sql("""
                insert into access_entries (permission_group_id, effect, domain, actions, scope_kind, created_at, updated_at)
                select g.id, 0, 25, 255, 0, now(), now()
                from permission_groups g
                where g.slug = 'administrators'
                  and not exists (
                      select 1 from access_entries e where e.permission_group_id = g.id and e.domain = 25);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("delete from access_entries where domain = 25;");

            migrationBuilder.DropTable(
                name: "processing_profiles");

            migrationBuilder.DropTable(
                name: "scan_step_defaults");
        }
    }
}
