using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestDataRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_data_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    profile = table.Column<string>(type: "jsonb", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: false),
                    phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    progress_done = table.Column<int>(type: "integer", nullable: false),
                    progress_total = table.Column<int>(type: "integer", nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: false),
                    notes = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    alongside_existing_data = table.Column<bool>(type: "boolean", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_job_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_data_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_data_rows",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    row_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_data_rows", x => new { x.run_id, x.table_name, x.row_id });
                    table.ForeignKey(
                        name: "fk_test_data_rows_test_data_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "test_data_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_data_rows_run_id_sequence",
                table: "test_data_rows",
                columns: new[] { "run_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_test_data_rows_table_name_row_id",
                table: "test_data_rows",
                columns: new[] { "table_name", "row_id" });

            migrationBuilder.CreateIndex(
                name: "ix_test_data_runs_created_at",
                table: "test_data_runs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_data_rows");

            migrationBuilder.DropTable(
                name: "test_data_runs");
        }
    }
}
