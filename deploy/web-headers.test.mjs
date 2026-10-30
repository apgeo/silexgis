// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The packaged web server's configuration, run for real: the share page and the embed still load, are
// still framable by the sites named, and everything else is still not; every response carries the
// type and referrer headers; and the proxy body limit is the one the environment sets.
//
// The text-level checks in framing.test.mjs cannot see an nginx that refuses to start, an
// `add_header` that was not inherited, or a placeholder that was not expanded; this starts the
// image's own server on the configuration as the image installs it (as a template expanded with the
// same filter) and asks it. Skipped where Docker or the nginx image is not available.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, before, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const docker = (...args) => spawnSync('docker', args, { encoding: 'utf8' });
const available = docker('image', 'inspect', 'nginx:alpine').status === 0;

describe('the packaged web server answers with the intended headers', { skip: available ? false : 'docker with nginx:alpine is not available' }, () => {
  const name = `sx-web-headers-${process.pid}`;
  const site = fs.mkdtempSync(path.join(os.tmpdir(), 'sx-web-'));
  let base = '';

  before(async () => {
    // The server runs as another user inside the container; a temporary directory is private to ours.
    fs.chmodSync(site, 0o755);
    fs.mkdirSync(path.join(site, 'assets'));
    fs.chmodSync(path.join(site, 'assets'), 0o755);
    fs.writeFileSync(path.join(site, 'index.html'), '<!doctype html><title>shell</title>');
    fs.writeFileSync(path.join(site, 'assets', 'a.js'), 'export {};');
    for (const file of ['index.html', 'assets/a.js']) fs.chmodSync(path.join(site, file), 0o644);
    const started = docker(
      'run', '-d', '--name', name, '-p', '127.0.0.1::80',
      '-e', 'NGINX_ENVSUBST_FILTER=^SILEXGIS__Web__',
      '-e', 'SILEXGIS__Web__FrameAncestors=https://club.example.org',
      '-e', 'SILEXGIS__Web__MaxBodySize=2g',
      '-v', `${path.join(repoRoot, 'client', 'nginx.conf')}:/etc/nginx/templates/default.conf.template:ro`,
      '-v', `${site}:/usr/share/nginx/html:ro`,
      'nginx:alpine',
    );
    assert.equal(started.status, 0, started.stderr);
    const port = /:(\d+)\s*$/m.exec(docker('port', name, '80/tcp').stdout)?.[1];
    assert.ok(port, 'the published port');
    base = `http://127.0.0.1:${port}`;
    for (let attempt = 0; attempt < 50; attempt++) {
      try {
        if ((await fetch(`${base}/`)).ok) return;
      } catch {
        /* not up yet */
      }
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
    assert.fail(`the web server did not answer; log: ${docker('logs', name).stderr.slice(-400)}`);
  });

  after(() => {
    docker('rm', '-f', name);
    fs.rmSync(site, { recursive: true, force: true });
  });

  const hardened = (response, where) => {
    assert.equal(response.headers.get('x-content-type-options'), 'nosniff', `${where}: nosniff`);
    assert.equal(response.headers.get('referrer-policy'), 'strict-origin-when-cross-origin', `${where}: referrer policy`);
  };

  for (const route of ['/shared/trips/abc', '/shared/trips/abc/embed']) {
    it(`${route} loads, may be framed by the named site only, and is hardened`, async () => {
      const response = await fetch(`${base}${route}`);
      assert.equal(response.status, 200);
      assert.match(await response.text(), /shell/);
      assert.equal(response.headers.get('content-security-policy'), "frame-ancestors 'self' https://club.example.org");
      assert.equal(response.headers.get('x-frame-options'), null, 'the framable page sends no X-Frame-Options');
      assert.equal(response.headers.get('x-robots-tag'), 'noindex, nofollow');
      hardened(response, route);
    });
  }

  it('denies framing of the application and hardens it', async () => {
    const response = await fetch(`${base}/`);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('content-security-policy'), "frame-ancestors 'none'");
    assert.equal(response.headers.get('x-frame-options'), 'DENY');
    hardened(response, '/');
  });

  it('hardens a hashed asset, which sets a header of its own', async () => {
    const response = await fetch(`${base}/assets/a.js`);
    assert.equal(response.status, 200);
    assert.match(response.headers.get('cache-control') ?? '', /immutable/);
    hardened(response, '/assets/a.js');
  });

  it('hardens a response the API location produces, even when there is no API behind it', async () => {
    const response = await fetch(`${base}/api/v1/anything`);
    assert.equal(response.status, 502);
    assert.equal(response.headers.get('x-frame-options'), 'DENY');
    hardened(response, '/api');
  });

  it('takes the proxy body limit from the environment', () => {
    const rendered = docker('exec', name, 'grep', '-n', 'client_max_body_size', '/etc/nginx/conf.d/default.conf').stdout;
    assert.match(rendered, /client_max_body_size 2g;/);
    assert.ok(!rendered.includes('${'), 'no placeholder is left unexpanded');
  });
});
