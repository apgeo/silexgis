#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Install the operational wrappers: the `silexgis-update` command, a nightly backup timer,
# and an optional (disabled by default) automatic-update timer.
#
# Usage, as root on the target host:
#   bash install-ops.sh
#
# Overrides (environment):
#   SILEXGIS_DEPLOY_USER  default silexgis
#   SILEXGIS_APP_DIR      default /opt/silexgis
#   SILEXGIS_BACKUP_DIR   default /var/backups/silexgis
#   SILEXGIS_BACKUP_KEEP  default 14
set -euo pipefail

DEPLOY_USER="${SILEXGIS_DEPLOY_USER:-silexgis}"
APP_DIR="${SILEXGIS_APP_DIR:-/opt/silexgis}"
BACKUP_DIR="${SILEXGIS_BACKUP_DIR:-/var/backups/silexgis}"
BACKUP_KEEP="${SILEXGIS_BACKUP_KEEP:-14}"

[ "$(id -u)" -eq 0 ] || { echo "must run as root" >&2; exit 1; }
say() { printf '\n==> %s\n' "$*"; }

# A stable wrapper rather than a symlink into the checkout: the file this invokes is one that
# `git pull` replaces, and a symlink would put the shell that is running the update in the
# path of the update itself. update.sh handles that by re-execing from a copy; keeping the
# entry point outside the checkout means the wrapper never changes underneath a run either.
say "Installing /usr/local/sbin/silexgis-update"
cat > /usr/local/sbin/silexgis-update <<EOF
#!/usr/bin/env bash
# Entry point for updating this SilexGIS installation. The logic lives in the checkout at
# $APP_DIR/deploy/server/update.sh and is therefore updated by the update itself.
set -euo pipefail
if [ "\$(id -un)" != "$DEPLOY_USER" ]; then
	exec setpriv --reuid="$DEPLOY_USER" --regid="$DEPLOY_USER" --init-groups \\
		env SILEXGIS_APP_DIR="$APP_DIR" SILEXGIS_BACKUP_DIR="$BACKUP_DIR" \\
		bash "$APP_DIR/deploy/server/update.sh" "\$@"
fi
export SILEXGIS_APP_DIR="$APP_DIR" SILEXGIS_BACKUP_DIR="$BACKUP_DIR"
exec bash "$APP_DIR/deploy/server/update.sh" "\$@"
EOF
chmod 755 /usr/local/sbin/silexgis-update

install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 750 "$BACKUP_DIR"

say "Nightly backup timer"
cat > /etc/systemd/system/silexgis-backup.service <<EOF
[Unit]
Description=SilexGIS database and file-store backup
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
User=$DEPLOY_USER
WorkingDirectory=$APP_DIR/deploy
# The backup writes a timestamped directory of its own; retention is applied afterwards so a
# failed run cannot delete the previous good one on its way out.
ExecStart=/bin/sh -c 'sh scripts/backup.sh "$BACKUP_DIR/\$(date -u +%%Y%%m%%dT%%H%%M%%SZ)"'
ExecStartPost=/bin/sh -c 'ls -1dt $BACKUP_DIR/*/ 2>/dev/null | tail -n +$((BACKUP_KEEP + 1)) | xargs -r rm -rf'
Nice=10
IOSchedulingClass=idle
EOF

cat > /etc/systemd/system/silexgis-backup.timer <<'EOF'
[Unit]
Description=Nightly SilexGIS backup

[Timer]
OnCalendar=*-*-* 03:20:00
RandomizedDelaySec=20m
Persistent=true

[Install]
WantedBy=timers.target
EOF

say "Automatic update timer (installed, NOT enabled)"
cat > /etc/systemd/system/silexgis-update.service <<'EOF'
[Unit]
Description=Update SilexGIS to the latest commit on its tracked branch
After=docker.service network-online.target
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/silexgis-update
TimeoutStartSec=3600
EOF

cat > /etc/systemd/system/silexgis-update.timer <<'EOF'
[Unit]
Description=Weekly SilexGIS update check
# Deliberately not enabled by install-ops.sh. An unattended update of an application whose
# schema may change under it is a decision an operator makes with their eyes open, not a
# default. Enable with:  systemctl enable --now silexgis-update.timer

[Timer]
OnCalendar=Sun *-*-* 04:00:00
RandomizedDelaySec=30m
Persistent=true

[Install]
WantedBy=timers.target
EOF

systemctl daemon-reload
systemctl enable --now silexgis-backup.timer >/dev/null

say "Installed"
echo "    silexgis-update            update now (backs up first, rolls back on failure)"
echo "    silexgis-update --check    report available commits, change nothing"
echo "    backups                    $BACKUP_DIR, nightly, keeping $BACKUP_KEEP"
systemctl list-timers --no-pager silexgis-\* 2>/dev/null | sed 's/^/    /'
