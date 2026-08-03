using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cabinets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries");

            migrationBuilder.CreateTable(
                name: "cabinets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ancestor_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cabinets", x => x.id);
                    table.CheckConstraint("ck_cabinets_no_self_parent", "parent_id <> id");
                    table.ForeignKey(
                        name: "fk_cabinets_cabinets_parent_id",
                        column: x => x.parent_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cabinet_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cabinet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cabinet_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_cabinet_documents_cabinets_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "cabinets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cabinet_documents_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries",
                sql: "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR (scope_kind IN (2, 4, 6) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR (scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_documents_cabinet_id_document_id",
                table: "cabinet_documents",
                columns: new[] { "cabinet_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_documents_document_id",
                table: "cabinet_documents",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_ancestor_ids",
                table: "cabinets",
                column: "ancestor_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_parent_id",
                table: "cabinets",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_parent_name",
                table: "cabinets",
                columns: new[] { "parent_id", "name" },
                unique: true,
                filter: "parent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_path",
                table: "cabinets",
                column: "path")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_cabinets_root_name",
                table: "cabinets",
                column: "name",
                unique: true,
                filter: "parent_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cabinet_documents");

            migrationBuilder.DropTable(
                name: "cabinets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_access_entries_scope_anchor",
                table: "access_entries",
                sql: "(scope_kind IN (0, 1) AND scope_feature_id IS NULL AND scope_id IS NULL) OR (scope_kind IN (2, 4) AND scope_feature_id IS NULL AND scope_id IS NOT NULL) OR (scope_kind = 3 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (scope_kind = 5 AND ((domain = 0 AND scope_feature_id IS NOT NULL AND scope_id IS NULL) OR (domain <> 0 AND scope_feature_id IS NULL AND scope_id IS NOT NULL)))");
        }
    }
}
