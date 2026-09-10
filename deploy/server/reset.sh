#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Keep a named "baseline" copy of a SilexGIS installation's data, and put the installation
# back to it on demand. This exists for test and demo installations whose credentials are
# deliberately public: whatever visitors do to the data, one command (or a timer) returns
# the instance to a known-good state.
#
#   silexgis-baseline          capture the CURRENT data as the new baseline
#   silexgis-reset             restore the baseline (destroys everything done since)
#   silexgis-reset --status    show what baseline exists, change nothing
#
# The baseline lives OUTSIDE the rotated backup directory on purpose: both the nightly
# backup service and the updater prune old entries of that rotation by age, and a baseline
# is old by definition -- the one thing it must not be is pruned.
#
# Overrides (environment):
#   SILEXGIS_APP_DIR        default /opt/silexgis
#   SILEXGIS_BASELINE_DIR   default /var/backups/silexgis-baseline
#   SILEXGIS_BACKUP_DIR     default /var/backups/silexgis   (pre-reset safety copy)
#   SILEXGIS_HEALTH_TIMEOUT default 300 seconds to wait for /health/ready after a reset
set -euo pipefail

APP_DIR="${SILEXGIS_APP_DIR:-/opt/silexgis}"
DEPLOY_DIR="$APP_DIR/deploy"
BASELINE_DIR="${SILEXGIS_BASELINE_DIR:-/var/backups/silexgis-baseline}"
BACKUP_DIR="${SILEXGIS_BACKUP_DIR:-/var/backups/silexgis}"
HEALTH_TIMEOUT="${SILEXGIS_HEALTH_TIMEOUT:-300}"

MODE="${1:-}"
case "$MODE" in
	baseline|reset) ;;
	--status) MODE=status ;;
	*) echo "usage: reset.sh baseline|reset|--status" >&2; exit 2 ;;
esac

say()  { printf '\n==> %s\n' "$*"; }
fail() { printf '\nFAILED: %s\n' "$*" >&2; exit 1; }

[ -d "$DEPLOY_DIR" ] || fail "$DEPLOY_DIR does not exist"
cd "$DEPLOY_DIR"

if [ "$MODE" = status ]; then
	if [ -f "$BASELINE_DIR/current/db.sql.gz" ]; then
		echo "baseline: $BASELINE_DIR/current"
		[ -f "$BASELINE_DIR/current/commit.txt" ] && echo "captured at commit $(cat "$BASELINE_DIR/current/commit.txt")"
		ls -lh "$BASELINE_DIR/current" | sed 's/^/    /'
	else
		echo "no baseline captured yet -- run silexgis-baseline first"
	fi
	exit 0
fi

# Share the updater's lock: a reset in the middle of an update (or the other way round)
# would interleave two different writers of the same database.
exec 9>"/tmp/silexgis-update.lock"
flock -w 600 9 || fail "could not take the update lock -- is an update or reset running?"

docker compose ps --status running --services 2>/dev/null | grep -qx db || \
	fail "the db service is not running"

if [ "$MODE" = baseline ]; then
	STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
	say "Capturing baseline -> $BASELINE_DIR/$STAMP"
	mkdir -p "$BASELINE_DIR/$STAMP"
	sh scripts/backup.sh "$BASELINE_DIR/$STAMP" || fail "capture failed"
	git -C "$APP_DIR" rev-parse HEAD > "$BASELINE_DIR/$STAMP/commit.txt" 2>/dev/null || true

	# Only after the new capture is complete does it become "current"; a failed capture
	# leaves the previous baseline in place untouched.
	ln -sfn "$BASELINE_DIR/$STAMP" "$BASELINE_DIR/current"

	# Keep the two most recent captures: the one in use and the one it replaced.
	ls -1dt "$BASELINE_DIR"/*/ 2>/dev/null | grep -v '/current/$' | tail -n +3 | while read -r old; do
		echo "    removing old baseline $(basename "$old")"
		rm -rf "$old"
	done
	say "Baseline captured. Restore it any time with: silexgis-reset"
	exit 0
fi

# MODE = reset -------------------------------------------------------------------------
[ -f "$BASELINE_DIR/current/db.sql.gz" ] || \
	fail "no baseline at $BASELINE_DIR/current -- capture one first with silexgis-baseline"

# What is about to be destroyed goes into the ordinary backup rotation first. It costs a
# few megabytes and it is the difference between "the timer fired an hour early" being an
# anecdote or a loss.
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
say "Pre-reset safety backup -> $BACKUP_DIR/pre-reset-$STAMP"
mkdir -p "$BACKUP_DIR/pre-reset-$STAMP"
sh scripts/backup.sh "$BACKUP_DIR/pre-reset-$STAMP" || fail "safety backup failed -- refusing to reset"

say "Stopping the api service"
docker compose stop api

say "Restoring baseline from $BASELINE_DIR/current"
if ! sh scripts/restore.sh "$BASELINE_DIR/current"; then
	docker compose up -d
	fail "restore failed; the api has been started again over whatever state the database is in.
The pre-reset copy is at $BACKUP_DIR/pre-reset-$STAMP."
fi

say "Starting the stack"
docker compose up -d

# The api runs its migrations on start, so a baseline captured before a schema change is
# carried forward automatically. Wait for ready all the same: a baseline old enough to
# predate a squashed migration history will not come up, and that should be said here
# rather than discovered by the next visitor.
PUBLIC_URL="$(sed -n 's#^SILEXGIS_PUBLIC_URL=##p' .env 2>/dev/null | tail -1)"
if [ -z "$PUBLIC_URL" ]; then
	HTTP_PORT="$(sed -n 's/^SILEXGIS_HTTP_PORT=//p' .env 2>/dev/null | tail -1)"
	PUBLIC_URL="http://127.0.0.1:${HTTP_PORT:-8080}"
fi
HEALTH_URL="${PUBLIC_URL%/}/health/ready"
say "Waiting for $HEALTH_URL"
deadline=$(( SECONDS + HEALTH_TIMEOUT ))
while [ "$SECONDS" -lt "$deadline" ]; do
	if curl -fsSL --max-time 10 "$HEALTH_URL" 2>/dev/null | grep -qi healthy; then
		say "Reset complete -- the installation is back at its baseline"
		exit 0
	fi
	sleep 5
done
docker compose logs --tail 40 api || true
fail "the api did not become healthy after the restore. The state it was in before the
reset is at $BACKUP_DIR/pre-reset-$STAMP; if the baseline predates the current schema's
migration history, capture a fresh baseline after repairing the instance."
