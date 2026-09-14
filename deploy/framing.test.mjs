// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Who may put this application inside a frame — a rule stated in four files, in three languages,
// none of which can be checked by running the application.
//
// The failure this exists to catch is silence. A missing framing header is not an error anywhere:
// every page still loads, every test still passes, nothing appears in a log, and the only
// observable difference is that a page somewhere else on the internet can load this one in an
// invisible frame over its own controls and collect a signed-in reader's clicks. The opposite
// mistake is just as quiet from this side — a published trip denied framing shows a blank box on
// somebody else's website, and this application never hears about it.
//
// So the rule is pinned where it is written:
//   - client/nginx.conf        the packaged web server, whose one operator setting is expanded in
//   - client/Dockerfile        how that expansion happens, and the filter that keeps it from
//                              replacing every nginx variable in the file with an empty string
//   - deploy/nginx/silexgis.conf   the same rule for an operator who edits a file by hand
//   - client/vite.config.ts    the development server, so the browser suite drives the real policy
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

const packaged = read('client', 'nginx.conf');
const standalone = read('deploy', 'nginx', 'silexgis.conf');
const image = read('client', 'Dockerfile');
const devServer = read('client', 'vite.config.ts');
const compose = read('deploy', 'docker-compose.yml');
const envExample = read('deploy', '.env.example');

/**
 * A configuration with its commentary taken out.
 *
 * Both of these files explain themselves at length, and one of the explanations is a worked example
 * of the very directive being counted below. A check that read the comments would be a check that
 * a file can pass by talking about the right thing.
 */
const directives = (config) =>
  config
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('#'))
    .join('\n');

/**
 * One `location` block, from its opening brace to the brace that closes it.
 *
 * Counted by depth rather than found by the next `}`, because the one directive that matters here
 * contains a `${...}` of its own — and a slice that stopped at the first closing brace would cut
 * the allow-list in half and then fail to find what it had just removed.
 */
const blockOf = (config, opening, why) => {
  const text = directives(config);
  const start = text.indexOf(opening);
  assert.notEqual(start, -1, why);
  let depth = 0;
  for (let at = start; at < text.length; at++) {
    if (text[at] === '{') depth++;
    if (text[at] === '}') {
      depth--;
      if (depth === 0) return text.slice(start, at + 1);
    }
  }
  throw new assert.AssertionError({ message: `${opening} is never closed` });
};

/** The published-trip block, which is the one exception to denying framing. */
const embedBlock = (config) =>
  blockOf(
    config,
    'location ~ ^/shared/trips/ {',
    'a configuration must name the addresses a published trip is framed at',
  );

/** The single-page fallback, which is what serves every other address in the application. */
const shellBlock = (config) =>
  blockOf(config, 'location / {', 'a configuration must serve the application shell');

describe('framing is denied by default', () => {
  it('says so at the server level of both configurations', () => {
    for (const config of [packaged, standalone]) {
      const text = directives(config);
      assert.match(text, /add_header Content-Security-Policy "frame-ancestors 'none'" always;/);
      assert.match(text, /add_header X-Frame-Options "DENY" always;/);
    }
  });

  it('says so again on the addresses that actually serve documents', () => {
    // `add_header` does not inherit into a location that has any `add_header` of its own, and the
    // shell's location sets Cache-Control — so without repeating them there, the whole application
    // would be served with no framing header at all while the server block looked correct.
    for (const config of [packaged, standalone]) {
      const shell = shellBlock(config);
      assert.match(shell, /frame-ancestors 'none'/);
      assert.match(shell, /X-Frame-Options "DENY"/);
    }
  });

  it('covers the sign-in flow, which is served through the proxy rather than from disk', () => {
    for (const config of [packaged, standalone]) {
      const block = blockOf(
        config,
        'location ~ ^/(api|connect|health|openapi)(/|$) {',
        'a configuration must proxy the API and the protocol endpoints',
      );
      assert.match(block, /frame-ancestors 'none'/);
      assert.match(block, /X-Frame-Options "DENY"/);
    }
  });
});

describe('a published trip is the one exception', () => {
  it('is the only address either configuration lets anybody frame', () => {
    for (const config of [packaged, standalone]) {
      const allowing = [...directives(config).matchAll(/frame-ancestors 'self'/g)];
      assert.equal(
        allowing.length,
        1,
        'exactly one location may allow framing, and it is the published trip',
      );
      assert.match(embedBlock(config), /frame-ancestors 'self'/);
    }
  });

  it('sends no X-Frame-Options alongside the allow-list', () => {
    // That header can say DENY or SAMEORIGIN and nothing else — `ALLOW-FROM` was removed from every
    // browser — so sending it here is one header refusing what the other allows, and the embed
    // fails with nothing to see but a blank frame.
    for (const config of [packaged, standalone]) {
      assert.ok(
        !embedBlock(config).includes('X-Frame-Options'),
        'the framable location must not also send X-Frame-Options',
      );
    }
  });

  it('serves the shell from within that location rather than redirecting into the denying one', () => {
    // `try_files` ending in a URI is an internal redirect, and an internal redirect re-runs location
    // matching: the page would be served from `location /` and would carry its deny headers. The
    // rule would be undone by a line that produces exactly the same page.
    for (const config of [packaged, standalone]) {
      const block = embedBlock(config);
      assert.match(block, /try_files \/index\.html =404;/);
      assert.ok(
        !/try_files[^\n]*\/index\.html;/.test(block),
        'the framable location must not end try_files with a URI',
      );
    }
  });

  it('takes the allowed origins from a setting in the packaged image, and by hand otherwise', () => {
    assert.match(embedBlock(packaged), /frame-ancestors 'self' \$\{SILEXGIS__Web__FrameAncestors\}/);
    // Nothing expands an environment variable in a file an operator edits by hand, so that one
    // documents where to type the origins instead of pretending to read them from somewhere.
    assert.ok(
      !directives(standalone).includes('${SILEXGIS__Web__FrameAncestors}'),
      'the hand-edited configuration must not pretend to read an environment variable',
    );
    assert.match(standalone, /EDIT THIS LINE/);
  });
});

describe('the one setting reaches the packaged web server', () => {
  it('is installed as a template, because a finished file can expand nothing', () => {
    assert.match(image, /COPY nginx\.conf \/etc\/nginx\/templates\/default\.conf\.template/);
    assert.ok(
      !image.includes('/etc/nginx/conf.d/default.conf'),
      'the configuration must not also be copied straight in, which would win over the template',
    );
  });

  it('substitutes that variable and nothing else', () => {
    // Without a filter the expansion replaces EVERY variable in the file — `$uri`, `$scheme`,
    // `$http_host`, `$proxy_add_x_forwarded_for`, `$terrain_build_cache` — with the empty string.
    // What comes out is a configuration that is still valid, still starts, and serves everything
    // wrongly.
    assert.match(image, /ENV NGINX_ENVSUBST_FILTER="\^SILEXGIS__Web__"/);
  });

  it('declares the variable empty, so the header cannot go out quoting its own placeholder', () => {
    assert.match(image, /ENV SILEXGIS__Web__FrameAncestors=""/);
  });

  it('is passed through by the deployment and documented for the operator', () => {
    assert.match(compose, /SILEXGIS__Web__FrameAncestors: \$\{SILEXGIS_FRAME_ANCESTORS:-\}/);
    assert.match(envExample, /# SILEXGIS_FRAME_ANCESTORS=/);
  });
});

describe('the development server states the same policy', () => {
  it('denies framing everywhere but a published trip', () => {
    // Vite sets no headers of its own, so without this the browser suite would drive an application
    // that is framable by everybody, prove nothing about the policy, and stay green.
    assert.match(devServer, /frame-ancestors 'none'/);
    assert.match(devServer, /X-Frame-Options', 'DENY'/);
    assert.match(devServer, /frame-ancestors 'self'/);
  });

  it('recognises the same addresses the web servers do, and reads the same setting', () => {
    assert.match(devServer, /\/\^\\\/shared\\\/trips\\\//);
    assert.match(devServer, /process\.env\.SILEXGIS__Web__FrameAncestors/);
  });
});
