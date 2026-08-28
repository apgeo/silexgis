using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripInvitationEventSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "trip_log_id",
                table: "trip_invitations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "trip_invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_event_id",
                table: "trip_invitations",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_event_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "event_id", "caver_id" },
                unique: true,
                filter: "event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true,
                filter: "trip_log_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trip_invitations_one_subject",
                table: "trip_invitations",
                sql: "(trip_log_id IS NOT NULL AND event_id IS NULL) OR (trip_log_id IS NULL AND event_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_trip_invitations_events_event_id",
                table: "trip_invitations",
                column: "event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_invitations_events_event_id",
                table: "trip_invitations");

            migrationBuilder.DropIndex(
                name: "ix_trip_invitations_event_id",
                table: "trip_invitations");

            migrationBuilder.DropIndex(
                name: "ix_trip_invitations_event_id_caver_id",
                table: "trip_invitations");

            migrationBuilder.DropIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trip_invitations_one_subject",
                table: "trip_invitations");

            migrationBuilder.DropColumn(
                name: "event_id",
                table: "trip_invitations");

            migrationBuilder.AlterColumn<Guid>(
                name: "trip_log_id",
                table: "trip_invitations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_invitations_trip_log_id_caver_id",
                table: "trip_invitations",
                columns: new[] { "trip_log_id", "caver_id" },
                unique: true);
        }
    }
}
