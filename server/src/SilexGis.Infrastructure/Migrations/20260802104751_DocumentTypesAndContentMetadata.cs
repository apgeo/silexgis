using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTypesAndContentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "author",
                table: "files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codec",
                table: "files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_created_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_modified_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "duration_seconds",
                table: "files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_count",
                table: "files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "producer",
                table: "files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "document_type_id",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metadata",
                table: "documents",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "metadata_schema_version",
                table: "documents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    metadata_schema = table.Column<string>(type: "jsonb", nullable: true),
                    metadata_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_type_schemas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_type_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_type_schemas", x => x.id);
                    table.CheckConstraint("ck_document_type_schemas_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_document_type_schemas_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_duration_seconds",
                table: "files",
                sql: "duration_seconds is null or duration_seconds >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_page_count",
                table: "files",
                sql: "page_count is null or page_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_documents_document_type_id",
                table: "documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_type_schemas_document_type_id_version",
                table: "document_type_schemas",
                columns: new[] { "document_type_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_types_code",
                table: "document_types",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_documents_document_types_document_type_id",
                table: "documents",
                column: "document_type_id",
                principalTable: "document_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Page count is promoted out of derived data into a column, so files whose pages
            // are already known get the count they always had. Taken from the page rows
            // themselves rather than assumed, so the column and the rows agree from the start.
            migrationBuilder.Sql(
                """
                UPDATE files f
                SET page_count = counted.pages
                FROM (SELECT file_id, count(*) AS pages FROM document_pages GROUP BY file_id) counted
                WHERE counted.file_id = f.id AND f.page_count IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documents_document_types_document_type_id",
                table: "documents");

            migrationBuilder.DropTable(
                name: "document_type_schemas");

            migrationBuilder.DropTable(
                name: "document_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_duration_seconds",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_page_count",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_documents_document_type_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "author",
                table: "files");

            migrationBuilder.DropColumn(
                name: "codec",
                table: "files");

            migrationBuilder.DropColumn(
                name: "content_created_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "content_modified_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "duration_seconds",
                table: "files");

            migrationBuilder.DropColumn(
                name: "page_count",
                table: "files");

            migrationBuilder.DropColumn(
                name: "producer",
                table: "files");

            migrationBuilder.DropColumn(
                name: "document_type_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "metadata",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "metadata_schema_version",
                table: "documents");
        }
    }
}
