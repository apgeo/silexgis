#!/usr/bin/env sh
# Back up the SilexGIS database and uploaded files from a running Docker Compose stack.
#
# Usage (from the deploy/ directory):
#   sh scripts/backup.sh            # → ./backups/<UTC timestamp>/
#   sh scripts/backup.sh /path/out  # → /path/out/
#
# Produces db.sql.gz (pg_dump) and files.tar.gz (the uploads volume). Restore with restore.sh.
set -eu

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
OUT="${1:-./backups/$STAMP}"
mkdir -p "$OUT"
OUT_ABS=$(cd "$OUT" && pwd)

echo "==> Database  -> $OUT/db.sql.gz"
docker compose exec -T db pg_dump -U silexgis -d silexgis | gzip > "$OUT/db.sql.gz"

# The files volume is dumped via a throwaway container so we do not depend on the api
# container being up. The name is <project>_<volume>; the project is 'silexgis'.
echo "==> Files     -> $OUT/files.tar.gz"
docker run --rm \
	-v silexgis_silexgis-files:/data:ro \
	-v "$OUT_ABS":/backup \
	alpine tar czf /backup/files.tar.gz -C /data .

echo "==> Done: $OUT_ABS"
echo "    (data-protection keys are NOT backed up; losing them only invalidates"
echo "     outstanding file links and sessions, not stored data.)"
