#!/usr/bin/env sh
# Restore a SilexGIS backup produced by backup.sh into a running Docker Compose stack.
#
# Usage (from the deploy/ directory):
#   sh scripts/restore.sh ./backups/<timestamp>
#
# WARNING: this overwrites the current database and files volume (and, when the backup holds
# them, the browser-uploaded terrain rasters). Stop the api service first
# so nothing is writing during the restore:
#   docker compose stop api
#   sh scripts/restore.sh ./backups/<timestamp>
#   docker compose up -d
set -eu

SRC="${1:?usage: restore.sh <backup-dir>}"
SRC_ABS=$(cd "$SRC" && pwd)

fail() {
	echo "!! $*" >&2
	exit 1
}

[ -f "$SRC_ABS/db.sql.gz" ] || fail "missing $SRC_ABS/db.sql.gz"
[ -f "$SRC_ABS/files.tar.gz" ] || fail "missing $SRC_ABS/files.tar.gz"

# Everything below destroys what is there now, so every archive is read back in full first and a
# backup that cannot be restored whole is refused while the installation is still untouched. A copy
# cut short in transfer, or a dump that stopped halfway, would otherwise load as far as it goes with
# no error at all, after the schema and the uploads had already been removed.
echo "==> Checking $SRC_ABS before anything is changed"
gzip -t "$SRC_ABS/db.sql.gz" || fail "db.sql.gz does not decompress -- nothing was changed"
# pg_dump writes this line only after the last object, so a dump without it is incomplete.
gzip -dc "$SRC_ABS/db.sql.gz" | tail -n 20 | grep -q '^-- PostgreSQL database dump complete$' \
	|| fail "db.sql.gz is incomplete: it does not end with pg_dump's completion line -- nothing was changed"
tar -tzf "$SRC_ABS/files.tar.gz" > /dev/null || fail "files.tar.gz does not read back -- nothing was changed"
if [ -f "$SRC_ABS/terrain-uploads.tar.gz" ]; then
	tar -tzf "$SRC_ABS/terrain-uploads.tar.gz" > /dev/null \
		|| fail "terrain-uploads.tar.gz does not read back -- nothing was changed"
fi
# The volume name is <project>_<volume>, and the project is asked of the resolved compose
# config rather than assumed: on a host running more than one installation the projects differ
# by COMPOSE_PROJECT_NAME, and a restore that assumed the shipped name would erase the
# neighbouring installation's uploads. It is asked before anything is dropped, so a failure to
# answer stops the restore with the database as it was.
PROJECT=$(docker compose config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])') \
	|| fail "could not read the compose project name -- nothing was changed"
[ -n "$PROJECT" ] || fail "the compose configuration names no project -- nothing was changed"

echo "==> Restoring database from $SRC_ABS/db.sql.gz"
# Drop and recreate the public schema, then load the dump.
docker compose exec -T db psql -U silexgis -d silexgis \
	-c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
# psql goes on past an error and still exits 0, so a restore that half failed used to end in
# "Done". Its errors are collected and judged here instead. Three are expected and harmless:
# the database image creates the geocoder's and topology's schemas itself, only `public` is
# dropped above, and the dump's CREATE SCHEMA for each of them then finds it already there.
#
# dash has no pipefail, so the decompressor's own status is written out from inside the pipeline
# and checked afterwards, as backup.sh does for pg_dump.
ERRORS=$(mktemp)
STATUS=$(mktemp)
trap 'rm -f "$ERRORS" "$STATUS"' EXIT
{
	unzipped=0
	gunzip -c "$SRC_ABS/db.sql.gz" || unzipped=$?
	echo "$unzipped" > "$STATUS"
} | docker compose exec -T db psql -U silexgis -d silexgis 2> "$ERRORS" || loaded=$?
cat "$ERRORS" >&2
if [ "$(cat "$STATUS")" != 0 ]; then
	echo "!! db.sql.gz stopped decompressing partway (exit $(cat "$STATUS")): the database holds only" >&2
	echo "!! part of the backup. The files volume has NOT been touched." >&2
	exit 1
fi
if [ "${loaded:-0}" != 0 ]; then
	echo "!! psql exited ${loaded} (it could not connect, or the connection was lost): the database is in" >&2
	echo "!! whatever state the load left it in; the files volume has NOT been touched." >&2
	exit 1
fi
UNEXPECTED=$(grep 'ERROR:' "$ERRORS" \
	| grep -Ev 'ERROR: +schema "(tiger|tiger_data|topology)" already exists' | wc -l | tr -d ' ')
if [ "$UNEXPECTED" -gt 0 ]; then
	echo "!! The database restore reported $UNEXPECTED error(s) -- see above. The database is in" >&2
	echo "!! whatever state the load left it in; the files volume has NOT been touched." >&2
	exit 1
fi

echo "==> Restoring files volume from $SRC_ABS/files.tar.gz"
# The api container runs as the image's non-root app user (uid and gid 1654 in the .NET runtime
# image), which must own the volume to write new uploads into it. A backup taken by the native
# installer records its own service account as the owner, and extracting that as root would hand
# every entry -- the volume's top folder included -- to a uid this container does not have. So
# ownership is not taken from the archive, and the whole tree is given to the app user afterwards.
docker run --rm \
	-v "${PROJECT}_silexgis-files":/data \
	-v "$SRC_ABS":/backup:ro \
	alpine sh -c 'rm -rf /data/* && tar xzf /backup/files.tar.gz --no-same-owner -C /data && chown -R 1654:1654 /data' \
	|| fail "the files volume could not be restored; the database has already been restored"

# Only present when the backup holds browser-uploaded rasters (and absent from backups made before
# they were archived), so its absence is not an error and leaves the terrain volume exactly as it is.
# The uploads folder alone is replaced; builds, pyramids and queued work beside it are not touched.
if [ -f "$SRC_ABS/terrain-uploads.tar.gz" ]; then
	echo "==> Restoring uploaded terrain rasters from $SRC_ABS/terrain-uploads.tar.gz"
	docker run --rm \
		-v "${PROJECT}_silexgis-terrain":/data \
		-v "$SRC_ABS":/backup:ro \
		alpine sh -c 'rm -rf /data/builds/uploads && mkdir -p /data/builds && tar xzf /backup/terrain-uploads.tar.gz --no-same-owner -C /data && chown 1654:1654 /data/builds && chown -R 1654:1654 /data/builds/uploads' \
		|| fail "the uploaded terrain rasters could not be restored; the database and files volume have already been restored"
else
	echo "==> No terrain-uploads.tar.gz in this backup -- the terrain volume is left as it is"
fi

echo "==> Done. Start the stack: docker compose up -d"
