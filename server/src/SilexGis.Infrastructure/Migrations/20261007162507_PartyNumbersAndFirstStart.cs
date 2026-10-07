using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartyNumbersAndFirstStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_armed_at",
                table: "trip_tracking",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trip_party_numbers",
                columns: table => new
                {
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_party_numbers", x => new { x.trip_log_id, x.number });
                    table.CheckConstraint("ck_trip_party_numbers_number_from_one", "number >= 1");
                    table.ForeignKey(
                        name: "fk_trip_party_numbers_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_party_numbers_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_party_numbers_caver_id",
                table: "trip_party_numbers",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ux_trip_party_numbers_trip_caver",
                table: "trip_party_numbers",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);

            // Every party keeps the numbers it has been shown under. Until now a person's number
            // was their rank among the trip's people by the first roster row that named them, so
            // that rank is what is written down — once, for every trip that has a roster.
            //
            // Deleted trips are numbered too, deliberately, which is why neither statement asks
            // whether a trip is deleted: a deleted trip can be put back, and it has to come back
            // with its party numbered as it was rather than waiting for somebody to save its roster.
            migrationBuilder.Sql(
                """
                INSERT INTO trip_party_numbers (trip_log_id, number, caver_id)
                SELECT named.trip_log_id,
                       row_number() OVER (PARTITION BY named.trip_log_id ORDER BY named.first_row_id),
                       named.caver_id
                FROM (
                    SELECT trip_log_id, caver_id, min(id) AS first_row_id
                    FROM trip_log_participants
                    GROUP BY trip_log_id, caver_id
                ) AS named;
                """);

            // The only start a watch has kept until now is its latest one, so that is the earliest
            // start anybody can still vouch for. A watch that was never started — one an import
            // wrote already closed — has none and keeps none.
            migrationBuilder.Sql(
                """
                UPDATE trip_tracking SET first_armed_at = armed_at WHERE armed_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_party_numbers");

            migrationBuilder.DropColumn(
                name: "first_armed_at",
                table: "trip_tracking");
        }
    }
}
