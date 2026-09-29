// SPDX-License-Identifier: AGPL-3.0-or-later
//
// How many proxies stand in front of the API — a number the API is told, and which every
// per-address rate limit turns on.
//
// The failure this exists to catch is silence. Each proxy appends its own peer to
// X-Forwarded-For, and the API walks that header back a stated number of hops to find the
// caller. Under the base compose file there is one hop, nginx, and the caller is the last entry.
// Under the TLS overlay Caddy stands in front of nginx, so the caller is one entry further back;
// told nothing, the API would take Caddy's own address for every caller on the internet, and the
// sign-in, printed-code and published-trip limiters would share a single budget across all of
// them. Nothing errors, nothing logs, and the first symptom is a club's whole readership refused
// together once enough of them open an article.
//
// So the overlay states the hop count on the api service, and this pins that it does, and that
// the setting an operator reaches for is documented under the name the API reads.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(deployDir, '..');
const read = (...parts) => readFileSync(join(repoRoot, ...parts), 'utf8');

const tlsOverlay = read('deploy', 'docker-compose.tls.yml');
const envExample = read('deploy', '.env.example');
const install = read('docs', 'INSTALL.md');

/** A YAML file with its commentary taken out, so a comment cannot satisfy a check. */
const uncommented = (text) =>
  text
    .split('\n')
    .map((line) => line.replace(/\s#.*$/, '').replace(/^#.*$/, ''))
    .join('\n');

/** The body of one top-level service in a compose file. */
function service(compose, name) {
  const match = uncommented(compose).match(new RegExp(`^  ${name}:\\n((?:    .*\\n|\\n)*)`, 'm'));
  assert.ok(match, `${name} is not a service of the overlay`);
  return match[1];
}

describe('the TLS overlay tells the API how many proxies stand in front of it', () => {
  it('sets two hops on the api service — Caddy, then nginx', () => {
    const api = service(tlsOverlay, 'api');
    assert.match(
      api,
      /^\s+SILEXGIS__Proxy__Hops:\s*"?2"?\s*$/m,
      'docker-compose.tls.yml must set SILEXGIS__Proxy__Hops to 2 on the api service',
    );
  });

  it('documents the setting under the name the API reads, for an operator with a proxy of their own', () => {
    assert.match(envExample, /^# SILEXGIS__Proxy__Hops=1$/m);
    assert.match(envExample, /^# SILEXGIS__Proxy__TrustedNetworks__0=/m);
    assert.match(install, /SILEXGIS__Proxy__Hops=2/);
  });
});
