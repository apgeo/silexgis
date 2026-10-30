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
set -eu

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT="${1:-./backups/$STAMP}"
mkdir -p "$OUT"
OUT_ABS=$(cd "$OUT" && pwd)

echo "==> Database  -> $OUT/db.sql.gz"
docker compose exec -T db pg_dump -U silexgis -d silexgis | gzip > "$OUT/db.sql.gz"

# The files volume is dumped via a throwaway container so we do not depend on the api
# container being up. The name is <project>_<volume>, and the project is asked of the resolved
# compose config rather than assumed: the files ship with `name: silexgis`, but a host running
# more than one installation distinguishes them with COMPOSE_PROJECT_NAME, and a backup that
# assumed the shipped name would silently dump the neighbouring installation's uploads.
PROJECT=$(docker compose config --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])')
echo "==> Files     -> $OUT/files.tar.gz"
docker run --rm \
	-v "${PROJECT}_silexgis-files":/data:ro \
	-v "$OUT_ABS":/backup \
	alpine tar czf /backup/files.tar.gz -C /data .


# Rasters uploaded through the browser exist nowhere else: a build copies the ones it names into its
# own folder, but the originals stay in the uploads folder of the terrain volume and the copies go
# when a build is deleted. Everything else on that volume -- downloaded elevation, reprojected
# copies, tiles, published pyramids -- is derived data that a new build recreates, and is the
# volume's bulk (tens of gigabytes), so only the uploads folder is archived. An installation that
# never uploaded a raster has nothing to archive, and gets no file rather than an empty one.
echo "==> Terrain   -> $OUT/terrain-uploads.tar.gz (browser-uploaded rasters only)"
docker run --rm \
	-v "${PROJECT}_silexgis-terrain":/data:ro \
	-v "$OUT_ABS":/backup \
	alpine sh -c 'if [ -d /data/builds/uploads ] && [ -n "$(ls -A /data/builds/uploads)" ]; then tar czf /backup/terrain-uploads.tar.gz -C /data builds/uploads; else echo "    no uploaded rasters -- nothing to archive"; fi'

echo "==> Done: $OUT_ABS"
echo "    (data-protection keys and token certificates are NOT backed up; losing them only invalidates"
echo "     outstanding file links and sessions, not stored data.)"
echo "    (baked terrain is NOT backed up beyond the rasters uploaded through the browser: the"
echo "     rest is derived data whose recipe is in the database, it is the largest and least"
echo "     valuable thing on disk, and losing it costs the 3D view its ground until a build is"
echo "     run again. See 'Backups' in docs/INSTALL.md. Photo libraries have their own state"
echo "     and are not covered either.)"
