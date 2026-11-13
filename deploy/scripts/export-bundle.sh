#!/usr/bin/env sh
# Package this installation's data as a bundle another installation can import.
#
# Usage (from the deploy/ directory):
#   sh scripts/export-bundle.sh                 # → ./exports/<UTC timestamp>/
#   sh scripts/export-bundle.sh /path/out       # → /path/out/
#   sh scripts/export-bundle.sh --with-terrain  # also include rasters uploaded through the browser
#
# A bundle is a backup.sh backup (db.sql.gz, files.tar.gz) plus SHA256SUMS and MANIFEST.txt, so
# that a copy cut short or damaged on the way is refused by import-bundle.sh instead of loaded.
# Browser-uploaded terrain rasters are left out unless asked for: they are elevation data that can
# be downloaded again, and they are the bulk of a bundle. Baked terrain is never included.
#
# Serve the folder over plain HTTPS (any static file host) and give the other side the base URL:
#   sh scripts/import-bundle.sh https://host/some-random-path/ --yes
#
# The bundle holds everything the database holds -- accounts and their password hashes, share
# tokens, access-key hashes -- so publish it only where that is acceptable.
set -eu

WITH_TERRAIN=
OUT=
for arg in "$@"; do
	case "$arg" in
		--with-terrain) WITH_TERRAIN=1 ;;
		-*) echo "usage: export-bundle.sh [--with-terrain] [out-dir]" >&2; exit 2 ;;
		*) OUT="$arg" ;;
	esac
done
[ -n "$OUT" ] || OUT="./exports/$(date -u +%Y%m%dT%H%M%SZ)"

HERE=$(cd "$(dirname "$0")" && pwd)
sh "$HERE/backup.sh" "$OUT"
OUT_ABS=$(cd "$OUT" && pwd)

if [ -z "$WITH_TERRAIN" ]; then
	rm -f "$OUT_ABS/terrain-uploads.tar.gz"
fi

{
	echo "exported_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
	echo "source_commit=$(git -C "$HERE" rev-parse --short HEAD 2>/dev/null || echo unknown)"
	echo "migrations=$(docker compose exec -T db psql -U silexgis -d silexgis -tAc 'select count(*) from "__EFMigrationsHistory"' 2>/dev/null | tr -d '\r' || echo unknown)"
	echo "terrain_uploads=$([ -f "$OUT_ABS/terrain-uploads.tar.gz" ] && echo included || echo excluded)"
} > "$OUT_ABS/MANIFEST.txt"

(cd "$OUT_ABS" && sha256sum db.sql.gz files.tar.gz MANIFEST.txt $( [ -f terrain-uploads.tar.gz ] && echo terrain-uploads.tar.gz ) > SHA256SUMS)

echo "==> Bundle ready in $OUT_ABS"
ls -l "$OUT_ABS"
