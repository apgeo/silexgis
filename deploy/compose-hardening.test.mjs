// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Operational limits the deployment files are expected to carry, pinned where they are written.
//
//   - every service a compose file defines has a bounded log (docker's default `json-file` driver
//     never rotates, so an unbounded container log is a disk that fills in the night);
//   - the application and the web server report whether they are healthy, and the web server waits
//     for the application, so `docker compose up` does not announce success over a front that has
//     nothing behind it;
//   - the compose files still resolve together, for the combinations an installation uses.
//
// The last group runs `docker compose config` and is skipped on a machine that has no compose.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = path.dirname(fileURLToPath(import.meta.url));
const read = (name) => fs.readFileSync(path.join(deployDir, name), 'utf8');
const composeFiles = fs.readdirSync(deployDir).filter((name) => /^docker-compose.*\.yml$/.test(name));

/** The services a file defines, each with the text of its own block. */
function servicesOf(text) {
  const lines = text.split('\n');
  const start = lines.findIndex((line) => /^services:\s*$/.test(line));
  assert.notEqual(start, -1, 'a compose file with a services section');
  const services = {};
  let current = null;
  for (const line of lines.slice(start + 1)) {
    if (/^\S/.test(line) && !line.startsWith('#')) break; // the next top-level section
    const header = /^  ([A-Za-z0-9_-]+):\s*$/.exec(line);
    if (header) {
      current = header[1];
      services[current] = [];
    } else if (current) {
      services[current].push(line);
    }
  }
  return Object.fromEntries(Object.entries(services).map(([name, body]) => [name, body.join('\n')]));
}

// Development stacks are started by hand on a developer's machine and thrown away; they define their
// own database and API with throwaway settings and are not what an installation runs.
const developmentOnly = new Set(['docker-compose.dev.yml', 'docker-compose.speleoloc-dev.yml']);

describe('every service has a bounded log', () => {
  for (const file of composeFiles.filter((name) => !developmentOnly.has(name))) {
    it(`${file}`, () => {
      for (const [name, body] of Object.entries(servicesOf(read(file)))) {
        // An overlay that only adjusts a service defined elsewhere names neither an image nor a build.
        if (!/^    (image|build):/m.test(body)) continue;
        assert.match(body, /^    logging:\s*$/m, `${file}: ${name} has no logging section`);
        assert.match(body, /^      driver: json-file\s*$/m, `${file}: ${name} does not use json-file`);
        assert.match(body, /^        max-size: "20m"\s*$/m, `${file}: ${name} has no size bound`);
        assert.match(body, /^        max-file: "5"\s*$/m, `${file}: ${name} has no file-count bound`);
      }
    });
  }

  it('covers the three services of the base file', () => {
    const services = servicesOf(read('docker-compose.yml'));
    assert.deepEqual(Object.keys(services).sort(), ['api', 'db', 'web']);
  });
});

describe('the application and the web server report their health', () => {
  const services = servicesOf(read('docker-compose.yml'));

  it('probes the application at its readiness address, with no tool the image lacks', () => {
    const api = services.api;
    assert.match(api, /^    healthcheck:\s*$/m);
    assert.match(api, /GET \/health\/ready HTTP\/1\.0/);
    assert.match(api, /\/dev\/tcp\/127\.0\.0\.1\/8080/);
    // The runtime image has bash and neither curl nor wget; a probe naming one would be unhealthy
    // from the first second and nothing would say why.
    assert.ok(!/\b(curl|wget)\b/.test(api.replace(/^\s*#.*$/gm, '')), 'the api probe names a tool its image lacks');
    assert.match(api, /start_period: \d+s/, 'a slow first start (migrations) must not count as unhealthy');
  });

  it('probes the web server on its own front page, over IPv4', () => {
    const web = services.web;
    assert.match(web, /^    healthcheck:\s*$/m);
    assert.match(web, /"wget", "-q", "-O", "\/dev\/null", "http:\/\/127\.0\.0\.1\/"/);
  });

  it('starts the web server only once the application is healthy', () => {
    assert.match(services.web, /depends_on:\s*\n\s+api:\s*\n\s+condition: service_healthy/);
  });

  it('still lets the database gate the application', () => {
    assert.match(services.api, /depends_on:\s*\n\s+db:\s*\n\s+condition: service_healthy/);
  });
});

describe('the compose files resolve together', () => {
  const compose = spawnSync('docker', ['compose', 'version'], { encoding: 'utf8' });
  const available = compose.status === 0;

  // Values for the settings the files insist on (`${NAME:?…}`); none is used for anything but
  // letting the file resolve.
  const required = {
    SILEXGIS_DB_PASSWORD: 'x',
    SILEXGIS_DOMAIN: 'caves.example.org',
    SILEXGIS_TLS_EMAIL: 'admin@example.org',
    SILEXGIS_TERRAIN_DIR: '/srv/terrain-tiles',
    SILEXGIS__Terrain__BakeEnabled: 'true',
    SILEXGIS__SurveyCompiler__Mode: 'service',
    SILEXGIS_PGADMIN_PASSWORD: 'x',
    SILEXGIS_PGADMIN_EMAIL: 'admin@example.org',
    SILEXGIS_IMMICH_VERSION: 'v3.0.0',
    SILEXGIS_IMMICH_DB_PASSWORD: 'x',
    SILEXGIS_IMMICH_LIBRARY_DIR: '/srv/photos',
    SILEXGIS_PHOTOPRISM_VERSION: '260101',
    SILEXGIS_PHOTOPRISM_ADMIN_PASSWORD: 'long-enough-password',
    SILEXGIS_PHOTOPRISM_LIBRARY_DIR: '/srv/photos',
  };

  const combinations = {
    'the base file alone': ['docker-compose.yml'],
    'with TLS': ['docker-compose.yml', 'docker-compose.tls.yml'],
    'with terrain': ['docker-compose.yml', 'docker-compose.terrain.yml'],
    'with every optional service': [
      'docker-compose.yml',
      'docker-compose.basemap.yml',
      'docker-compose.terrain-worker.yml',
      'docker-compose.therion.yml',
      'docker-compose.convert.yml',
      'docker-compose.pgadmin.yml',
      'docker-compose.dbport.yml',
      'docker-compose.tls.yml',
    ],
    'with Immich': ['docker-compose.yml', 'docker-compose.immich.yml'],
    'with PhotoPrism': ['docker-compose.yml', 'docker-compose.photoprism.yml'],
  };

  for (const [name, files] of Object.entries(combinations)) {
    it(name, { skip: available ? false : 'docker compose is not installed' }, () => {
      // The files name `.env` as an env_file, so it must exist beside them; a scratch copy keeps the
      // working tree clean. Build contexts are not read by `config`.
      const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sx-compose-'));
      try {
        for (const file of composeFiles) fs.copyFileSync(path.join(deployDir, file), path.join(dir, file));
        fs.copyFileSync(path.join(deployDir, '.env.example'), path.join(dir, '.env'));
        const result = spawnSync('docker', ['compose', ...files.flatMap((f) => ['-f', f]), 'config', '-q'], {
          cwd: dir,
          encoding: 'utf8',
          env: { ...process.env, ...required, COMPOSE_FILE: '' },
        });
        assert.equal(result.status, 0, result.stderr);
      } finally {
        fs.rmSync(dir, { recursive: true, force: true });
      }
    });
  }
});
