using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncLinkRemovalsAndDeviceDefaultSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "in_device_default_set",
                table: "attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "sync_link_removals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<short>(type: "smallint", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_link_removals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_link_removals_entity",
                table: "sync_link_removals",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_link_removals_feature_removed",
                table: "sync_link_removals",
                columns: new[] { "feature_id", "removed_at", "attachment_id" });

            // The record of every attachment row that leaves the table, by whichever path: a person
            // detaching a file, a revision collapsing duplicate links, a regenerated report, a bulk
            // delete that never loads the rows, and the cascades when a file or a feature row goes.
            // A record written by each path would be one the next path forgets; the trigger sees them
            // all. The document is looked up while the row goes and is absent when its file went in the
            // same statement — nothing reads it as a key.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.sync_record_link_removal() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO public.sync_link_removals
                        (attachment_id, file_id, document_id, feature_id, entity_type, entity_id, removed_at)
                    VALUES (
                        OLD.id,
                        OLD.file_id,
                        (SELECT v.document_id
                           FROM public.files f
                           JOIN public.document_versions v ON v.id = f.document_version_id
                          WHERE f.id = OLD.file_id),
                        OLD.feature_id,
                        OLD.entity_type,
                        OLD.entity_id,
                        clock_timestamp());
                    RETURN NULL;
                END;
                $$;
                """);
            migrationBuilder.Sql(
                "CREATE TRIGGER attachments_record_removal AFTER DELETE ON public.attachments "
                + "FOR EACH ROW EXECUTE FUNCTION public.sync_record_link_removal();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attachments_record_removal ON public.attachments;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.sync_record_link_removal();");

            migrationBuilder.DropTable(
                name: "sync_link_removals");

            migrationBuilder.DropColumn(
                name: "in_device_default_set",
                table: "attachments");
        }
    }
}
