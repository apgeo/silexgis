// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Survey files are compressed on their way to the browser, by both web-tier configurations.
//
// They are stored with no type of their own (a Therion .lox, a Survex .3d), and nginx compresses
// only the types it is told to — so left to the server's list they went over the wire whole, tens
// of megabytes for an expedition's model where a fifth of that would do. Behind the front proxy
// that terminates TLS a request may also carry a Via header, and nginx compresses nothing for such
// a request unless told to: a rule that left `gzip_proxied` out would pass a check on a directly
// connected test server and compress nothing in production.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (...parts) => readFileSync(join(repoRoot, ...parts), 'utf8');

const configs = {
  packaged: read('client', 'nginx.conf'),
  standalone: read('deploy', 'nginx', 'silexgis.conf'),
};

/** A configuration without its comments, which talk about the very directives looked for. */
const directives = (config) =>
  config
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('#'))
    .join('\n');

/** One block, from its opening line to the brace that closes it, counted by depth. */
function blockOf(text, opening) {
  const start = text.indexOf(opening);
  assert.notEqual(start, -1, `no block opens with ${opening}`);
  let depth = 0;
  for (let at = start; at < text.length; at++) {
    if (text[at] === '{') depth++;
    if (text[at] === '}' && --depth === 0) return text.slice(start, at + 1);
  }
  throw new assert.AssertionError({ message: `${opening} is never closed` });
}

describe('a stored file on its way to the browser', () => {
  for (const [name, config] of Object.entries(configs)) {
    it(`is compressed by the ${name} configuration, behind a proxy too, inside the API's own location`, () => {
      const api = blockOf(directives(config), 'location ~ ^/(api|connect|health|openapi)(/|$) {');
      const content = blockOf(api, 'location ~ ^/api/files/[^/]+/content$ {');
      assert.match(content, /^\s*gzip_types\s+application\/octet-stream;/m);
      assert.match(content, /^\s*gzip_proxied\s+any;/m);
      assert.match(content, /^\s*gzip_vary\s+on;/m);
      // A nested location inherits no content handler: without its own it would answer from disk.
      const upstream = /^\s*proxy_pass\s+(\S+);/m.exec(api)[1];
      assert.match(content, new RegExp(`^\\s*proxy_pass\\s+${upstream.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')};`, 'm'));
      // Its own add_header would stop the framing headers inheriting from the API's location.
      assert.doesNotMatch(content, /add_header/);
    });
  }
});
