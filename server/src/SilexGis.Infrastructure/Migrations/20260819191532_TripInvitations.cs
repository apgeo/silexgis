using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response = table.Column<short>(type: "smallint", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_trip_invitations_cavers_caver_id",
                        column: x => x.caver_id,
                        principalTable: "cavers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_invitations_trip_logs_trip_log_id",
                        column: x => x.trip_log_id,
                        principalTable: "trip_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trip_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_trip_invitations_users_responded_by_user_id",
                        column: x => x.responded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_caver_id",
                table: "trip_invitations",
                column: "caver_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_invited_by_user_id",
                table: "trip_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_responded_by_user_id",
                table: "trip_invitations",
                column: "responded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id",
                table: "trip_invitations",
                column: "trip_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_invitations");
        }
    }
}
