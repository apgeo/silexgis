// SPDX-License-Identifier: AGPL-3.0-or-later
//
// How the installation's own basemap reaches a browser. Five files have to agree and none of them
// can be checked by running the application: the api's directory and its switch, the overlay that
// runs the tile server, the volume both of them mount, the location that passes tile requests on,
// and the image that carries the extracting tool. Every disagreement between them fails silently:
//
//   - the switch on with no tile server behind it offers an entry whose every tile answers 502;
//   - a location passing more than tile addresses publishes the tile server's own catalogue and
//     whatever else it answers, to anybody who can reach the site;
//   - a year-long cache on an address that can be reused shows last month's extract for a year.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const read = (...parts) => readFileSync(join(deployDir, ...parts), 'utf8');

const base = read('docker-compose.yml');
const overlay = read('docker-compose.basemap.yml');
const sample = read('.env.example');
const packaged = readFileSync(join(deployDir, '..', 'client', 'nginx.conf'), 'utf8');
const apiImage = readFileSync(join(deployDir, '..', 'server', 'Dockerfile'), 'utf8');

/** A service's block of a compose file, from its name to the next top-level-indented key. */
const service = (compose, name) => {
  const start = compose.search(new RegExp(`^  ${name}:$`, 'm'));
  assert.notEqual(start, -1, `the compose file must define ${name}`);
  const rest = compose.slice(start + 1);
  const end = rest.search(/^(  [a-z]|[a-z])/m);
  return end === -1 ? rest : rest.slice(0, end);
};

describe('serving the installation’s own basemap', () => {
  it('ties the switch to the overlay that runs the tile server', () => {
    // Off in the base stack, which runs no tile server; on in the overlay, which does.
    assert.match(service(base, 'api'), /SILEXGIS__Basemap__SelfHosted: "false"/);
    assert.match(service(overlay, 'api'), /SILEXGIS__Basemap__SelfHosted: "true"/);
    // And the default install has the overlay, with a separator that reads the same on Windows.
    assert.match(sample, /^COMPOSE_PATH_SEPARATOR=:$/m);
    assert.match(sample, /^COMPOSE_FILE=docker-compose\.yml:docker-compose\.basemap\.yml$/m);
  });

  it('keeps the extract on one named volume the api writes and the tile server only reads', () => {
    assert.match(service(base, 'api'), /SILEXGIS__Basemap__Directory: \/data\/basemap/);
    assert.match(service(base, 'api'), /- silexgis-basemap:\/data\/basemap$/m);
    assert.match(service(overlay, 'martin'), /- silexgis-basemap:\/data:ro$/m);
    assert.match(base, /^volumes:[\s\S]*^  silexgis-basemap:$/m);
    // The api image creates the mount point as its own user, so a fresh volume is writable.
    assert.match(apiImage, /RUN mkdir -p [^\n]*\/data\/basemap[^\n]*chown -R app:app/);
  });

  it('runs a pinned tile server that publishes no port and watches its directory', () => {
    const martin = service(overlay, 'martin');
    assert.match(martin, /image: ghcr\.io\/maplibre\/martin:\d+\.\d+\.\d+$/m);
    assert.doesNotMatch(martin, /ports:/);
    assert.match(martin, /command: \["\/data"\]/);
  });

  it('passes on the tile addresses of an extract and nothing else, and caches them for a year', () => {
    const start = packaged.indexOf('location ~ "^/basemap/tiles/');
    assert.notEqual(start, -1, 'the web image must pass tile requests to the tile server');
    // To the block's own closing line: its pattern holds braces of its own.
    const block = packaged.slice(start, packaged.indexOf('\n    }\n', start));
    // Only an extract's name as the application writes it, then z/x/y.
    assert.match(block, /basemap-\[0-9\]\{8\}-\[0-9\]\{14\}/);
    assert.match(block, /proxy_pass http:\/\/silexgis_basemap\/\$basemap_extract\/\$basemap_z\/\$basemap_x\/\$basemap_y;/);
    assert.match(block, /Cache-Control "public, max-age=31536000, immutable"/);
    // Everything else under /basemap/ answers 404 rather than reaching the tile server.
    assert.match(packaged, /location \/basemap\/ \{\s*return 404;\s*\}/);
    // Found at request time, so the site starts where the overlay is not used.
    assert.match(packaged, /upstream silexgis_basemap \{[^}]*server martin:3000 resolve;/);
  });

  it('carries the extracting tool in the api image, pinned by version and checksum', () => {
    assert.match(apiImage, /ARG PMTILES_VERSION=\d+\.\d+\.\d+/);
    assert.match(apiImage, /amd64\)[^;]*;\s*\\\s*sum=[0-9a-f]{64}/);
    assert.match(apiImage, /arm64\)[^;]*;\s*\\\s*sum=[0-9a-f]{64}/);
    assert.match(apiImage, /sha256sum -c -/);
    assert.match(apiImage, /COPY --from=pmtiles \/out\/pmtiles \/usr\/local\/bin\/pmtiles/);
  });
});
