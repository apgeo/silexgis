// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// The statements that bring a database from the oldest step <see cref="MigrationBaseline"/>
/// accepts up to the last step of the chain the baseline replaced.
/// </summary>
/// <remarks>
/// Written out by the migration tool from those steps as an idempotent script: each block runs only
/// when its step is missing from the history, so a database already further along skips what it
/// has. Kept verbatim; it is never edited, only replaced when the accepted range changes.
/// </remarks>
internal static class MigrationBaselineSql
{
    internal const string CatchUp = """
        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016090838_SavedFilters') THEN
            CREATE TABLE saved_filters (
                id uuid NOT NULL,
                name character varying(200) NOT NULL,
                description character varying(2000),
                document jsonb NOT NULL DEFAULT ('{}'::jsonb),
                owner_user_id uuid NOT NULL,
                caving_group_id uuid,
                visibility smallint NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_saved_filters PRIMARY KEY (id),
                CONSTRAINT fk_saved_filters_caving_groups_caving_group_id FOREIGN KEY (caving_group_id) REFERENCES caving_groups (id) ON DELETE SET NULL,
                CONSTRAINT fk_saved_filters_users_owner_user_id FOREIGN KEY (owner_user_id) REFERENCES users (id) ON DELETE CASCADE
            );
            END IF;
        END $EF$;

        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016090838_SavedFilters') THEN
            CREATE INDEX ix_saved_filters_caving_group_id ON saved_filters (caving_group_id);
            END IF;
        END $EF$;

        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016090838_SavedFilters') THEN
            CREATE INDEX ix_saved_filters_owner_user_id ON saved_filters (owner_user_id);
            END IF;
        END $EF$;

        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016090838_SavedFilters') THEN
            INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
            VALUES ('20261016090838_SavedFilters', '10.0.9');
            END IF;
        END $EF$;
        COMMIT;

        START TRANSACTION;

        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016125428_MapLayerOnSharedLinks') THEN
            ALTER TABLE map_layers ADD on_shared_links boolean NOT NULL DEFAULT FALSE;
            END IF;
        END $EF$;

        DO $EF$
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20261016125428_MapLayerOnSharedLinks') THEN
            INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
            VALUES ('20261016125428_MapLayerOnSharedLinks', '10.0.9');
            END IF;
        END $EF$;
        """;

    internal const string HistoryTableExists =
        "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL AS \"Value\"";

    internal const string AppliedIds =
        "SELECT migration_id AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY migration_id";

    internal const string ReplaceHistory = """
        DELETE FROM "__EFMigrationsHistory";
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES (@id, @version);
        """;
}
