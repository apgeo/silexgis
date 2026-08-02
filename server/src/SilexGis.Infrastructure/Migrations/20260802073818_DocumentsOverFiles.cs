using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsOverFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caving_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_caving_groups_caving_group_id",
                        column: x => x.caving_group_id,
                        principalTable: "caving_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    change_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_versions", x => x.id);
                    table.CheckConstraint("ck_document_versions_number", "version_number >= 1");
                    table.ForeignKey(
                        name: "fk_document_versions_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_versions_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_pages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_pages", x => x.id);
                    table.CheckConstraint("ck_document_pages_number", "page_number >= 1");
                    table.ForeignKey(
                        name: "fk_document_pages_stored_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_caving_group_id",
                table: "documents",
                column: "caving_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_user_id",
                table: "documents",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_current",
                table: "document_versions",
                column: "document_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_document_id_version_number",
                table: "document_versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_versions_uploaded_by",
                table: "document_versions",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_document_pages_file_id_page_number",
                table: "document_pages",
                columns: new[] { "file_id", "page_number" },
                unique: true);

            // Added nullable so the existing rows can be filled in below, then made required:
            // every stored file belongs to a document revision.
            migrationBuilder.AddColumn<Guid>(
                name: "document_version_id",
                table: "files",
                type: "uuid",
                nullable: true);

            // ---- carry the existing file-level version chains over ----
            // One document per chain, keeping the chain's own identity (it already meant
            // "stable document id", so anything anchored on it keeps working), and one
            // revision per file row, keeping that file's id. Files whose uploader is gone
            // fall back to the installation's oldest account so the owner column can be
            // required; ids are time-ordered, so that is the account created first. A
            // database holding files but no accounts cannot arise, and would fail loudly.
            migrationBuilder.Sql("""
                INSERT INTO documents (id, title, owner_user_id, caving_group_id, visibility, created_at, updated_at)
                SELECT head.version_group_id,
                       left(head.original_name, 300),
                       coalesce(head.uploaded_by, (SELECT u.id FROM users u ORDER BY u.id LIMIT 1)),
                       NULL,
                       0,
                       head.created_at,
                       head.updated_at
                FROM files head
                WHERE head.version_number = (
                    SELECT max(f.version_number) FROM files f
                    WHERE f.version_group_id = head.version_group_id);
                """);

            migrationBuilder.Sql("""
                INSERT INTO document_versions (
                    id, document_id, version_number, is_current, label, change_note,
                    document_date, uploaded_by, created_at, updated_at)
                SELECT f.id,
                       f.version_group_id,
                       f.version_number,
                       f.version_number = (
                           SELECT max(h.version_number) FROM files h
                           WHERE h.version_group_id = f.version_group_id),
                       NULL,
                       NULL,
                       f.document_date,
                       f.uploaded_by,
                       f.created_at,
                       f.updated_at
                FROM files f;
                """);

            migrationBuilder.Sql("UPDATE files SET document_version_id = id;");

            // An image is one page by definition; paged formats wait for text extraction,
            // which is the only thing that knows their real count.
            migrationBuilder.Sql("""
                INSERT INTO document_pages (file_id, page_number)
                SELECT id, 1 FROM files WHERE kind = 0;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "document_version_id",
                table: "files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "fk_files_users_uploaded_by",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_uploaded_by",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files");

            migrationBuilder.DropColumn(
                name: "document_date",
                table: "files");

            migrationBuilder.DropColumn(
                name: "uploaded_by",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_group_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "version_number",
                table: "files");

            migrationBuilder.CreateIndex(
                name: "ix_files_document_version_id",
                table: "files",
                column: "document_version_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_document_versions_document_version_id",
                table: "files",
                column: "document_version_id",
                principalTable: "document_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "version_group_id",
                table: "files",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "version_number",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "document_date",
                table: "files",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "uploaded_by",
                table: "files",
                type: "uuid",
                nullable: true);

            // Fold the version detail back onto the file rows before the tables holding it go.
            migrationBuilder.Sql("""
                UPDATE files f
                SET version_group_id = v.document_id,
                    version_number = v.version_number,
                    document_date = v.document_date,
                    uploaded_by = v.uploaded_by
                FROM document_versions v
                WHERE v.id = f.document_version_id;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_files_document_versions_document_version_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_document_version_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "document_version_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "document_pages");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.CreateIndex(
                name: "ix_files_uploaded_by",
                table: "files",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_files_version_group_id_version_number",
                table: "files",
                columns: new[] { "version_group_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_files_users_uploaded_by",
                table: "files",
                column: "uploaded_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
