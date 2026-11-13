#!/usr/bin/env sh
# Replace this installation's data with a bundle made by export-bundle.sh.
#
# Usage (from the deploy/ directory):
#   sh scripts/import-bundle.sh <base-url | folder> --yes
#
# <base-url> is a folder served over HTTP(S) holding db.sql.gz, files.tar.gz, SHA256SUMS and,
# optionally, terrain-uploads.tar.gz. A local folder works too. Everything is downloaded and
# checked against SHA256SUMS BEFORE anything is changed; then the api is stopped, restore.sh
# replaces the database and the files volume, and the stack is started again, which applies any
# migrations this version has and the bundle's version lacks.
#
# WARNING: this overwrites the current database and uploaded files. --yes is required.
set -eu

SRC=
YES=
for arg in "$@"; do
	case "$arg" in
		--yes) YES=1 ;;
		-*) echo "usage: import-bundle.sh <base-url|folder> --yes" >&2; exit 2 ;;
		*) SRC="$arg" ;;
	esac
done
[ -n "$SRC" ] || { echo "usage: import-bundle.sh <base-url|folder> --yes" >&2; exit 2; }
[ -n "$YES" ] || { echo "!! this replaces the current database and files; run again with --yes" >&2; exit 2; }

HERE=$(cd "$(dirname "$0")" && pwd)
case "$SRC" in
	http://*|https://*)
		DIR="./backups/import-$(date -u +%Y%m%dT%H%M%SZ)"
		mkdir -p "$DIR"
		BASE="${SRC%/}"
		for f in SHA256SUMS MANIFEST.txt db.sql.gz files.tar.gz; do
			echo "==> Downloading $f"
			curl -fL --retry 3 -o "$DIR/$f" "$BASE/$f" || { echo "!! could not download $f -- nothing was changed" >&2; exit 1; }
		done
		if grep -q ' terrain-uploads.tar.gz$' "$DIR/SHA256SUMS"; then
			echo "==> Downloading terrain-uploads.tar.gz"
			curl -fL --retry 3 -o "$DIR/terrain-uploads.tar.gz" "$BASE/terrain-uploads.tar.gz" \
				|| { echo "!! could not download terrain-uploads.tar.gz -- nothing was changed" >&2; exit 1; }
		fi
		;;
	*) DIR="$SRC" ;;
esac

echo "==> Verifying checksums"
(cd "$DIR" && sha256sum -c SHA256SUMS) || { echo "!! checksum mismatch -- nothing was changed" >&2; exit 1; }
[ -f "$DIR/MANIFEST.txt" ] && cat "$DIR/MANIFEST.txt"

docker compose stop api
sh "$HERE/restore.sh" "$DIR"
docker compose up -d
echo "==> Imported. The api applies any newer migrations as it starts: docker compose logs -f api"
