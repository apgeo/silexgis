using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CavesCarryAFeatureType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_features_feature_types_feature_type_id",
                table: "features");

            migrationBuilder.DropIndex(
                name: "ix_features_feature_type_id",
                table: "features");

            migrationBuilder.DropCheckConstraint(
                name: "ck_features_generic_type",
                table: "features");

            migrationBuilder.AddColumn<short>(
                name: "bound_kind",
                table: "feature_types",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_feature_types_id_bound_kind",
                table: "feature_types",
                columns: new[] { "id", "bound_kind" });

            // Every existing row survives this migration: caves gain a type by an UPDATE, nothing
            // is dropped and recreated, and the one deletion below removes a taxonomy row only
            // where nothing refers to it.
            //
            // A type an installation made itself under the code the shipped cave type now takes is
            // kept under a new code, rows and all, rather than turned into the type every cave
            // carries: its rows are generic features, and binding it to caves would orphan them.
            migrationBuilder.Sql("""
                UPDATE feature_types SET code = left(code, 40) || '_' || id::text
                WHERE code = 'cave';
                """);

            // The cave type, at the sort order the seeder gives it (its position in the shipped
            // list), so a database migrated here and one seeded fresh agree about the palette's
            // order. Category underground, point-shaped, snapped when protected, needing no
            // parent; its properties schema is written by the seeder on the next start, which
            // fills a schema nobody has set.
            migrationBuilder.Sql("""
                INSERT INTO feature_types (
                    code, name, sort_order, category, accepted_geometry_classes, protected_display,
                    requires_parent, properties_schema_version, bound_kind, created_at, updated_at)
                VALUES ('cave', 'Cave', 320, 1, '{0,3}', 0, false, 1, 1, now(), now());
                """);

            // Every cave, deleted ones included (a restored cave must come back with its type).
            migrationBuilder.Sql("""
                UPDATE features
                SET feature_type_id = (SELECT id FROM feature_types WHERE code = 'cave' AND bound_kind = 1)
                WHERE kind = 1 AND feature_type_id IS NULL;
                """);

            // The surface kind "pit" meant what the cave classification "Pit / Aven" means. It is
            // removed where nothing uses it — no feature, deleted or not, and no permission entry —
            // and otherwise left where it is, as one of the installation's own kinds.
            migrationBuilder.Sql("""
                DELETE FROM feature_types t
                WHERE t.code = 'pit'
                  AND NOT EXISTS (SELECT 1 FROM features f WHERE f.feature_type_id = t.id)
                  AND NOT EXISTS (SELECT 1 FROM access_entries a WHERE a.feature_type_id = t.id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_features_feature_type_id_kind",
                table: "features",
                columns: new[] { "feature_type_id", "kind" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_features_generic_type",
                table: "features",
                sql: "kind <> 0 OR feature_type_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_features_feature_types_feature_type_id_kind",
                table: "features",
                columns: new[] { "feature_type_id", "kind" },
                principalTable: "feature_types",
                principalColumns: new[] { "id", "bound_kind" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_features_feature_types_feature_type_id_kind",
                table: "features");

            migrationBuilder.DropIndex(
                name: "ix_features_feature_type_id_kind",
                table: "features");

            migrationBuilder.DropCheckConstraint(
                name: "ck_features_generic_type",
                table: "features");

            // Back to "only generic rows carry a type": caves give theirs up, and the cave type
            // goes once nothing refers to it. A retired "pit" is not brought back; the seeder no
            // longer ships it.
            migrationBuilder.Sql("UPDATE features SET feature_type_id = NULL WHERE kind <> 0;");
            migrationBuilder.Sql("DELETE FROM feature_types WHERE bound_kind <> 0;");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_feature_types_id_bound_kind",
                table: "feature_types");

            migrationBuilder.DropColumn(
                name: "bound_kind",
                table: "feature_types");

            migrationBuilder.CreateIndex(
                name: "ix_features_feature_type_id",
                table: "features",
                column: "feature_type_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_features_generic_type",
                table: "features",
                sql: "(kind = 0) = (feature_type_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_features_feature_types_feature_type_id",
                table: "features",
                column: "feature_type_id",
                principalTable: "feature_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
