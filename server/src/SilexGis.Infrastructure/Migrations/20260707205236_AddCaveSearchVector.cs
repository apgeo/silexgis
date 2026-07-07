using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaveSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Generated columns require IMMUTABLE expressions; the two-argument unaccent
            // (explicit dictionary) is deterministic, so this wrapper is safe to declare
            // IMMUTABLE. Used by the search_vector computed column below.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION immutable_unaccent(text)
                RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                RETURN public.unaccent('public.unaccent'::regdictionary, $1);
                """);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "caves",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', immutable_unaccent(coalesce(name, '') || ' ' || coalesce(other_toponyms, '') || ' ' || coalesce(description, '')))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_caves_search_vector",
                table: "caves",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_caves_search_vector",
                table: "caves");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "caves");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS immutable_unaccent(text);");
        }
    }
}
