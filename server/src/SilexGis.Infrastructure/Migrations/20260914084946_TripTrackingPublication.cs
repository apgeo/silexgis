using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripTrackingPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_tracking_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_participants_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_tracking_participants_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_tracking_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_tracking_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_tracking_shares_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_tracking_shares_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_participants_caver_id",
                table: "trip_tracking_participants",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_participants_trip_log_id_caver_id",
                table: "trip_tracking_participants",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_created_by",
                table: "trip_tracking_shares",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_token_hash",
                table: "trip_tracking_shares",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_tracking_shares_trip_log_id",
                table: "trip_tracking_shares",
                column: "trip_log_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_tracking_participants");

            migrationBuilder.DropTable(
                name: "trip_tracking_shares");
        }
    }
}
