using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripReportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "entry_time",
                table: "trip_logs",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "exit_time",
                table: "trip_logs",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organizing_club",
                table: "trip_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "results",
                table: "trip_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "type",
                table: "trip_logs",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weather_conditions",
                table: "trip_logs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "trip_log_participants",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entry_time",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "exit_time",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "organizing_club",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "results",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "type",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "weather_conditions",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "trip_log_participants");
        }
    }
}
