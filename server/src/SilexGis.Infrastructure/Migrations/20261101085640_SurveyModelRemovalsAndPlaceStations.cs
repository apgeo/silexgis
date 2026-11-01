using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SurveyModelRemovalsAndPlaceStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "place_station_id",
                table: "import_batch_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sync_survey_model_removals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    survey_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cave_feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_survey_model_removals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_items_place_station_id",
                table: "import_batch_items",
                column: "place_station_id",
                filter: "place_station_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_sync_survey_model_removals_cave_removed",
                table: "sync_survey_model_removals",
                columns: new[] { "cave_feature_id", "removed_at", "survey_model_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_res_links_place_station_id",
                table: "import_batch_items",
                column: "place_station_id",
                principalTable: "res_links",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // The record of every survey model row that leaves the table, by whichever path: a
            // person deleting it, and the cascade when its cave is removed for good. A record written
            // by each path would be one the next path forgets; the trigger sees them all. Rows
            // already in the table are untouched: only removals from now on are recorded.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.sync_record_survey_model_removal() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO public.sync_survey_model_removals (survey_model_id, cave_feature_id, removed_at)
                    VALUES (OLD.id, OLD.cave_feature_id, clock_timestamp());
                    RETURN NULL;
                END;
                $$;
                """);
            migrationBuilder.Sql(
                "CREATE TRIGGER survey_models_record_removal AFTER DELETE ON public.survey_models "
                + "FOR EACH ROW EXECUTE FUNCTION public.sync_record_survey_model_removal();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS survey_models_record_removal ON public.survey_models;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.sync_record_survey_model_removal();");

            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_res_links_place_station_id",
                table: "import_batch_items");

            migrationBuilder.DropTable(
                name: "sync_survey_model_removals");

            migrationBuilder.DropIndex(
                name: "ix_import_batch_items_place_station_id",
                table: "import_batch_items");

            migrationBuilder.DropColumn(
                name: "place_station_id",
                table: "import_batch_items");
        }
    }
}
