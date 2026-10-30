// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A survey sent from a field device arrives one file per request, each file the raw body of a
// PUT — a phone's survey archive with its sketches and photos is the largest of them, up to the
// configured `SILEXGIS__SurveyUploads__MaxFileBytes`. Both web-tier configurations stand in front
// of that route, and a body nginx refuses never reaches the application: the device would be
// told "413" by a proxy instead of a sentence naming the limit. So each configuration's body
// limit for the API is held at or above the documented default, and the body is streamed through
// rather than spooled whole to the proxy's disk first.
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
    .join('\n')
    // A placeholder's braces are not a block's; keep them from being counted as one.
    .replace(/\$\{(\w+)\}/g, '@@$1@@');

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

/** The text of a block with every nested block taken out, so only its own directives remain. */
function ownDirectives(block) {
  const inner = block.slice(block.indexOf('{') + 1, block.lastIndexOf('}'));
  let depth = 0;
  let own = '';
  for (const ch of inner) {
    if (ch === '{') depth++;
    if (depth === 0) own += ch;
    if (ch === '}') depth--;
  }
  return own;
}

/** An nginx size (`1024m`, `250M`, `2g`, a bare byte count) in bytes. */
function bytesOf(size) {
  const match = /^(\d+)([kmg]?)$/i.exec(size);
  assert.ok(match, `not an nginx size: ${size}`);
  const unit = { '': 1, k: 1024, m: 1024 ** 2, g: 1024 ** 3 }[match[2].toLowerCase()];
  return Number(match[1]) * unit;
}

/**
 * A limit as the configuration writes it. The packaged image takes its limit from a variable the web
 * container expands at start-up, so what applies when nobody sets one is the default the image
 * declares for it.
 */
function resolved(value) {
  const variable = /^@@(SILEXGIS__Web__\w+)@@$/.exec(value) ?? /^\$\{(SILEXGIS__Web__\w+)\}$/.exec(value);
  if (!variable) return value;
  const declared = new RegExp(`^ENV\\s+${variable[1]}="([^"]*)"\\s*$`, 'm').exec(read('client', 'Dockerfile'));
  assert.ok(declared, `client/Dockerfile declares no default for ${variable[1]}`);
  return declared[1];
}

/** The body limit a request to the API meets: the API location's own, else the server's. */
function apiBodyLimit(config) {
  const text = directives(config);
  const api = blockOf(text, 'location ~ ^/(api|connect|health|openapi)(/|$) {');
  const pattern = /^\s*client_max_body_size\s+(\S+);/m;
  const own = pattern.exec(ownDirectives(api));
  if (own) return bytesOf(resolved(own[1]));
  const server = pattern.exec(ownDirectives(blockOf(text, 'server {')));
  assert.ok(server, 'neither the API location nor the server sets client_max_body_size');
  return bytesOf(resolved(server[1]));
}

/** The per-file cap the deployment template documents, which is the API's own default. */
function documentedSurveyFileCap() {
  const line = /^#\s*SILEXGIS__SurveyUploads__MaxFileBytes=(\d+)\s*$/m.exec(read('deploy', '.env.example'));
  assert.ok(line, 'deploy/.env.example documents no SILEXGIS__SurveyUploads__MaxFileBytes');
  return Number(line[1]);
}

describe('one file of a survey sent from a field device', () => {
  it('is documented at 250 MB, the API default', () => {
    assert.equal(documentedSurveyFileCap(), 250 * 1024 * 1024);
  });

  for (const [name, config] of Object.entries(configs)) {
    it(`gets through the ${name} configuration's body limit`, () => {
      assert.ok(
        apiBodyLimit(config) >= documentedSurveyFileCap(),
        `the ${name} configuration refuses a body below SILEXGIS__SurveyUploads__MaxFileBytes`,
      );
    });

    it(`is streamed through by the ${name} configuration rather than spooled whole`, () => {
      const api = ownDirectives(blockOf(directives(config), 'location ~ ^/(api|connect|health|openapi)(/|$) {'));
      assert.match(api, /^\s*proxy_request_buffering\s+off;/m);
    });
  }
});

describe("the packaged image's body limit", () => {
  it('is an environment setting whose default is the 1 GB the image always had', () => {
    assert.equal(bytesOf(resolved('${SILEXGIS__Web__MaxBodySize}')), 1024 ** 3);
  });

  it('is reachable from the compose file and survives the template filter', () => {
    const compose = read('deploy', 'docker-compose.yml');
    assert.match(compose, /^\s*SILEXGIS__Web__MaxBodySize:\s*\$\{SILEXGIS_MAX_BODY_SIZE:-1024m\}\s*$/m);
    // Only variables with this prefix are expanded; any other would be left in the file verbatim.
    assert.match(read('client', 'Dockerfile'), /^ENV NGINX_ENVSUBST_FILTER="\^SILEXGIS__Web__"/m);
  });

  it('is documented where an operator looks', () => {
    assert.match(read('deploy', '.env.example'), /^# SILEXGIS_MAX_BODY_SIZE=1024m$/m);
    assert.match(read('docs', 'INSTALL.md'), /`SILEXGIS_MAX_BODY_SIZE`/);
  });
});
