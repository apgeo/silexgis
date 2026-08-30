#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Update a deployed SilexGIS to the latest commit on its tracked branch, keeping every byte
# of stored data, and put the previous version back if the new one does not come up healthy.
#
# What survives an update, and why: all persistent state lives in named Docker volumes
# (silexgis-db, silexgis-files, silexgis-keys, silexgis-terrain). Rebuilding images and
# recreating containers does not touch a volume. The one command that would destroy data is
# `docker compose down -v`; it appears nowhere in this script, and should appear nowhere in
# any procedure an operator runs against a live installation.
#
# Usage, as the deploy account:
#   silexgis-update            # update if the branch has moved
#   silexgis-update --check    # report whether it has, change nothing
#   silexgis-update --force    # rebuild and restart even at the same commit
#
# Overrides (environment):
#   SILEXGIS_APP_DIR        default /opt/silexgis
#   SILEXGIS_BACKUP_DIR     default /var/backups/silexgis
#   SILEXGIS_BACKUP_KEEP    default 10     how many timestamped backups to retain
#   SILEXGIS_HEALTH_TIMEOUT default 300    seconds to wait for /health/ready
#   SILEXGIS_SKIP_BACKUP    default 0      set 1 to skip the pre-update backup (discouraged)
set -euo pipefail

# ---------------------------------------------------------------------------
# Run from a copy of ourselves. This script lives inside the checkout it is about to `git
# pull`, and bash reads a script incrementally as it executes: replacing the file underneath a
# running shell can make it resume at a byte offset that is now the middle of a different
# line. Copying to a temporary file and re-execing once removes the whole class of problem.
# The next run picks up whatever the pull brought.
# ---------------------------------------------------------------------------
if [ "${SILEXGIS_UPDATE_REEXEC:-}" != 1 ]; then
	_self="$(mktemp /tmp/silexgis-update.XXXXXX.sh)"
	cp "$0" "$_self"
	chmod +x "$_self"
	export SILEXGIS_UPDATE_REEXEC=1
	export SILEXGIS_UPDATE_SELFCOPY="$_self"
	exec "$_self" "$@"
fi
trap '[ -n "${SILEXGIS_UPDATE_SELFCOPY:-}" ] && rm -f "$SILEXGIS_UPDATE_SELFCOPY"' EXIT

APP_DIR="${SILEXGIS_APP_DIR:-/opt/silexgis}"
DEPLOY_DIR="$APP_DIR/deploy"
BACKUP_DIR="${SILEXGIS_BACKUP_DIR:-/var/backups/silexgis}"
BACKUP_KEEP="${SILEXGIS_BACKUP_KEEP:-10}"
HEALTH_TIMEOUT="${SILEXGIS_HEALTH_TIMEOUT:-300}"
SKIP_BACKUP="${SILEXGIS_SKIP_BACKUP:-0}"

MODE=update
case "${1:-}" in
	--check) MODE=check ;;
	--force) MODE=force ;;
	"") ;;
	*) echo "unknown argument: $1" >&2; exit 2 ;;
esac

say()  { printf '\n==> %s\n' "$*"; }
fail() { printf '\nFAILED: %s\n' "$*" >&2; exit 1; }

[ -d "$APP_DIR/.git" ] || fail "$APP_DIR is not a git checkout"
cd "$DEPLOY_DIR"
[ -f .env ] || fail "$DEPLOY_DIR/.env is missing"

# Check the address users actually reach, and follow redirects to it. Checking
# http://127.0.0.1:<port> instead looks equivalent and is not: with the TLS overlay in front,
# that port belongs to Caddy, which answers every plain-HTTP request with a 308 to https --
# and curl treats a 308 as success, so the gate would go green without one byte having come
# from the API. Asserting the body rather than the status closes the same gap from the other
# side: a proxy error page is a 200 as far as an exit code is concerned.
PUBLIC_URL="$(sed -n 's#^SILEXGIS_PUBLIC_URL=##p' .env | tail -1)"
if [ -z "$PUBLIC_URL" ]; then
	HTTP_PORT="$(sed -n 's/^SILEXGIS_HTTP_PORT=//p' .env | tail -1)"
	PUBLIC_URL="http://127.0.0.1:${HTTP_PORT:-8080}"
fi
HEALTH_URL="${PUBLIC_URL%/}/health/ready"

# One updater at a time. Two concurrent builds on a small host is how an update runs the
# machine out of memory and leaves neither version running.
exec 9>"/tmp/silexgis-update.lock"
flock -n 9 || fail "another update is already running"

BRANCH="$(git -C "$APP_DIR" rev-parse --abbrev-ref HEAD)"
git -C "$APP_DIR" fetch --quiet origin "$BRANCH"
CURRENT="$(git -C "$APP_DIR" rev-parse HEAD)"
TARGET="$(git -C "$APP_DIR" rev-parse "origin/$BRANCH")"

echo "branch:  $BRANCH"
echo "current: $(git -C "$APP_DIR" rev-parse --short "$CURRENT")"
echo "target:  $(git -C "$APP_DIR" rev-parse --short "$TARGET")"

if [ "$MODE" = check ]; then
	if [ "$CURRENT" = "$TARGET" ]; then
		echo "up to date"
	else
		echo
		echo "$(git -C "$APP_DIR" rev-list --count "$CURRENT..$TARGET") commit(s) available:"
		git -C "$APP_DIR" log --oneline --no-decorate "$CURRENT..$TARGET" | sed 's/^/    /'
	fi
	exit 0
fi

if [ "$CURRENT" = "$TARGET" ] && [ "$MODE" != force ]; then
	echo
	echo "already up to date -- nothing to do (use --force to rebuild anyway)"
	exit 0
fi

# ---------------------------------------------------------------------------
# Back up before anything can change the schema. Migrations run when the API container
# starts, so by the time a bad migration is visible it has already been attempted; the backup
# taken here is what makes that recoverable rather than merely observable.
# ---------------------------------------------------------------------------
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
if [ "$SKIP_BACKUP" != 1 ]; then
	say "Backup -> $BACKUP_DIR/$STAMP"
	mkdir -p "$BACKUP_DIR/$STAMP"
	if docker compose ps --status running --services 2>/dev/null | grep -qx db; then
		sh scripts/backup.sh "$BACKUP_DIR/$STAMP" || fail "backup failed -- refusing to update"
		echo "$CURRENT" > "$BACKUP_DIR/$STAMP/commit.txt"
	else
		echo "    database is not running; nothing to back up yet"
		rmdir "$BACKUP_DIR/$STAMP" 2>/dev/null || true
	fi
else
	say "Backup skipped (SILEXGIS_SKIP_BACKUP=1)"
fi

say "Updating checkout to $(git -C "$APP_DIR" rev-parse --short "$TARGET")"
git -C "$APP_DIR" merge --ff-only "origin/$BRANCH" || \
	fail "cannot fast-forward -- the checkout has diverged from origin/$BRANCH"

# A fast-forward moves the submodule *pointer* without touching the submodule's working tree.
# Skipping this builds new application code against the old survey readers, which either fails
# to compile or -- worse -- succeeds against the wrong version.
git -C "$APP_DIR" submodule update --init --recursive --quiet || \
	fail "could not update submodules"

bring_up() {
	docker compose build --pull
	docker compose up -d --remove-orphans
}

wait_healthy() {
	local deadline=$(( SECONDS + HEALTH_TIMEOUT ))
	while [ "$SECONDS" -lt "$deadline" ]; do
		if curl -fsSL --max-time 10 "$HEALTH_URL" 2>/dev/null | grep -qi healthy; then return 0; fi
		# A container that keeps restarting will never become healthy; say so early rather
		# than spending the whole timeout on it.
		sleep 5
	done
	return 1
}

say "Build and start"
if ! bring_up; then
	say "Build or start failed -- rolling back to $(git -C "$APP_DIR" rev-parse --short "$CURRENT")"
	git -C "$APP_DIR" reset --hard --quiet "$CURRENT"
	git -C "$APP_DIR" submodule update --init --recursive --quiet || true
	bring_up || true
	fail "update aborted; previous version restored"
fi

say "Waiting for $HEALTH_URL"
if wait_healthy; then
	say "Healthy at $(git -C "$APP_DIR" rev-parse --short HEAD)"
else
	say "Did not become healthy within ${HEALTH_TIMEOUT}s -- rolling back"
	docker compose logs --tail 60 api || true
	git -C "$APP_DIR" reset --hard --quiet "$CURRENT"
	git -C "$APP_DIR" submodule update --init --recursive --quiet || true
	bring_up || true
	if wait_healthy; then
		fail "update failed; previous version ($(git -C "$APP_DIR" rev-parse --short "$CURRENT")) is running again.
If the new version had already applied a database migration, restoring the code is not enough:
restore the backup at $BACKUP_DIR/$STAMP with deploy/scripts/restore.sh."
	fi
	fail "update failed AND the previous version did not come back up.
Restore from $BACKUP_DIR/$STAMP using deploy/scripts/restore.sh."
fi

say "Housekeeping"
docker image prune -f >/dev/null || true
if [ -d "$BACKUP_DIR" ]; then
	# shellcheck disable=SC2012
	ls -1dt "$BACKUP_DIR"/*/ 2>/dev/null | tail -n "+$((BACKUP_KEEP + 1))" | while read -r old; do
		echo "    removing old backup $(basename "$old")"
		rm -rf "$old"
	done
fi
df -h / | tail -1 | sed 's/^/    disk: /'

say "Updated $(git -C "$APP_DIR" rev-parse --short "$CURRENT") -> $(git -C "$APP_DIR" rev-parse --short HEAD)"
