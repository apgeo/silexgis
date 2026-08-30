#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Close off password and root logins over ssh.
#
# Separate from provision-host.sh on purpose. Applying this before a key login as the
# unprivileged account has actually been tested is the classic way to lock yourself out of a
# remote machine, so the order is: provision, log in as the deploy user, run sudo, and only
# then run this. It refuses to proceed unless that account has an authorised key.
#
# Usage, as root on the target host:
#   bash harden-ssh.sh
#
# Overrides (environment):
#   SILEXGIS_DEPLOY_USER  default silexgis
set -euo pipefail

DEPLOY_USER="${SILEXGIS_DEPLOY_USER:-silexgis}"
KEYS="/home/$DEPLOY_USER/.ssh/authorized_keys"

[ "$(id -u)" -eq 0 ] || { echo "must run as root" >&2; exit 1; }

if [ ! -s "$KEYS" ]; then
	echo "refusing: $KEYS is missing or empty -- disabling password login now would" >&2
	echo "leave no way back in. Install a key for $DEPLOY_USER first." >&2
	exit 1
fi
echo "==> $DEPLOY_USER has $(grep -c . "$KEYS") authorised key(s)"

# A drop-in rather than an edit of sshd_config: it survives package upgrades and is a single
# file to remove if it ever has to be undone from a provider's serial console.
#
# The 01- prefix is load-bearing. sshd keeps the FIRST value it reads for a keyword, and the
# include of this directory is at the top of sshd_config, so the lowest-sorting file wins --
# the opposite of the last-one-wins convention these directories usually follow. Ubuntu cloud
# images ship 50-cloud-init.conf containing `PasswordAuthentication yes`, which silently beats
# any hardening file named 60-. Sorting ahead of it is what makes this take effect, and
# checking `sshd -T` afterwards rather than trusting the file is what proves it did.
rm -f /etc/ssh/sshd_config.d/60-silexgis.conf
cat > /etc/ssh/sshd_config.d/01-silexgis.conf <<'EOF'
# Public-key authentication only. Recovery, should the key be lost, is the provider's
# console -- not a password over the network.
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
PermitEmptyPasswords no
UsePAM yes

# Drop unauthenticated connections quickly and cap how many may queue.
LoginGraceTime 30
MaxAuthTries 3
MaxSessions 10
MaxStartups 10:30:60

X11Forwarding no
AllowAgentForwarding no
AllowTcpForwarding yes
EOF

# Ubuntu 26.04 uses socket activation for sshd; validate before restarting either unit so a
# bad file cannot take the daemon down.
sshd -t
systemctl restart ssh 2>/dev/null || systemctl restart sshd

echo "==> effective configuration"
sshd -T | grep -E '^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication|pubkeyauthentication|maxauthtries|logingracetime)' | sed 's/^/    /'

# Assert rather than report. The failure this catches is a drop-in that is present, correct
# and outranked -- which reads as success in every check that looks at the file.
fail=0
[ "$(sshd -T | awk '/^passwordauthentication /{print $2}')" = no ] || { echo "FAIL: password authentication is still enabled" >&2; fail=1; }
[ "$(sshd -T | awk '/^permitrootlogin /{print $2}')" = no ] || { echo "FAIL: root login is still permitted" >&2; fail=1; }
[ "$fail" -eq 0 ] || { echo "sshd is NOT hardened; check for a lower-sorting file in /etc/ssh/sshd_config.d/" >&2; exit 1; }

echo
echo "Existing sessions stay open. Verify a NEW ssh session works before closing this one."
