using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anchor_kind = table.Column<short>(type: "smallint", nullable: false),
                    anchor = table.Column<string>(type: "jsonb", nullable: true),
                    anchor_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_comments", x => x.id);
                    table.CheckConstraint("ck_document_comments_anchor_payload", "(anchor_kind = 0 AND anchor IS NULL AND anchor_file_id IS NULL) OR (anchor_kind <> 0 AND anchor IS NOT NULL)");
                    table.CheckConstraint("ck_document_comments_body", "length(btrim(body)) > 0");
                    table.ForeignKey(
                        name: "fk_document_comments_document_comments_parent_id",
                        column: x => x.parent_id,
                        principalTable: "document_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_comments_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_comments_stored_files_anchor_file_id",
                        column: x => x.anchor_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_document_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_anchor_file_id",
                table: "document_comments",
                column: "anchor_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_author_id",
                table: "document_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_document_id_created_at",
                table: "document_comments",
                columns: new[] { "document_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_comments_parent_id",
                table: "document_comments",
                column: "parent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_comments");
        }
    }
}
