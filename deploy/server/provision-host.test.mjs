// SPDX-License-Identifier: AGPL-3.0-or-later
//
// provision-host.sh is re-run on a live host to re-apply its configuration, and two of its steps
// can take that host down: the Docker daemon settings (a wrong file, or a needless restart, stops
// every container) and the firewall (a reset drops the rules an operator added by hand).
//
// The script is sourced, which defines its steps without running any of them, and each step is run
// against a scratch configuration directory with stand-ins for `systemctl`, `dockerd` and `ufw` that
// record what they were asked. jq is the real one: the merge is what is under test.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const script = join(here, 'provision-host.sh');

const hasJq = process.platform !== 'win32' && spawnSync('jq', ['--version']).status === 0;
const SKIP =
  (process.platform === 'win32' && 'provision-host.sh is a bash script for an Ubuntu host') ||
  (!hasJq && 'jq is not installed here; the script installs it on the host before this step');

const RECORDER = (name) => `#!/usr/bin/env bash
echo "${name} $*" >> "$STANDIN_LOG"
exit 0
`;
// Answers --help as an engine that knows --validate, and rejects a file when told to.
const DOCKERD = `#!/usr/bin/env bash
case "$1" in
--help) echo "      --validate       Validate daemon configuration and exit"; exit 0 ;;
--validate) echo "dockerd $*" >> "$STANDIN_LOG"; exit "\${STANDIN_DOCKERD_REJECT:-0}" ;;
esac
exit 0
`;

function sandbox() {
  const root = mkdtempSync(join(tmpdir(), 'silexgis-provision-'));
  const bin = join(root, 'bin');
  const etc = join(root, 'etc-docker');
  mkdirSync(bin);
  mkdirSync(etc);
  for (const [name, text] of [
    ['systemctl', RECORDER('systemctl')],
    ['ufw', RECORDER('ufw')],
    ['dockerd', DOCKERD],
  ]) {
    writeFileSync(join(bin, name), text);
    chmodSync(join(bin, name), 0o755);
  }
  const log = join(root, 'calls.log');
  return {
    root,
    etc,
    daemonJson: join(etc, 'daemon.json'),
    calls: () => (existsSync(log) ? readFileSync(log, 'utf8') : ''),
    step(call, env = {}) {
      return spawnSync('bash', ['-c', `. "$1" && ${call}`, 'step', script], {
        encoding: 'utf8',
        env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, STANDIN_LOG: log, ...env },
      });
    },
  };
}

function withBox(fn) {
  const box = sandbox();
  try {
    fn(box);
  } finally {
    rmSync(box.root, { recursive: true, force: true });
  }
}

const OURS = { 'log-driver': 'json-file', 'live-restore': true };

describe('provision-host.sh: the Docker daemon settings', { skip: SKIP }, () => {
  it('sourcing the script runs nothing', () =>
    withBox((box) => {
      const r = box.step('true');
      assert.equal(r.status, 0, r.stderr);
      assert.equal(box.calls(), '');
      assert.equal(r.stdout, '');
    }));

  it('writes its settings on a host that has none, and restarts the daemon for them', () =>
    withBox((box) => {
      const r = box.step(`configure_docker_daemon "${box.etc}"`);
      assert.equal(r.status, 0, r.stderr);
      const written = JSON.parse(readFileSync(box.daemonJson, 'utf8'));
      assert.deepEqual(written, { ...OURS, 'log-opts': { 'max-size': '10m', 'max-file': '5' } });
      assert.match(box.calls(), /^dockerd --validate --config-file /m, 'the daemon was asked first');
      assert.match(box.calls(), /^systemctl restart docker$/m);
    }));

  it("keeps the operator's own settings and log options, and restarts only when the file changed", () =>
    withBox((box) => {
      const operator = {
        'data-root': '/mnt/second-disk/docker',
        'registry-mirrors': ['https://mirror.example.invalid'],
        'log-opts': { compress: 'true', 'max-size': '50m' },
      };
      writeFileSync(box.daemonJson, JSON.stringify(operator));
      const first = box.step(`configure_docker_daemon "${box.etc}"`);
      assert.equal(first.status, 0, first.stderr);
      const merged = JSON.parse(readFileSync(box.daemonJson, 'utf8'));
      assert.equal(merged['data-root'], operator['data-root']);
      assert.deepEqual(merged['registry-mirrors'], operator['registry-mirrors']);
      assert.deepEqual(merged['log-opts'], { compress: 'true', 'max-size': '10m', 'max-file': '5' });
      assert.equal(merged['log-driver'], 'json-file');
      assert.equal(merged['live-restore'], true);
      assert.equal(box.calls().match(/^systemctl restart docker$/gm)?.length, 1);

      const before = readFileSync(box.daemonJson, 'utf8');
      const again = box.step(`configure_docker_daemon "${box.etc}"`);
      assert.equal(again.status, 0, again.stderr);
      assert.equal(readFileSync(box.daemonJson, 'utf8'), before);
      assert.equal(box.calls().match(/^systemctl restart docker$/gm)?.length, 1, 'an unchanged file does not restart the daemon');
      assert.match(box.calls(), /^systemctl enable --now docker$/m);
      assert.deepEqual(readdirSync(box.etc), ['daemon.json'], 'no scratch file is left beside it');
    }));

  it("drops another log driver's options rather than hand them to json-file", () =>
    withBox((box) => {
      writeFileSync(box.daemonJson, JSON.stringify({ 'log-driver': 'syslog', 'log-opts': { 'syslog-address': 'udp://logs.example.invalid:514' }, debug: true }));
      const r = box.step(`configure_docker_daemon "${box.etc}"`);
      assert.equal(r.status, 0, r.stderr);
      const merged = JSON.parse(readFileSync(box.daemonJson, 'utf8'));
      assert.deepEqual(merged['log-opts'], { 'max-size': '10m', 'max-file': '5' });
      assert.equal(merged['log-driver'], 'json-file');
      assert.equal(merged.debug, true, 'the rest of the file is kept');
      assert.match(r.stderr, /log driver was syslog/);
    }));

  it('stops on a daemon.json that is not JSON, leaving it as it was and the daemon running', () =>
    withBox((box) => {
      writeFileSync(box.daemonJson, '{ "data-root": "/mnt/second-disk/docker", }\n');
      const r = box.step(`configure_docker_daemon "${box.etc}"; echo "carried on"`);
      assert.notEqual(r.status, 0);
      assert.doesNotMatch(r.stdout, /carried on/, 'the script stops at the failed step');
      assert.match(r.stderr, /is not valid JSON/);
      assert.equal(readFileSync(box.daemonJson, 'utf8'), '{ "data-root": "/mnt/second-disk/docker", }\n');
      assert.doesNotMatch(box.calls(), /systemctl/);
      assert.deepEqual(readdirSync(box.etc), ['daemon.json']);
    }));

  it('stops when the daemon rejects the merged file, without restarting it', () =>
    withBox((box) => {
      const original = JSON.stringify({ 'data-root': '/mnt/second-disk/docker' });
      writeFileSync(box.daemonJson, original);
      const r = box.step(`configure_docker_daemon "${box.etc}"; echo "carried on"`, { STANDIN_DOCKERD_REJECT: '1' });
      assert.notEqual(r.status, 0);
      assert.doesNotMatch(r.stdout, /carried on/);
      assert.match(r.stderr, /rejects the merged/);
      assert.equal(readFileSync(box.daemonJson, 'utf8'), original);
      assert.doesNotMatch(box.calls(), /systemctl/);
      assert.deepEqual(readdirSync(box.etc), ['daemon.json']);
    }));
});

describe('provision-host.sh: the firewall', { skip: SKIP }, () => {
  const ufwCalls = (box) => box.calls().split('\n').filter((l) => l.startsWith('ufw '));

  it('adds its rules without resetting the ones already there', () =>
    withBox((box) => {
      const r = box.step('configure_firewall', { SILEXGIS_SSH_PORT: '2222' });
      assert.equal(r.status, 0, r.stderr);
      const calls = ufwCalls(box);
      assert.deepEqual(
        calls.filter((c) => !/^ufw (allow|default|--force enable|status)/.test(c)),
        [],
        'nothing but adding rules, the defaults, enabling and reporting',
      );
      assert.ok(!calls.some((c) => /\breset\b/.test(c)), 'ufw is never reset');
      assert.ok(calls.includes("ufw allow 2222/tcp comment ssh"));
      assert.ok(calls.includes('ufw allow 80/tcp comment silexgis web'));
      assert.ok(!calls.some((c) => c.startsWith('ufw allow 443/')), '443 only when asked for');
    }));

  it('opens 443 when the TLS overlay is used', () =>
    withBox((box) => {
      const r = box.step('configure_firewall', { SILEXGIS_HTTPS: '1' });
      assert.equal(r.status, 0, r.stderr);
      assert.ok(ufwCalls(box).includes('ufw allow 443/tcp comment silexgis web tls'));
    }));
});
