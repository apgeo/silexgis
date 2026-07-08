using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripsAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trip_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    trip_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_date_end = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    location_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_logs_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_logs_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taggings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tag_id = table.Column<long>(type: "bigint", nullable: false),
                    entity_type = table.Column<short>(type: "smallint", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taggings", x => x.id);
                    table.ForeignKey(
                        name: "fk_taggings_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_taggings_users_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "trip_log_caves",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_log_caves", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_log_caves_caves_cave_id",
                        column: x => x.cave_id,
                        principalTable: "caves",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_log_caves_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_log_participants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_log_participants", x => x.id);
                    table.CheckConstraint("ck_trip_log_participants_one_identity", "(user_id IS NULL) <> (name_text IS NULL)");
                    table.ForeignKey(
                        name: "fk_trip_log_participants_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_log_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_taggings_added_by",
                table: "taggings",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_taggings_entity_type_entity_id",
                table: "taggings",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_taggings_tag_id_entity_type_entity_id",
                table: "taggings",
                columns: new[] { "tag_id", "entity_type", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                table: "tags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_cave_id",
                table: "trip_log_caves",
                column: "cave_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_caves_trip_log_id_cave_id",
                table: "trip_log_caves",
                columns: new[] { "trip_log_id", "cave_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_trip_log_id",
                table: "trip_log_participants",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_log_participants_user_id",
                table: "trip_log_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_geom",
                table: "trip_logs",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_owner_user_id",
                table: "trip_logs",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_team_id",
                table: "trip_logs",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_logs_trip_date",
                table: "trip_logs",
                column: "trip_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "taggings");

            migrationBuilder.DropTable(
                name: "trip_log_caves");

            migrationBuilder.DropTable(
                name: "trip_log_participants");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "trip_logs");
        }
    }
}
