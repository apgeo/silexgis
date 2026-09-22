#!/usr/bin/env sh
# Restore a SilexGIS backup produced by backup.sh into a running Docker Compose stack.
#
# Usage (from the deploy/ directory):
#   sh scripts/restore.sh ./backups/<timestamp>
#
# WARNING: this overwrites the current database and files volume. Stop the api service first
# so nothing is writing during the restore:
#   docker compose stop api
#   sh scripts/restore.sh ./backups/<timestamp>
#   docker compose up -d
set -eu

SRC="${1:?usage: restore.sh <backup-dir>}"
SRC_ABS=$(cd "$SRC" && pwd)
[ -f "$SRC_ABS/db.sql.gz" ] || { echo "missing $SRC_ABS/db.sql.gz" >&2; exit 1; }
[ -f "$SRC_ABS/files.tar.gz" ] || { echo "missing $SRC_ABS/files.tar.gz" >&2; exit 1; }

echo "==> Restoring database from $SRC_ABS/db.sql.gz"
# Drop and recreate the public schema, then load the dump.
docker compose exec -T db psql -U silexgis -d silexgis \
	-c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
gunzip -c "$SRC_ABS/db.sql.gz" | docker compose exec -T db psql -U silexgis -d silexgis

echo "==> Restoring files volume from $SRC_ABS/files.tar.gz"
# The volume name is <project>_<volume>, and the project is asked of the resolved compose
# config rather than assumed: on a host running more than one installation the projects differ
# by COMPOSE_PROJECT_NAME, and a restore that assumed the shipped name would erase the
# neighbouring installation's uploads.
PROJECT=$(docker compose config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])')
docker run --rm \
	-v "${PROJECT}_silexgis-files":/data \
	-v "$SRC_ABS":/backup:ro \
	alpine sh -c 'rm -rf /data/* && tar xzf /backup/files.tar.gz -C /data'

echo "==> Done. Start the stack: docker compose up -d"
