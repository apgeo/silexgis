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
#   SILEXGIS_UPDATE_LOCK    default /tmp/silexgis-update.lock   shared with reset.sh
#
# Exit status: 0 only when the new version was built, started and answered healthy. Any other
# outcome exits 1 with a FAILED line saying what failed, whether the previous version is running
# again, and whether the database may already have been migrated.
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
exec 9>"${SILEXGIS_UPDATE_LOCK:-/tmp/silexgis-update.lock}"
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
# What a failure message tells the operator to restore from, if anything.
BACKUP_NOTE="no backup was taken by this update (SILEXGIS_SKIP_BACKUP=1)"
if [ "$SKIP_BACKUP" != 1 ]; then
	say "Backup -> $BACKUP_DIR/$STAMP"
	mkdir -p "$BACKUP_DIR/$STAMP"
	# A compose that cannot run at all is not "no database yet": read as that, it skipped the
	# backup and updated anyway.
	running="$(docker compose ps --status running --services)" || \
		fail "docker compose could not list the services (its error is above) -- refusing to update without a backup"
	# A stopped db container is not an empty database either: the volume still holds every row,
	# and the new API would migrate it the moment the stack comes up. So start the database on
	# its own, wait for its health check, and back it up like any other. On an installation that
	# has never held data this dumps an empty schema, which costs nothing.
	if ! printf '%s\n' "$running" | grep -qx db; then
		echo "    database is not running; starting it to take the backup"
		docker compose up -d --wait db || \
			fail "could not start the database to back it up (its error is above) -- refusing to update without a backup"
	fi
	sh scripts/backup.sh "$BACKUP_DIR/$STAMP" || fail "backup failed -- refusing to update"
	echo "$CURRENT" > "$BACKUP_DIR/$STAMP/commit.txt"
	BACKUP_NOTE="restore the backup at $BACKUP_DIR/$STAMP with deploy/scripts/restore.sh"
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

# Every step below is a single command tested on its own. They used to be two commands inside a
# function called as an `if` condition -- and bash ignores `set -e` for everything inside such a
# function, so a failed build fell through to `up -d`, which restarted the previous images, the
# previous API answered healthy, and the script reported "Updated" with exit 0.

# Polls until the public health address answers healthy or the timeout has passed. It always
# probes at least once, so a timeout of 0 means "check once, now".
wait_healthy() {
	local deadline=$(( SECONDS + HEALTH_TIMEOUT ))
	while :; do
		if curl -fsSL --max-time 10 "$HEALTH_URL" 2>/dev/null | grep -qi healthy; then return 0; fi
		[ "$SECONDS" -lt "$deadline" ] || return 1
		sleep 5
	done
}

# Puts the previous commit back and brings its containers up. Each command is checked explicitly
# because this runs as a condition, where `set -e` does not apply. The rebuild is without --pull:
# the previous version's base images are in the local cache, and a registry that cannot be
# reached may be the very reason the update failed. Rebuilding is needed even when the update
# failed at the build: a build that got halfway has already re-pointed some image tags at the new
# code, and the next `up` would start those.
roll_back() {
	git -C "$APP_DIR" reset --hard --quiet "$CURRENT" || return 1
	git -C "$APP_DIR" submodule update --init --recursive --quiet || return 1
	docker compose build || return 1
	docker compose up -d --remove-orphans || return 1
}

# Rolls back after a failed step and exits 1, saying what failed and what is running now.
#   $1  what failed, as a sentence start ("the new version did not build")
#   $2  "migrated" if the new API may have started and so may have applied migrations
abort_update() {
	local what="$1" migrated="${2:-}" previous
	previous="$(git -C "$APP_DIR" rev-parse --short "$CURRENT")"
	say "Update failed: $what -- rolling back to $previous"
	local schema="No new container was started, so the database is as it was."
	if [ "$migrated" = migrated ]; then
		docker compose logs --tail 60 api || true
		schema="If the new version had already applied a database migration, restoring the code is not enough:
$BACKUP_NOTE."
	fi
	if ! roll_back; then
		fail "$what, AND the previous version could not be put back (the error is above).
Check \`docker compose ps\` in $DEPLOY_DIR; $BACKUP_NOTE."
	fi
	say "Waiting for $HEALTH_URL (previous version)"
	if ! wait_healthy; then
		fail "$what, AND the previous version did not come back up within ${HEALTH_TIMEOUT}s.
$BACKUP_NOTE."
	fi
	fail "$what; the previous version ($previous) is running again.
$schema"
}

say "Build"
docker compose build --pull || abort_update "the new version did not build (the error is above)"

say "Start"
docker compose up -d --remove-orphans || \
	abort_update "the new version did not start (the error is above; a failing database migration shows up here)" migrated

say "Waiting for $HEALTH_URL"
wait_healthy || abort_update "the new version did not become healthy within ${HEALTH_TIMEOUT}s" migrated
say "Healthy at $(git -C "$APP_DIR" rev-parse --short HEAD)"

say "Housekeeping"
docker image prune -f >/dev/null || true
# Build cache is the one thing here that grows without bound: every update leaves a new set of
# layers behind, and on a single-disk host that is what eventually fills it. Two weeks keeps
# the cache that makes an incremental rebuild fast while bounding the total.
docker builder prune -f --filter until=336h >/dev/null || true
if [ -d "$BACKUP_DIR" ]; then
	# shellcheck disable=SC2012
	ls -1dt "$BACKUP_DIR"/*/ 2>/dev/null | tail -n "+$((BACKUP_KEEP + 1))" | while read -r old; do
		echo "    removing old backup $(basename "$old")"
		rm -rf "$old"
	done
fi
df -h / | tail -1 | sed 's/^/    disk: /'

say "Updated $(git -C "$APP_DIR" rev-parse --short "$CURRENT") -> $(git -C "$APP_DIR" rev-parse --short HEAD)"
