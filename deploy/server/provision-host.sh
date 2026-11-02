#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Prepare a fresh Ubuntu server to run the SilexGIS Docker stack.
#
# Idempotent: re-running it is the supported way to re-apply the configuration after an
# Ubuntu upgrade or a manual change. It installs Docker, creates the unprivileged account
# that owns the checkout, sizes swap, and puts a firewall and automatic security updates in
# place. It deliberately does NOT touch sshd -- see harden-ssh.sh, which is separate so that
# password logins are only switched off once a key login has been proven to work.
#
# Usage, as root on the target host:
#   bash provision-host.sh
#
# Overrides (environment):
#   SILEXGIS_DEPLOY_USER  default silexgis  account owning /opt/silexgis, in the docker group
#   SILEXGIS_SWAP_GB      default 8         swap file size in GiB; 0 skips swap entirely
#   SILEXGIS_SSH_PORT     default 22        port the firewall opens for ssh
#   SILEXGIS_HTTP_PORT    default 80        port the firewall opens for the web front
#   SILEXGIS_HTTPS        default 0         also open 443 (set when the TLS overlay is used)
set -euo pipefail

DEPLOY_USER="${SILEXGIS_DEPLOY_USER:-silexgis}"
SWAP_GB="${SILEXGIS_SWAP_GB:-8}"
SSH_PORT="${SILEXGIS_SSH_PORT:-22}"
HTTP_PORT="${SILEXGIS_HTTP_PORT:-80}"
OPEN_HTTPS="${SILEXGIS_HTTPS:-0}"
APP_DIR=/opt/silexgis

say() { printf '\n==> %s\n' "$*"; }

# Container logs are unbounded by default and this host has one disk for everything.
#
# The three settings are merged into whatever daemon.json already holds rather than written over
# it: an operator's data-root on a second disk, registry mirror or proxy must survive a re-run, and
# losing a data-root silently starts the daemon on an empty image store. The operator's own log
# options are kept only when the file already used the json-file driver: options of another driver
# (syslog-address, gelf-address) are unknown to json-file, and a daemon handed them refuses to
# start. A daemon.json that is not valid JSON, or a result the daemon itself rejects, stops the
# script with the file as it was. The log settings take effect only when the daemon restarts (a
# reload does not apply them), so it is restarted -- and only when the file actually changed,
# because a restart without live-restore already in force stops the containers.
#
# Takes the configuration directory (/etc/docker on a host) so that it can be run against a scratch one.
configure_docker_daemon() {
	local dir="$1"
	local current="$dir/daemon.json" new driver
	local settings='{"log-driver": "json-file", "log-opts": {"max-size": "10m", "max-file": "5"}, "live-restore": true}'
	say "Docker: daemon configuration"
	mkdir -p "$dir"
	new=$(mktemp "$dir/daemon.json.XXXXXX")
	if [ -s "$current" ]; then
		if ! driver=$(jq -r '.["log-driver"] // "json-file"' "$current" 2>/dev/null) \
			|| ! jq --argjson ours "$settings" \
				'if (.["log-driver"] // "json-file") == "json-file"
				 then . + $ours + {"log-opts": ((.["log-opts"] // {}) + $ours["log-opts"])}
				 else . + $ours end' \
				"$current" > "$new"; then
			rm -f "$new"
			echo "!! $current is not valid JSON -- fix it by hand and run this again" >&2
			return 1
		fi
		if [ "$driver" != json-file ]; then
			echo "    note: the log driver was $driver; it is now json-file, and that driver's options replace its own" >&2
		fi
	else
		jq -n --argjson ours "$settings" '$ours' > "$new"
	fi
	if cmp -s "$new" "$current"; then
		rm -f "$new"
		systemctl enable --now docker >/dev/null
		return 0
	fi
	# Ask the daemon whether it would start on the new file before the running one is stopped for it.
	# --validate arrived in Docker 23; an engine without it is restarted unasked, as before.
	if command -v dockerd >/dev/null && dockerd --help 2>&1 | grep -q -- '--validate'; then
		if ! dockerd --validate --config-file "$new" >&2; then
			rm -f "$new"
			echo "!! the Docker daemon rejects the merged $current -- it was left as it was and Docker was not restarted" >&2
			return 1
		fi
	fi
	chmod 644 "$new"
	mv -f "$new" "$current"
	systemctl enable docker >/dev/null
	systemctl restart docker
}

# Firewall. Note that Docker inserts its published-port rules ahead of ufw's filter chain, so ufw
# does not gate a published container port: what actually keeps the database private is that it
# publishes no port at all. These rules govern host services.
#
# Rules are added, never reset: a re-run must not drop a rule the operator added by hand, nor the
# 443 rule of an earlier run that set SILEXGIS_HTTPS=1 when this one does not. `ufw allow` of a
# rule that already exists does nothing. The flip side is that a port this script once opened and
# no longer needs (an SSH port that moved) stays open until it is removed by hand:
# ufw delete allow <port>/tcp
configure_firewall() {
	say "Firewall"
	ufw default deny incoming >/dev/null
	ufw default allow outgoing >/dev/null
	ufw allow "${SSH_PORT}/tcp" comment 'ssh' >/dev/null
	ufw allow "${HTTP_PORT}/tcp" comment 'silexgis web' >/dev/null
	if [ "$OPEN_HTTPS" = 1 ]; then
		ufw allow 443/tcp comment 'silexgis web tls' >/dev/null
	fi
	ufw --force enable >/dev/null
	ufw status verbose | sed 's/^/    /'
}

# Sourced rather than run (as the tests do): define the steps above and change nothing.
if [ "${BASH_SOURCE[0]}" != "$0" ]; then
	return 0
fi

[ "$(id -u)" -eq 0 ] || { echo "must run as root" >&2; exit 1; }
. /etc/os-release
[ "${ID:-}" = ubuntu ] || echo "warning: tested on Ubuntu, found ${ID:-unknown}" >&2

export DEBIAN_FRONTEND=noninteractive

say "Base packages"
apt-get update -qq
apt-get -y -qq full-upgrade
apt-get -y -qq install ca-certificates curl gnupg git ufw fail2ban \
	unattended-upgrades apt-listchanges jq

# ---------------------------------------------------------------------------
# Swap. This host runs a JVM-based terrain baker whose heap is a percentage of
# its container memory limit, alongside PostgreSQL and the API. Swap is a
# safety net for the bake peak and for the image build, not a place to run
# from -- hence swappiness 10 rather than the default 60.
# ---------------------------------------------------------------------------
if [ "$SWAP_GB" != 0 ]; then
	if ! swapon --show --noheadings | grep -q .; then
		say "Swap: creating ${SWAP_GB}G at /swapfile"
		fallocate -l "${SWAP_GB}G" /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=$((SWAP_GB * 1024))
		chmod 600 /swapfile
		mkswap /swapfile >/dev/null
		swapon /swapfile
		grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
	else
		say "Swap: already active, leaving alone"
	fi
fi
cat > /etc/sysctl.d/60-silexgis.conf <<'EOF'
# Swap is a burst safety net for terrain baking and image builds, not routine storage.
vm.swappiness = 10
vm.vfs_cache_pressure = 50
EOF
sysctl -q --system

# ---------------------------------------------------------------------------
# Docker from the official repository. The Ubuntu archive's docker.io ships the
# engine without a matching Compose plugin version; the stack's TLS overlay
# needs Compose v2.24+ for `ports: !reset []`.
# ---------------------------------------------------------------------------
if ! command -v docker >/dev/null; then
	say "Docker: installing from download.docker.com"
	install -m 0755 -d /etc/apt/keyrings
	curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
	chmod a+r /etc/apt/keyrings/docker.asc
	echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
		> /etc/apt/sources.list.d/docker.list
	apt-get update -qq
	apt-get -y -qq install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
else
	say "Docker: already installed ($(docker --version))"
fi

configure_docker_daemon /etc/docker

# ---------------------------------------------------------------------------
# The account that owns the checkout. Membership of the docker group is
# equivalent to root on this host; that is inherent to a Compose deployment and
# is why this account exists at all rather than the stack running from /root.
# ---------------------------------------------------------------------------
if ! id -u "$DEPLOY_USER" >/dev/null 2>&1; then
	say "User: creating $DEPLOY_USER"
	adduser --disabled-password --gecos "SilexGIS service account" "$DEPLOY_USER"
else
	say "User: $DEPLOY_USER exists"
fi
usermod -aG docker "$DEPLOY_USER"
passwd -l "$DEPLOY_USER" >/dev/null   # key logins only; no password to guess

# Passwordless sudo. This grants nothing the docker group above has not already granted --
# anyone who can talk to the container engine can start a privileged container and read the
# host -- and without it, disabling root ssh logins would leave the machine with no
# administrative path at all. The account's password is locked, so a sudo that prompted for
# one could never succeed.
cat > /etc/sudoers.d/60-silexgis <<EOF
${DEPLOY_USER} ALL=(ALL) NOPASSWD:ALL
EOF
chmod 440 /etc/sudoers.d/60-silexgis
visudo -cf /etc/sudoers.d/60-silexgis >/dev/null

# Carry root's authorised keys across so the operator keeps access under the new account.
if [ -s /root/.ssh/authorized_keys ]; then
	install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
	touch "/home/$DEPLOY_USER/.ssh/authorized_keys"
	while IFS= read -r key; do
		[ -n "$key" ] || continue
		grep -qxF "$key" "/home/$DEPLOY_USER/.ssh/authorized_keys" || echo "$key" >> "/home/$DEPLOY_USER/.ssh/authorized_keys"
	done < /root/.ssh/authorized_keys
	chmod 600 "/home/$DEPLOY_USER/.ssh/authorized_keys"
	chown -R "$DEPLOY_USER:$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
fi

install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 755 "$APP_DIR"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 750 /var/backups/silexgis

configure_firewall

say "fail2ban"
cat > /etc/fail2ban/jail.d/silexgis.local <<EOF
[DEFAULT]
bantime  = 1h
findtime = 10m
maxretry = 5
backend  = systemd

[sshd]
enabled = true
port    = ${SSH_PORT}
EOF
systemctl enable --now fail2ban >/dev/null
systemctl restart fail2ban

say "Unattended security upgrades"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
EOF
cat > /etc/apt/apt.conf.d/52silexgis-unattended <<'EOF'
// Security updates only. Docker's repository is deliberately absent from the
// origins list: an engine upgrade restarts the daemon and every container with
// it, which is an operator's decision rather than a nightly one.
Unattended-Upgrade::Allowed-Origins {
        "${distro_id}:${distro_codename}-security";
        "${distro_id}ESMApps:${distro_codename}-apps-security";
        "${distro_id}ESM:${distro_codename}-infra-security";
};
Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
Unattended-Upgrade::Remove-Unused-Dependencies "true";
Unattended-Upgrade::Automatic-Reboot "false";
EOF
systemctl enable --now unattended-upgrades >/dev/null

# Journals default to a percentage of the filesystem; cap them on a single-disk host.
say "Journal size cap"
mkdir -p /etc/systemd/journald.conf.d
cat > /etc/systemd/journald.conf.d/60-silexgis.conf <<'EOF'
[Journal]
SystemMaxUse=500M
EOF
systemctl restart systemd-journald

timedatectl set-timezone UTC

say "Host provisioned"
echo "    docker:  $(docker --version)"
echo "    compose: $(docker compose version --short 2>/dev/null)"
echo "    swap:    $(swapon --show=SIZE --noheadings | tr -d ' \n')"
echo "    user:    $DEPLOY_USER (docker group, no password)"
[ -f /var/run/reboot-required ] && echo "    NOTE: reboot required (${*:-pending kernel})"
