// SPDX-License-Identifier: AGPL-3.0-or-later
//
// How a pyramid the application built for itself reaches a browser. Four files have to agree and
// none of them can be checked by running the application: the API's configured publish directory,
// the volume the web front mounts, the location that serves it, and the rule deciding how long each
// file may be kept. Every disagreement between them fails silently.
//
//   - a rule aimed one directory above the published pyramids serves the operator's own source
//     rasters to anybody who can reach the site, with no account and no request reaching the
//     application at all;
//   - a manifest that inherits the tiles' week-long cache leaves browsers building addresses from a
//     version that no longer exists, answering every one of them out of their own cache and sending
//     no request — ground drawn from elevation the installation no longer has, and no error.
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
const packaged = readFileSync(join(deployDir, '..', 'client', 'nginx.conf'), 'utf8');
const standalone = read('nginx', 'silexgis.conf');
const apiImage = readFileSync(join(deployDir, '..', 'server', 'Dockerfile'), 'utf8');

/** The location block for published builds, whichever configuration it is read from. */
const publishedBlock = (config) => {
  const start = config.indexOf('location /terrain/builds/ {');
  assert.notEqual(start, -1, 'a configuration must serve the address published builds are at');
  return config.slice(start, config.indexOf('}', start));
};

describe('serving a pyramid the application built', () => {
  it('publishes into a directory of its own, and mounts that same volume for serving', () => {
    assert.match(base, /SILEXGIS__Terrain__PublishRoot: \/data\/terrain\/published/);
    assert.match(base, /- silexgis-terrain:\/srv\/terrain:ro$/m);
    // Owned by the application's user before it drops privileges, like every other mount point:
    // a directory Docker seeds root-owned is a first publication that cannot write.
    assert.match(apiImage, /RUN mkdir -p [^\n]*\/data\/terrain\/published[^\n]*chown -R app:app/);
  });

  it('aliases the published pyramids and nothing above them', () => {
    // Both configurations, because the one an operator edits by hand is the one where aiming a
    // directory too high is easiest to do and impossible to notice: it answers 200 either way.
    assert.match(publishedBlock(packaged), /alias \/srv\/terrain\/published\/;/);
    assert.match(publishedBlock(standalone), /alias \/var\/lib\/silexgis\/terrain\/published\/;/);

    // The whole point of the directory: everything under whatever is aliased is downloadable by
    // anybody who can reach the site. A build keeps the rasters it was given, and the intermediates
    // made from them, beside its tiles — so naming the volume root, the build root or one build's
    // folder publishes an operator's own data.
    for (const config of [packaged, standalone]) {
      const block = publishedBlock(config);
      for (const tooHigh of [
        '/srv/terrain/;',
        '/srv/terrain/builds/;',
        '/data/terrain/;',
        '/var/lib/silexgis/terrain/;',
        '/var/lib/silexgis/terrain/builds/;',
      ]) {
        assert.ok(
          !block.includes(`alias ${tooHigh}`),
          `the serving rule must not be aimed at ${tooHigh}`,
        );
      }
    }
  });

  it('keeps the manifest out of the cache its tiles are kept in', () => {
    for (const config of [packaged, standalone]) {
      // Not an exact path: each build is served from an address of its own, so a rule naming one
      // fixed manifest matches nothing and every manifest quietly inherits a week of caching.
      assert.match(config, /"~\^\/terrain\/builds\/\[\^\/\]\+\/layer\\\.json\$"\s+"no-cache"/);
      assert.match(config, /default\s+"public, max-age=604800"/);
      assert.match(publishedBlock(config), /add_header Cache-Control \$terrain_build_cache;/);
    }
  });

  it('declares no encoding it has not applied, and compresses nothing on the fly', () => {
    for (const config of [packaged, standalone]) {
      const block = publishedBlock(config);
      // A tile is a binary mesh, and a browser handed one whose declared encoding is wrong reports
      // nothing: 200, empty console, and a globe with no ground on it. gzip_static serves a sibling
      // .gz where one exists and labels it itself, which is the only way to get the pairing right
      // that cannot also get it wrong.
      assert.match(block, /gzip off;/);
      assert.match(block, /gzip_static on;/);
      assert.ok(!/add_header Content-Encoding/.test(block));
    }
  });
});
