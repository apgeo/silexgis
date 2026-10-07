using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovedReportsKept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "trip_position_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "removed_by_user_id",
                table: "trip_position_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_position_events_removed_by_user_id",
                table: "trip_position_events",
                column: "removed_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_trip_position_events_users_removed_by_user_id",
                table: "trip_position_events",
                column: "removed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trip_position_events_users_removed_by_user_id",
                table: "trip_position_events");

            migrationBuilder.DropIndex(
                name: "ix_trip_position_events_removed_by_user_id",
                table: "trip_position_events");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "trip_position_events");

            migrationBuilder.DropColumn(
                name: "removed_by_user_id",
                table: "trip_position_events");
        }
    }
}
