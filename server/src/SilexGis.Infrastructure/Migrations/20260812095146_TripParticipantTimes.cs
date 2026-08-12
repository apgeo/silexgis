using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripParticipantTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "entry_time",
                table: "trip_log_participants",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "exit_time",
                table: "trip_log_participants",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "trip_log_participants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entry_time",
                table: "trip_log_participants");

            migrationBuilder.DropColumn(
                name: "exit_time",
                table: "trip_log_participants");

            migrationBuilder.DropColumn(
                name: "note",
                table: "trip_log_participants");
        }
    }
}
