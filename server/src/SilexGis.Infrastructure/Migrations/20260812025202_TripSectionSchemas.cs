using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TripSectionSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "field_data_schema",
                table: "trip_types",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "field_data_schema_version",
                table: "trip_types",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "logistics_schema",
                table: "trip_types",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "logistics_schema_version",
                table: "trip_types",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "safety_schema",
                table: "trip_types",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "safety_schema_version",
                table: "trip_types",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "field_data",
                table: "trip_logs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "field_data_schema_version",
                table: "trip_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logistics",
                table: "trip_logs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "logistics_schema_version",
                table: "trip_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safety",
                table: "trip_logs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "safety_schema_version",
                table: "trip_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trip_type_schemas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trip_type_id = table.Column<long>(type: "bigint", nullable: false),
                    section = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_type_schemas", x => x.id);
                    table.CheckConstraint("ck_trip_type_schemas_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_trip_type_schemas_trip_types_trip_type_id",
                        column: x => x.trip_type_id,
                        principalTable: "trip_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trip_type_schemas_trip_type_id_section_version",
                table: "trip_type_schemas",
                columns: new[] { "trip_type_id", "section", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_type_schemas");

            migrationBuilder.DropColumn(
                name: "field_data_schema",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "field_data_schema_version",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "logistics_schema",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "logistics_schema_version",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "safety_schema",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "safety_schema_version",
                table: "trip_types");

            migrationBuilder.DropColumn(
                name: "field_data",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "field_data_schema_version",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "logistics",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "logistics_schema_version",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "safety",
                table: "trip_logs");

            migrationBuilder.DropColumn(
                name: "safety_schema_version",
                table: "trip_logs");
        }
    }
}
