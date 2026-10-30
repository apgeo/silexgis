#!/usr/bin/env sh
# Rehearse restoring a SilexGIS backup without touching the running installation.
#
# Usage (from the deploy/ directory of a running stack):
#   sh scripts/restore-drill.sh                     # take a fresh backup, then rehearse it
#   sh scripts/restore-drill.sh ./backups/<stamp>   # rehearse an existing backup
#
# A backup nobody has restored is a hope, not a backup. This loads the database dump into a
# throwaway database container of the same image -- no network, no volume, removed afterwards --
# reads the uploads archives end to end, and compares what came back with the running
# installation. The running installation is only ever READ, and only for counts: nothing here
# stops a service, drops a schema or writes to a volume of the installation.
#
# What it prints is numbers and schema names only -- how many rows each table holds on each
# side, a checksum of each table's contents (a number, computed inside the database and compared
# by this script; never printed per row), how many files and bytes -- never a row's
# contents, so its output can be pasted into a report about an installation that holds real data.
# The checksums read every row of the running installation once, so run the drill at a quiet hour
# on a large installation.
#
# Compared against a fresh backup the two sides should agree exactly on a quiet installation;
# against an older one, tables written since the backup differ, which is expected and reported.
# The verdict is PASS when the dump loads with no error, every table's count and checksum agree,
# the uploads archive reads back with the files the installation holds, and so does the archive of
# browser-uploaded terrain rasters (absent from a backup of an installation that has none).
set -eu

say() { printf '\n==> %s\n' "$*"; }

if [ $# -ge 1 ]; then
	SRC_ABS=$(cd "$1" && pwd)
	FRESH=no
else
	SRC_ABS=$(mktemp -d "${TMPDIR:-/tmp}/silexgis-drill-backup.XXXXXX")
	FRESH=yes
fi

CONFIG=$(docker compose config --format json)
PROJECT=$(printf '%s' "$CONFIG" | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])')
IMAGE=$(printf '%s' "$CONFIG" | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"]["db"]["image"])')
DRILL="${PROJECT}-restore-drill-$$"
WORK=$(mktemp -d "${TMPDIR:-/tmp}/silexgis-drill.XXXXXX")

cleanup() {
	docker rm -f "$DRILL" >/dev/null 2>&1 || true
	rm -rf "$WORK"
	if [ "$FRESH" = yes ]; then rm -rf "$SRC_ABS"; fi
}
trap cleanup EXIT INT TERM

docker compose ps --status running --services 2>/dev/null | grep -qx db || {
	echo "the db service of '$PROJECT' is not running -- run this beside a running stack" >&2
	exit 1
}

if [ "$FRESH" = yes ]; then
	say "Taking a fresh backup to rehearse -> $SRC_ABS (removed afterwards)"
	sh scripts/backup.sh "$SRC_ABS" >/dev/null
fi
[ -f "$SRC_ABS/db.sql.gz" ] || { echo "missing $SRC_ABS/db.sql.gz" >&2; exit 1; }
[ -f "$SRC_ABS/files.tar.gz" ] || { echo "missing $SRC_ABS/files.tar.gz" >&2; exit 1; }

# Exact row count of every table outside the system schemas: one "schema.table|count" per line.
CENSUS="select n.nspname||'.'||c.relname||'|'||(xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
from pg_class c join pg_namespace n on n.oid = c.relnamespace
where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg_toast%'
order by 1;"

# A number per table that changes when any row's contents change: the sum of the first 64 bits of
# each row's md5, so it does not depend on the order rows are read in (the restored copy has no
# reason to return them in the order the running one does). Row text includes every column, geometry
# as hex, so a column that loaded as something else shows up here and a count cannot see it.
CHECKSUMS="select n.nspname||'.'||c.relname||'|'||(xpath('/row/c/text()', query_to_xml(format('select coalesce(sum((''x''||left(md5(t::text),16))::bit(64)::bigint),0) as c from %I.%I t', n.nspname, c.relname), false, true, '')))[1]::text
from pg_class c join pg_namespace n on n.oid = c.relnamespace
where c.relkind in ('r','p') and n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg_toast%'
order by 1;"

say "Starting a throwaway database ($IMAGE, no network) as $DRILL"
docker run -d --name "$DRILL" --network none \
	-e POSTGRES_DB=silexgis -e POSTGRES_USER=silexgis -e POSTGRES_PASSWORD=drill \
	"$IMAGE" >/dev/null
# The image's first start runs its set-up on a server that listens on the socket only, then
# restarts; waiting for TCP is what tells the real start from the set-up one.
tries=0
until docker exec "$DRILL" pg_isready -h 127.0.0.1 -U silexgis -d silexgis >/dev/null 2>&1; do
	tries=$((tries + 1))
	[ "$tries" -lt 90 ] || { echo "the throwaway database did not start" >&2; exit 1; }
	sleep 2
done

say "Loading the dump"
started=$(date +%s)
gunzip -c "$SRC_ABS/db.sql.gz" \
	| docker exec -i "$DRILL" psql -q -U silexgis -d silexgis >/dev/null 2>"$WORK/load.err" || true
echo "    loaded in $(( $(date +%s) - started )) s"
# Only the ERROR lines, with anything quoted masked: an error about a row can quote a value, and
# the lines that follow an error (CONTEXT, DETAIL) can carry a row's text, so those are never shown.
# Three errors are expected and harmless, the same three restore.sh lets through: the image
# creates the geocoder's and topology's schemas itself, so the dump's CREATE SCHEMA finds them.
EXPECTED='ERROR: +schema "(tiger|tiger_data|topology)" already exists'
expected=$(grep -Ec "$EXPECTED" "$WORK/load.err" || true)
errors=$(grep 'ERROR:' "$WORK/load.err" | grep -Evc "$EXPECTED" || true)
echo "    errors while loading: $errors (and $expected expected: schemas the image makes itself)"
if [ "$errors" -gt 0 ]; then
	grep 'ERROR:' "$WORK/load.err" | grep -Ev "$EXPECTED" | sed -e 's/^.*ERROR: *//' -e 's/"[^"]*"/"…"/g' -e "s/'[^']*'/'…'/g" \
		| cut -c1-90 | sort | uniq -c | sort -rn | head -20 | sed 's/^/      /'
fi

say "Comparing every table's row count with the running installation"
docker exec "$DRILL" psql -U silexgis -d silexgis -tAc "$CENSUS" > "$WORK/drill.txt"
docker compose exec -T db psql -U silexgis -d silexgis -tAc "$CENSUS" > "$WORK/live.txt"
python3 - "$WORK/live.txt" "$WORK/drill.txt" > "$WORK/census.txt" <<'PY'
import sys
def read(path):
    out = {}
    for line in open(path):
        line = line.strip()
        if line:
            name, _, count = line.rpartition('|')
            out[name] = int(count)
    return out
live, drill = read(sys.argv[1]), read(sys.argv[2])
names = sorted(set(live) | set(drill))
differ = [n for n in names if live.get(n) != drill.get(n)]
print(f"    tables: {len(names)}   agree: {len(names) - len(differ)}   differ: {len(differ)}")
print(f"    rows: running {sum(live.values())}   restored {sum(drill.values())}")
for n in differ:
    print(f"      {n}: running {live.get(n, 'absent')}   restored {drill.get(n, 'absent')}")
print("RESULT", "agree" if not differ else "differ")
PY
grep -v '^RESULT' "$WORK/census.txt"
census=$(sed -n 's/^RESULT //p' "$WORK/census.txt")

say "Comparing a checksum of every table's contents with the running installation"
docker exec "$DRILL" psql -U silexgis -d silexgis -tAc "$CHECKSUMS" > "$WORK/drill-sums.txt"
docker compose exec -T db psql -U silexgis -d silexgis -tAc "$CHECKSUMS" > "$WORK/live-sums.txt"
python3 - "$WORK/live-sums.txt" "$WORK/drill-sums.txt" > "$WORK/checksums.txt" <<'PY'
import sys
def read(path):
    out = {}
    for line in open(path):
        line = line.strip()
        if line:
            name, _, total = line.rpartition('|')
            out[name] = total
    return out
live, drill = read(sys.argv[1]), read(sys.argv[2])
names = sorted(set(live) | set(drill))
differ = [n for n in names if live.get(n) != drill.get(n)]
print(f"    tables: {len(names)}   checksums agree: {len(names) - len(differ)}   differ: {len(differ)}")
for n in differ:
    print(f"      {n}: contents differ")
print("RESULT", "agree" if not differ else "differ")
PY
grep -v '^RESULT' "$WORK/checksums.txt"
checksums=$(sed -n 's/^RESULT //p' "$WORK/checksums.txt")

say "Reading the uploads archive end to end"
archived=$(docker run --rm -v "$SRC_ABS":/backup:ro alpine \
	sh -c 'tar tzvf /backup/files.tar.gz | awk '\''$1 ~ /^-/ { n++; b += $3 } END { print n + 0, b + 0 }'\''')
running=$(docker run --rm -v "${PROJECT}_silexgis-files":/data:ro alpine \
	sh -c 'find /data -type f -exec stat -c %s {} + | awk '\''{ n++; b += $1 } END { print n + 0, b + 0 }'\''')
set -- $archived; a_files=$1; a_bytes=$2
set -- $running; r_files=$1; r_bytes=$2
echo "    archive: $a_files files, $a_bytes bytes   running: $r_files files, $r_bytes bytes"

say "Reading the browser-uploaded terrain rasters"
# The archive exists only when there were uploaded rasters to put in it, and a backup made before
# they were archived never has one -- so what is compared is "what the archive holds" (nothing, if
# absent) against what the running volume holds under the uploads folder.
if [ -f "$SRC_ABS/terrain-uploads.tar.gz" ]; then
	t_archived=$(docker run --rm -v "$SRC_ABS":/backup:ro alpine \
		sh -c 'tar tzvf /backup/terrain-uploads.tar.gz | awk '\''$1 ~ /^-/ { n++; b += $3 } END { print n + 0, b + 0 }'\''')
else
	t_archived="0 0"
fi
t_running=$(docker run --rm -v "${PROJECT}_silexgis-terrain":/data:ro alpine \
	sh -c 'if [ -d /data/builds/uploads ]; then find /data/builds/uploads -type f -exec stat -c %s {} + | awk '\''{ n++; b += $1 } END { print n + 0, b + 0 }'\''; else echo "0 0"; fi')
set -- $t_archived; t_a_files=$1; t_a_bytes=$2
set -- $t_running; t_r_files=$1; t_r_bytes=$2
echo "    archive: $t_a_files files, $t_a_bytes bytes   running: $t_r_files files, $t_r_bytes bytes"

say "Verdict"
verdict=PASS
[ "$errors" -eq 0 ] || verdict=FAIL
[ "$census" = agree ] || verdict=FAIL
[ "$checksums" = agree ] || verdict=FAIL
[ "$a_files" = "$r_files" ] || verdict=FAIL
[ "$t_a_files" = "$t_r_files" ] || verdict=FAIL
if [ "$verdict" = FAIL ] && [ "$FRESH" = no ]; then
	echo "    FAIL against an older backup: differences may be what was written since it was taken."
	echo "    Run with no argument to rehearse a fresh one, where everything should agree."
fi
echo "    $verdict (backup: $(basename "$SRC_ABS"), fresh: $FRESH)"
[ "$verdict" = PASS ]
