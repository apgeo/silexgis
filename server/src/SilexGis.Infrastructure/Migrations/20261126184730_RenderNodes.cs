using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenderNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "render_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    worker = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    blender_versions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    free_disk_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_render_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_render_nodes_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "flyby_renders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    processing_job_id = table.Column<long>(type: "bigint", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plan = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    container = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    bundle_path = table.Column<string>(type: "text", nullable: true),
                    bundle_size = table.Column<long>(type: "bigint", nullable: false),
                    bundle_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    phase = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    seconds_left = table.Column<double>(type: "double precision", nullable: true),
                    seconds_per_frame = table.Column<double>(type: "double precision", nullable: true),
                    device = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    render_seconds = table.Column<double>(type: "double precision", nullable: true),
                    frame_count = table.Column<int>(type: "integer", nullable: true),
                    notices = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flyby_renders", x => x.id);
                    table.ForeignKey(
                        name: "fk_flyby_renders_processing_jobs_processing_job_id",
                        column: x => x.processing_job_id,
                        principalTable: "processing_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_flyby_renders_render_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "render_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_flyby_renders_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flyby_render_files",
                columns: table => new
                {
                    render_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flyby_render_files", x => new { x.render_id, x.name });
                    table.ForeignKey(
                        name: "fk_flyby_render_files_flyby_renders_render_id",
                        column: x => x.render_id,
                        principalTable: "flyby_renders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_flyby_renders_expires_at",
                table: "flyby_renders",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_flyby_renders_node_id",
                table: "flyby_renders",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_flyby_renders_processing_job_id",
                table: "flyby_renders",
                column: "processing_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flyby_renders_requested_by",
                table: "flyby_renders",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_render_nodes_created_by",
                table: "render_nodes",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_render_nodes_token_hash",
                table: "render_nodes",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_render_nodes_name_active",
                table: "render_nodes",
                column: "name",
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flyby_render_files");

            migrationBuilder.DropTable(
                name: "flyby_renders");

            migrationBuilder.DropTable(
                name: "render_nodes");
        }
    }
}
