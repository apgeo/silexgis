#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Point a deployed SilexGIS at a hostname: update the reverse proxy, the public URL the
# application builds links and OIDC redirects from, and the TLS contact address.
#
# Usage, as the deploy account:
#   set-domain.sh --primary caves.example.org --email you@example.org
#   set-domain.sh --primary caves.example.org --also old.example.org --email you@example.org
#
#   --primary  the canonical hostname. PublicUrl becomes https://<primary>, so this is the
#              address in emails, share links, and the OIDC issuer and redirect URI.
#   --also     an additional hostname the proxy should also serve and accept sign-ins on.
#              Repeatable. Use it to keep an old address working across a move: the SPA takes
#              its authority from the address in the browser's bar, so each extra name needs
#              its callback registered or sign-in through that name is refused.
#   --email    Let's Encrypt account contact (expiry and revocation notices).
#   --no-restart  write the configuration but do not restart anything.
#
# Every name given must already resolve to this host, over BOTH A and AAAA where both exist:
# Let's Encrypt validates over whichever it picks, and a stale record pointing somewhere else
# fails the challenge with no hint that DNS rather than the server is at fault. The script
# checks and refuses by default -- pass --skip-dns-check to configure ahead of a DNS change.
set -euo pipefail

APP_DIR="${SILEXGIS_APP_DIR:-/opt/silexgis}"
ENV_FILE="$APP_DIR/deploy/.env"
PRIMARY=""; EMAIL=""; ALSO=(); RESTART=1; DNS_CHECK=1

while [ $# -gt 0 ]; do
	case "$1" in
		--primary) PRIMARY="$2"; shift 2 ;;
		--also)    ALSO+=("$2"); shift 2 ;;
		--email)   EMAIL="$2"; shift 2 ;;
		--no-restart) RESTART=0; shift ;;
		--skip-dns-check) DNS_CHECK=0; shift ;;
		*) echo "unknown argument: $1" >&2; exit 2 ;;
	esac
done
[ -n "$PRIMARY" ] || { echo "--primary is required" >&2; exit 2; }
[ -f "$ENV_FILE" ] || { echo "$ENV_FILE not found" >&2; exit 1; }

say() { printf '\n==> %s\n' "$*"; }

# The address this host actually answers on, as seen from outside. Used only to tell the
# operator what the DNS records ought to say.
MYV4="$(curl -4 -fsS --max-time 10 https://api.ipify.org 2>/dev/null || echo '')"

check_name() {
	local name="$1" ok=1
	local a aaaa
	a="$(getent ahostsv4 "$name" 2>/dev/null | awk '{print $1}' | sort -u | tr '\n' ' ')"
	aaaa="$(getent ahostsv6 "$name" 2>/dev/null | awk '{print $1}' | sort -u | grep -v '^::ffff:' | tr '\n' ' ')"
	printf '    %-34s A: %s\n' "$name" "${a:-<none>}"
	printf '    %-34s AAAA: %s\n' "" "${aaaa:-<none>}"
	if [ -n "$MYV4" ] && [ -n "$a" ] && ! echo " $a " | grep -q " $MYV4 "; then
		echo "    ^ does not include this host ($MYV4)" >&2; ok=0
	fi
	# More than one A record means round-robin: some visitors, and some ACME validation
	# attempts, will land on the other address. That is a misconfiguration, not redundancy.
	if [ "$(echo $a | wc -w)" -gt 1 ]; then
		echo "    ^ several A records: requests will be split between them" >&2; ok=0
	fi
	# An AAAA that is not this host is the quiet killer: browsers and Let's Encrypt both
	# prefer IPv6, so everything reaches the wrong server while the A record looks correct.
	if [ -n "$aaaa" ]; then
		echo "    ^ has AAAA records; confirm they point at THIS host or remove them" >&2
	fi
	return $ok
}

say "DNS as this host resolves it"
bad=0
for n in "$PRIMARY" ${ALSO+"${ALSO[@]}"}; do check_name "$n" || bad=1; done
if [ "$bad" -ne 0 ] && [ "$DNS_CHECK" = 1 ]; then
	echo
	echo "Refusing: at least one name does not resolve cleanly to this host." >&2
	echo "Fix the records (or pass --skip-dns-check to configure anyway)." >&2
	exit 1
fi

set_env() {
	local key="$1" value="$2"
	if grep -q "^${key}=" "$ENV_FILE"; then
		# One exact key per line, rewritten in place; never a pattern across the file.
		python3 - "$ENV_FILE" "$key" "$value" <<'PY'
import io,sys
path,key,value = sys.argv[1],sys.argv[2],sys.argv[3]
lines = io.open(path,encoding='utf-8').read().split('\n')
out=[]
for line in lines:
    if line.startswith(key+'='):
        out.append(key+'='+value)
    else:
        out.append(line)
io.open(path,'w',encoding='utf-8').write('\n'.join(out))
PY
	else
		printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
	fi
}

# Caddy takes a comma-separated list of site addresses, and the Caddyfile interpolates this
# variable directly, so every name the proxy should answer on goes here.
ALL="$PRIMARY"
for n in ${ALSO+"${ALSO[@]}"}; do ALL="$ALL, $n"; done

say "Writing configuration"
set_env SILEXGIS_DOMAIN "$ALL"
set_env SILEXGIS_PUBLIC_URL "https://$PRIMARY"
[ -n "$EMAIL" ] && set_env SILEXGIS_TLS_EMAIL "$EMAIL"

# The seeder registers {PublicUrl}/auth/callback by itself; the extra names need theirs added
# explicitly, or signing in through one of them is refused with an invalid-redirect error.
python3 - "$ENV_FILE" <<'PY'
import io,sys,re
path = sys.argv[1]
lines = [l for l in io.open(path,encoding='utf-8').read().split('\n')
         if not re.match(r'^SILEXGIS__Auth__AdditionalRedirectUris__\d+=', l)]
io.open(path,'w',encoding='utf-8').write('\n'.join(lines))
PY
i=0
for n in ${ALSO+"${ALSO[@]}"}; do
	printf 'SILEXGIS__Auth__AdditionalRedirectUris__%s=https://%s/auth/callback\n' "$i" "$n" >> "$ENV_FILE"
	i=$((i + 1))
done

grep -E '^(SILEXGIS_DOMAIN|SILEXGIS_PUBLIC_URL|SILEXGIS_TLS_EMAIL|SILEXGIS__Auth__AdditionalRedirectUris)' "$ENV_FILE" | sed 's/^/    /'

if [ "$RESTART" = 1 ]; then
	say "Restarting proxy and application"
	cd "$APP_DIR/deploy"
	docker compose up -d caddy api
	echo "    waiting for a certificate for $PRIMARY (Let's Encrypt, up to ~90s)"
	for _ in $(seq 1 30); do
		if curl -fsS --max-time 5 "https://$PRIMARY/health/ready" >/dev/null 2>&1; then
			echo "    https://$PRIMARY is serving with a trusted certificate"
			exit 0
		fi
		sleep 3
	done
	echo "    not serving yet -- check: docker compose logs caddy | grep -i acme" >&2
	exit 1
fi
