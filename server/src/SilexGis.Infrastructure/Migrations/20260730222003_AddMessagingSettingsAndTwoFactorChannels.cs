using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SilexGis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingSettingsAndTwoFactorChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_phone_number",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_phone_requested_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "preferred_two_factor_method",
                table: "users",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_authenticator_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_email_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_sms_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_key_locale",
                table: "message_templates",
                columns: new[] { "key", "locale" },
                unique: true);

            // Until now the single two_factor_enabled flag could only ever have meant an
            // authenticator app, because that was the only second factor. Without this the flag
            // would survive with no method behind it and everyone who had set up two-factor would
            // be pushed onto their recovery codes at the next sign-in.
            migrationBuilder.Sql(
                """
                UPDATE users
                   SET two_factor_authenticator_enabled = TRUE
                 WHERE two_factor_enabled = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropColumn(
                name: "pending_phone_number",
                table: "users");

            migrationBuilder.DropColumn(
                name: "pending_phone_requested_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "preferred_two_factor_method",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_authenticator_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_email_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_sms_enabled",
                table: "users");
        }
    }
}
