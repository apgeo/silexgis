#!/usr/bin/env sh
# Back up the SilexGIS database and uploaded files from a running Docker Compose stack.
#
# Usage (from the deploy/ directory):
#   sh scripts/backup.sh            # → ./backups/<UTC timestamp>/
#   sh scripts/backup.sh /path/out  # → /path/out/
#
# Produces db.sql.gz (pg_dump), files.tar.gz (the uploads volume) and, when anybody has uploaded
# terrain rasters through the browser, terrain-uploads.tar.gz (those rasters only). Restore with
# restore.sh.
#
# Either every archive is written and checked, or none is: the archives are written into a hidden
# staging folder and moved beside it only once all of them are complete, and any failure removes
# the staging folder (and the output folder, if that leaves it empty) and exits non-zero. A caller
# -- the updater before it migrates, the reset before it restores, the nightly unit before it
# prunes older backups -- may therefore take exit 0 to mean "a restorable backup is here".
#
# This stays POSIX sh, because every caller and the documented cron line start it as `sh ...`, and
# /bin/sh is dash on Debian and Ubuntu.
set -eu

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT="${1:-./backups/$STAMP}"
mkdir -p "$OUT"
OUT_ABS=$(cd "$OUT" && pwd)
STAGE="$OUT_ABS/.incomplete"
rm -rf "$STAGE"
mkdir "$STAGE"

complete=
cleanup() {
	[ -n "$complete" ] && return 0
	rm -rf "$STAGE"
	rmdir "$OUT_ABS" 2>/dev/null || true
	echo "FAILED: no backup was written to $OUT_ABS" >&2
}
trap cleanup EXIT
# dash runs the EXIT trap only on an exit, not on a signal: turn the signals into exits.
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

fail() {
	echo "FAILED: $*" >&2
	exit 1
}

echo "==> Database  -> $OUT/db.sql.gz"
# dash has no pipefail: the status of `pg_dump | gzip` is gzip's alone, and a dump that failed
# halfway would be compressed into a small, valid archive. So the dump's own status is written out
# from inside the pipeline and checked afterwards, and the archive is then read back in full: it
# must decompress cleanly and end with the completion line pg_dump writes only after the last
# object (a stream cut off early has no such line, whatever status was reported for it). The dump
# is compressed as it streams rather than first written out plain, so a backup never needs room
# for an uncompressed copy of the database.
{
	status=0
	docker compose exec -T db pg_dump -U silexgis -d silexgis || status=$?
	echo "$status" > "$STAGE/pg_dump.status"
} | gzip > "$STAGE/db.sql.gz" || fail "could not compress the database dump"
status=$(cat "$STAGE/pg_dump.status" 2>/dev/null || echo unknown)
[ "$status" = 0 ] || fail "pg_dump failed (exit $status) -- the database was not backed up"
rm -f "$STAGE/pg_dump.status"
gzip -t "$STAGE/db.sql.gz" || fail "the compressed database dump does not read back"
gzip -dc "$STAGE/db.sql.gz" | tail -n 20 | grep -q '^-- PostgreSQL database dump complete$' \
	|| fail "the database dump is incomplete: it does not end with pg_dump's completion line"

# The files volume is dumped via a throwaway container so we do not depend on the api
# container being up. The name is <project>_<volume>, and the project is asked of the resolved
# compose config rather than assumed: the files ship with `name: silexgis`, but a host running
# more than one installation distinguishes them with COMPOSE_PROJECT_NAME, and a backup that
# assumed the shipped name would silently dump the neighbouring installation's uploads.
PROJECT=$(docker compose config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])') \
	|| fail "could not read the compose project name"
[ -n "$PROJECT" ] || fail "the compose configuration names no project"
echo "==> Files     -> $OUT/files.tar.gz"
docker run --rm \
	-v "${PROJECT}_silexgis-files":/data:ro \
	-v "$STAGE":/backup \
	alpine tar czf /backup/files.tar.gz -C /data . \
	|| fail "could not archive the files volume"
gzip -t "$STAGE/files.tar.gz" || fail "the files archive does not read back"


# Rasters uploaded through the browser exist nowhere else: a build copies the ones it names into its
# own folder, but the originals stay in the uploads folder of the terrain volume and the copies go
# when a build is deleted. Everything else on that volume -- downloaded elevation, reprojected
# copies, tiles, published pyramids -- is derived data that a new build recreates, and is the
# volume's bulk (tens of gigabytes), so only the uploads folder is archived. An installation that
# never uploaded a raster has nothing to archive, and gets no file rather than an empty one.
echo "==> Terrain   -> $OUT/terrain-uploads.tar.gz (browser-uploaded rasters only)"
docker run --rm \
	-v "${PROJECT}_silexgis-terrain":/data:ro \
	-v "$STAGE":/backup \
	alpine sh -c 'if [ -d /data/builds/uploads ] && [ -n "$(ls -A /data/builds/uploads)" ]; then tar czf /backup/terrain-uploads.tar.gz -C /data builds/uploads; else echo "    no uploaded rasters -- nothing to archive"; fi' \
	|| fail "could not archive the uploaded terrain rasters"
if [ -f "$STAGE/terrain-uploads.tar.gz" ]; then
	gzip -t "$STAGE/terrain-uploads.tar.gz" || fail "the terrain archive does not read back"
fi

# Everything is complete: move it into place. The database dump goes last, so that an output
# folder holding a db.sql.gz from this run also holds the rest of it.
for name in files.tar.gz terrain-uploads.tar.gz db.sql.gz; do
	if [ -f "$STAGE/$name" ]; then
		mv -f "$STAGE/$name" "$OUT_ABS/$name" || fail "could not move $name into $OUT_ABS"
	fi
done
rmdir "$STAGE" || fail "unexpected files left in $STAGE"
complete=1

echo "==> Done: $OUT_ABS"
echo "    (data-protection keys and token certificates are NOT backed up; losing them only invalidates"
echo "     outstanding file links and sessions, not stored data.)"
echo "    (baked terrain is NOT backed up beyond the rasters uploaded through the browser: the"
echo "     rest is derived data whose recipe is in the database, it is the largest and least"
echo "     valuable thing on disk, and losing it costs the 3D view its ground until a build is"
echo "     run again. See 'Backups' in docs/INSTALL.md. Photo libraries have their own state"
echo "     and are not covered either.)"
