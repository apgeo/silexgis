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
#   SILEXGIS_DEPLOY_USER        default silexgis
#   SILEXGIS_SSH_ALLOW_ROOT     default 0 -- set 1 to permit direct root logins
#   SILEXGIS_SSH_ALLOW_PASSWORD default 0 -- set 1 to permit password authentication
#
# The two switches exist because an operator may deliberately trade this away for convenient
# remote access. Both are off by default, and each is a stated risk rather than a preference:
# on a public address, permitting root logins removes the record of which person did what, and
# permitting passwords exposes every account to continuous credential guessing. fail2ban,
# installed by provision-host.sh, blunts the second without removing it.
set -euo pipefail

DEPLOY_USER="${SILEXGIS_DEPLOY_USER:-silexgis}"
ALLOW_ROOT="${SILEXGIS_SSH_ALLOW_ROOT:-0}"
ALLOW_PASSWORD="${SILEXGIS_SSH_ALLOW_PASSWORD:-0}"
KEYS="/home/$DEPLOY_USER/.ssh/authorized_keys"

if [ "$ALLOW_ROOT" = 1 ]; then ROOT_SETTING=yes; else ROOT_SETTING=no; fi
if [ "$ALLOW_PASSWORD" = 1 ]; then PW_SETTING=yes; else PW_SETTING=no; fi

[ "$(id -u)" -eq 0 ] || { echo "must run as root" >&2; exit 1; }

if [ ! -s "$KEYS" ] && [ "$ALLOW_PASSWORD" != 1 ] && [ "$ALLOW_ROOT" != 1 ]; then
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
cat > /etc/ssh/sshd_config.d/01-silexgis.conf <<EOF
# Written by harden-ssh.sh. Recovery, should a key be lost, is the provider's console.
PermitRootLogin ${ROOT_SETTING}
PasswordAuthentication ${PW_SETTING}
KbdInteractiveAuthentication ${PW_SETTING}
ChallengeResponseAuthentication ${PW_SETTING}
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
[ "$(sshd -T | awk '/^passwordauthentication /{print $2}')" = "$PW_SETTING" ] || { echo "FAIL: passwordauthentication is not '$PW_SETTING'" >&2; fail=1; }
[ "$(sshd -T | awk '/^permitrootlogin /{print $2}')" = "$ROOT_SETTING" ] || { echo "FAIL: permitrootlogin is not '$ROOT_SETTING'" >&2; fail=1; }
[ "$fail" -eq 0 ] || { echo "effective sshd config does not match what was asked for; look for a lower-sorting file in /etc/ssh/sshd_config.d/" >&2; exit 1; }

if [ "$ALLOW_ROOT" = 1 ] || [ "$ALLOW_PASSWORD" = 1 ]; then
	echo
	echo "NOTE: this host now permits root logins=$ROOT_SETTING, password auth=$PW_SETTING."
	echo "      Deliberate, and reversible by re-running this script without the switches."
fi

echo
echo "Existing sessions stay open. Verify a NEW ssh session works before closing this one."
