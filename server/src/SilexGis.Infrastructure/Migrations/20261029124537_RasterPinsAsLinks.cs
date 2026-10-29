using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SilexGis.Domain;
using SilexGis.Domain.ResLinks;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RasterPinsAsLinks : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Every pin a phone sent becomes a place-pin link under the pin's own identifier, live and
        /// removed alike, with its times as they were — the times are the revisions the phones hold.
        /// The table the pins were kept in is renamed and left read-only as a copy for one release;
        /// a later update drops it. One transaction: a database either has its pins as links or as it
        /// had them.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_raster_pins_raster_pin_id",
                table: "import_batch_items");

            migrationBuilder.DropIndex(
                name: "ix_raster_maps_document_id",
                table: "raster_maps");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "res_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_res_links_deleted_at",
                table: "res_links",
                column: "deleted_at",
                filter: "deleted_at is not null");

            // A pin's map is found again through its picture, so a picture is one map's at most. A
            // database where two maps share one fails here, loudly, rather than moving pins onto the
            // wrong map.
            migrationBuilder.CreateIndex(
                name: "ux_raster_maps_document_id",
                table: "raster_maps",
                column: "document_id",
                unique: true);

            // The relation the pins move into, ahead of the seeder, which runs after migrations. Its
            // sort order is the one the seeder gives it from its place in the shipped list, so a
            // database moved here and a fresh one agree.
            var position = ResLinkRelationTypeSeeds.All
                .Select((seed, index) => (seed, index))
                .Single(x => x.seed.Code == ResLinkRelationTypeSeeds.MapPlacePointCode);
            migrationBuilder.Sql(FormattableString.Invariant($"""
                INSERT INTO res_link_relation_types (code, name, sort_order, directed, inverse_name, created_at, updated_at)
                VALUES ('{position.seed.Code}', '{position.seed.Name}', {ShippedVocabularyOrder.Of(position.index)}, TRUE,
                        '{position.seed.InverseName}', now(), now())
                ON CONFLICT (code) DO NOTHING;
                """));

            // One link per pin: the map's picture anchored at the pixel, measured on the original
            // file of the picture's current revision, and the place. The fraction the browser draws
            // the point at is not worked out here — the application does that at start, with the
            // one conversion there is — so the payload carries the pixel alone until then. Created
            // by the account whose upload brought the map. A short code drawn from random bytes is
            // drawn again on the rare collision.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    relation bigint;
                    pin record;
                    picture uuid;
                    drawn text;
                    alphabet constant text := '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';
                    failed text;
                BEGIN
                    SELECT t.id INTO relation FROM res_link_relation_types t WHERE t.code = 'map-place-point';
                    FOR pin IN
                        SELECT p.id, p.place_feature_id, p.x, p.y, p.created_at, p.updated_at, p.deleted_at,
                               m.document_id, m.owner_user_id
                        FROM raster_pins p
                        JOIN raster_maps m ON m.id = p.raster_map_id
                    LOOP
                        SELECT f.id INTO picture
                        FROM document_versions v
                        JOIN files f ON f.document_version_id = v.id
                        WHERE v.document_id = pin.document_id AND v.is_current
                        ORDER BY f.created_at, f.id
                        LIMIT 1;

                        LOOP
                            drawn := '';
                            FOR i IN 1..8 LOOP
                                drawn := drawn || substr(alphabet, 1 + get_byte(uuid_send(gen_random_uuid()), 0) % 62, 1);
                            END LOOP;
                            BEGIN
                                INSERT INTO res_links (id, short_code, relation_type_id, description, created_by,
                                                       created_at, updated_at, deleted_at)
                                VALUES (pin.id, drawn, relation, NULL, pin.owner_user_id,
                                        pin.created_at, pin.updated_at, pin.deleted_at);
                                EXIT;
                            EXCEPTION WHEN unique_violation THEN
                                GET STACKED DIAGNOSTICS failed = CONSTRAINT_NAME;
                                IF failed <> 'ix_res_links_short_code' THEN
                                    RAISE;
                                END IF;
                            END;
                        END LOOP;

                        INSERT INTO res_link_members (id, res_link_id, feature_id, entity_type, entity_id, is_main,
                                                      anchor_kind, anchor, anchor_file_id, sort_order, note, added_by,
                                                      created_at, updated_at)
                        VALUES (gen_random_uuid(), pin.id, NULL, 9, pin.document_id, TRUE,
                                4, jsonb_build_object('shape', 'point', 'px', pin.x, 'py', pin.y), picture, 0, NULL,
                                pin.owner_user_id, pin.created_at, pin.updated_at);
                        INSERT INTO res_link_members (id, res_link_id, feature_id, entity_type, entity_id, is_main,
                                                      anchor_kind, anchor, anchor_file_id, sort_order, note, added_by,
                                                      created_at, updated_at)
                        VALUES (gen_random_uuid(), pin.id, pin.place_feature_id, NULL, NULL, FALSE,
                                0, NULL, NULL, 1, NULL,
                                pin.owner_user_id, pin.created_at, pin.updated_at);
                    END LOOP;
                END
                $$;
                """);

            // The lines that say an upload sent a pin name it by the same identifier, now a link's.
            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_res_links_raster_pin_id",
                table: "import_batch_items",
                column: "raster_pin_id",
                principalTable: "res_links",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Kept, read-only and read by nothing, as a copy for one release, with its keys and
            // indexes. A later update drops it.
            migrationBuilder.RenameTable(
                name: "raster_pins",
                newName: "raster_pins_moved_to_links");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batch_items_res_links_raster_pin_id",
                table: "import_batch_items");

            migrationBuilder.RenameTable(
                name: "raster_pins_moved_to_links",
                newName: "raster_pins");

            migrationBuilder.Sql("""
                DELETE FROM res_links
                WHERE relation_type_id = (SELECT id FROM res_link_relation_types WHERE code = 'map-place-point');
                """);

            migrationBuilder.DropIndex(
                name: "ix_res_links_deleted_at",
                table: "res_links");

            migrationBuilder.DropIndex(
                name: "ux_raster_maps_document_id",
                table: "raster_maps");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "res_links");

            migrationBuilder.CreateIndex(
                name: "ix_raster_maps_document_id",
                table: "raster_maps",
                column: "document_id");

            migrationBuilder.AddForeignKey(
                name: "fk_import_batch_items_raster_pins_raster_pin_id",
                table: "import_batch_items",
                column: "raster_pin_id",
                principalTable: "raster_pins",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
