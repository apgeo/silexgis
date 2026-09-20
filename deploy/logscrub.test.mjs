// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Credentials must not reach the access log — a rule written in nginx configuration, which nothing
// that runs the application can check.
//
// The failure this exists to catch is silence, the same shape as the framing rule next door. An
// nginx server block with no `log_format` of its own does not fail, does not warn and does not look
// different from one that has thought about it: it quietly falls back to the built-in `combined`
// format, which writes `$request` — the method and the FULL request URI. On this application that
// means one live credential per line. A published trip is followed at `/shared/trips/<token>`, the
// page polls `/api/v1/public/trips/<token>` once a minute for as long as anyone is watching, and
// every file it draws carries a signed `token=` in the query. None of that is a bug anywhere until
// somebody reads the log, ships a support bundle, or pastes a few lines into an issue — at which
// point whoever holds that text can open every published page on the installation.
//
// So the rule is pinned in the two files that state it:
//   - client/nginx.conf            the packaged web server
//   - deploy/nginx/silexgis.conf   the same rule for an operator who edits a file by hand
//
// <b>This file runs the maps rather than reading them, and that is the point of it.</b> An earlier
// version asserted that each credential-bearing prefix appeared as a string somewhere inside the
// map block. Every one of those assertions passed against a configuration that wrote four of those
// credentials into the log in full: the entries were anchored to the end of the address with a
// closed list of permitted tails, so `/shared/trips/<token>/` — a follower who pasted the link with
// a trailing slash — matched no entry at all, fell through to `default $request_uri`, and was
// logged whole beside correctly-scrubbed lines. A check that a rule is *mentioned* cannot see what
// the rule *does*. The evaluator below implements nginx's own `map` semantics, and the table under
// it states, for each address, the exact line the log must carry.
//
// The evaluator's fidelity was checked against nginx itself: the maps from both files were loaded
// into `nginx:alpine`, every address in the table was requested against it, and the access log it
// wrote matched these expectations line for line — including the eight cases that fail against the
// anchored entries this replaced.
//
// The application's own request log is scrubbed by `CredentialUrlScrubber`, which has its own unit
// tests; this file is only about the proxy in front of it, which asks that code nothing.
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

const configs = {
  'client/nginx.conf': read('client', 'nginx.conf'),
  'deploy/nginx/silexgis.conf': read('deploy', 'nginx', 'silexgis.conf'),
};

/**
 * A configuration with its commentary taken out.
 *
 * Both files explain this rule at length and quote the very tokens and parameter names being
 * looked for below. A check that read the comments would be one a file could pass by talking
 * about the right thing.
 */
const directives = (config) =>
  config
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('#'))
    .join('\n');

// ---- nginx's `map`, as nginx applies it ---------------------------------------------------
//
// Three rules, and the second is what the leak this file now catches was made of:
//
//   1. A `~` key is a case-sensitive regular expression, and `$1`, `$2` … in the result are its
//      capture groups; a group that did not take part contributes nothing.
//   2. Entries are tried IN ORDER and the FIRST match answers. A map is a lookup table, not a
//      sequence of substitutions — which is why taking a path credential and a query credential
//      out of the same address needs two maps in series rather than two entries in one.
//   3. `default` answers when nothing matched, and its value may name another variable, which is
//      what lets one map read what another answered.

/** Every `map <source> <target> { … }` in a configuration, in file order. */
const mapsOf = (config) => {
  const text = directives(config);
  const maps = [];
  const opener = /map\s+\$(\S+)\s+\$(\S+)\s*\{/g;
  let found;
  while ((found = opener.exec(text)) !== null) {
    const [, source, target] = found;
    const close = text.indexOf('}', found.index);
    assert.notEqual(close, -1, `map $${source} $${target} is never closed`);
    const entries = [];
    for (const line of text.slice(found.index + found[0].length, close).split('\n')) {
      const statement = line.trim();
      if (statement === '') continue;
      // A key and a value, either of which may be quoted — `""` is a key in its own right.
      const parts = statement.replace(/;$/, '').match(/"(?:[^"\\]|\\.)*"|\S+/g) ?? [];
      assert.equal(parts.length, 2, `cannot read map entry: ${statement}`);
      const unquote = (token) =>
        token.startsWith('"') && token.endsWith('"') ? token.slice(1, -1) : token;
      entries.push({ key: unquote(parts[0]), value: unquote(parts[1]) });
    }
    maps.push({ source, target, entries });
  }
  return maps;
};

/** What a variable holds for one request, following the chain of maps that produce it. */
const valueOf = (maps, name, request, depth = 0) => {
  assert.ok(depth < 10, `the maps producing $${name} refer to each other in a circle`);
  if (name === 'request_uri') return request.uri;
  if (name === 'http_referer') return request.referer ?? '';

  const map = maps.find((m) => m.target === name);
  assert.ok(map, `nothing in this configuration produces $${name}`);
  const input = valueOf(maps, map.source, request, depth + 1);

  /** A result string with its capture references and variable references filled in. */
  const expand = (value, match) =>
    value.replace(/\$(\d|\w+)/g, (_, reference) =>
      /^\d$/.test(reference)
        ? (match?.[Number(reference)] ?? '')
        : valueOf(maps, reference, request, depth + 1),
    );

  for (const entry of map.entries) {
    if (entry.key === 'default') continue;
    if (entry.key.startsWith('~')) {
      const caseInsensitive = entry.key.startsWith('~*');
      const pattern = entry.key.slice(caseInsensitive ? 2 : 1).trim();
      const match = new RegExp(pattern, caseInsensitive ? 'i' : '').exec(input);
      if (match !== null) return expand(entry.value, match);
    } else if (entry.key === input) {
      return expand(entry.value, null);
    }
  }
  const fallback = map.entries.find((entry) => entry.key === 'default');
  return fallback ? expand(fallback.value, null) : '';
};

/** The two columns of the access line this application had to rewrite, for one request. */
const logged = (config, request) => {
  const maps = mapsOf(config);
  return {
    uri: valueOf(maps, 'silexgis_log_uri', request),
    referer: valueOf(maps, 'silexgis_log_referer', request),
  };
};

/**
 * Every address shape whose next path segment is a credential, and why each one is one.
 *
 * The list is the point. A rule that scrubbed the follow link and left the envelope behind it
 * would look like a considered decision and would leak the same token from the line below.
 */
const credentialPrefixes = [
  ['/shared/trips/', "the follower's own address, which is the whole of their claim"],
  ['/api/v1/public/trips/', 'the envelope that page and the framed viewer read'],
  ['/api/v1/shared/features/', 'a feature share link somebody chose to hand out'],
  ['/api/v1/shared/views/', 'a map view share link somebody chose to hand out'],
  ['/api/v1/public/albums/', 'a published album link somebody chose to hand out'],
];

// ---- what the log must say ----------------------------------------------------------------
//
// Every invented credential below is spelled `CREDENTIAL…`, so that "nothing of the credential
// survived" can be swept over the whole table rather than restated case by case. Nothing here is a
// real token: what each case is about is where the credential SITS, not what it is made of.
const cases = [
  {
    why: 'the address a follower is on',
    uri: '/shared/trips/CREDENTIAL1',
    uriLogged: '/shared/trips/[redacted]',
  },
  {
    why: 'the same trip framed in an article — the tail says which, so the tail is kept',
    uri: '/shared/trips/CREDENTIAL2/embed',
    uriLogged: '/shared/trips/[redacted]/embed',
  },
  {
    why: 'a follower who pasted the link with a trailing slash, which the application serves',
    uri: '/shared/trips/CREDENTIAL3/',
    uriLogged: '/shared/trips/[redacted]/',
  },
  {
    why: 'and the same with the embed tail, which an anchored entry did not foresee either',
    uri: '/shared/trips/CREDENTIAL4/embed/',
    uriLogged: '/shared/trips/[redacted]/embed/',
  },
  {
    why: 'a query carrying no credential is kept, because it is route shape',
    uri: '/shared/trips/CREDENTIAL5/embed?station=P12',
    uriLogged: '/shared/trips/[redacted]/embed?station=P12',
  },
  {
    why: 'the envelope the followed page reads once a minute',
    uri: '/api/v1/public/trips/CREDENTIAL6',
    uriLogged: '/api/v1/public/trips/[redacted]',
  },
  {
    why: 'the same, with a trailing slash',
    uri: '/api/v1/public/trips/CREDENTIAL7/',
    uriLogged: '/api/v1/public/trips/[redacted]/',
  },
  {
    why: 'a published album’s cover — a sub-path, which no closed list of tails contained',
    uri: '/api/v1/public/albums/CREDENTIAL8/cover',
    uriLogged: '/api/v1/public/albums/[redacted]/cover',
  },
  {
    why: 'a feature share, likewise under a sub-path',
    uri: '/api/v1/shared/features/CREDENTIAL9/geojson',
    uriLogged: '/api/v1/shared/features/[redacted]/geojson',
  },
  {
    why: 'a map view share',
    uri: '/api/v1/shared/views/CREDENTIAL10/',
    uriLogged: '/api/v1/shared/views/[redacted]/',
  },
  {
    why: 'BOTH credentials in one address, which one map cannot answer twice',
    uri: '/shared/trips/CREDENTIAL11?token=CREDENTIAL12',
    uriLogged: '/shared/trips/[redacted]?[redacted]',
  },
  {
    why: 'the same on the envelope, where the share token is the more valuable of the two',
    uri: '/api/v1/public/trips/CREDENTIAL13?token=CREDENTIAL14',
    uriLogged: '/api/v1/public/trips/[redacted]?[redacted]',
  },
  {
    why: 'a signed delivery URL — the survey drawing and every published photograph arrive so',
    uri: '/api/v1/files/f1e2/thumbnail?size=160&token=CREDENTIAL15',
    uriLogged: '/api/v1/files/f1e2/thumbnail?[redacted]',
  },
  {
    why: 'the parameter repeated, which a one-occurrence rule left half written out',
    uri: '/api/v1/files/f1e2/content?token=CREDENTIAL16&token=CREDENTIAL17',
    uriLogged: '/api/v1/files/f1e2/content?[redacted]',
  },
  {
    why: 'a password reset, which is worth an account rather than a page',
    uri: '/reset-password?email=someone@example.org&token=CREDENTIAL18',
    uriLogged: '/reset-password?[redacted]',
  },
  {
    why: 'a parameter that merely ends in the credential’s name is not one',
    uri: '/api/v1/files/f1e2/content?csrf_token=notacredential',
    uriLogged: '/api/v1/files/f1e2/content?csrf_token=notacredential',
  },
  {
    why: 'an ordinary signed-in address is written exactly as it was asked for',
    uri: '/api/v1/caves?page=2&sort=name',
    uriLogged: '/api/v1/caves?page=2&sort=name',
  },
  {
    why: 'the prefix with no token after it has no credential to take out',
    uri: '/shared/trips/',
    uriLogged: '/shared/trips/',
  },
  {
    why: 'the referer, which carries the follower’s whole page address on every poll',
    uri: '/api/v1/public/trips/CREDENTIAL19',
    referer: 'https://caves.example.org/shared/trips/CREDENTIAL20',
    uriLogged: '/api/v1/public/trips/[redacted]',
    refererLogged: 'https://caves.example.org/shared/trips/[redacted]',
  },
  {
    why: 'a referer carrying both shapes, from an embedded viewer',
    uri: '/api/v1/files/f1e2/content?token=CREDENTIAL21',
    referer: 'https://club.example.org/shared/trips/CREDENTIAL22/embed?token=CREDENTIAL23',
    uriLogged: '/api/v1/files/f1e2/content?[redacted]',
    refererLogged: 'https://club.example.org/shared/trips/[redacted]/embed?[redacted]',
  },
  {
    why: 'a referer with a trailing slash',
    uri: '/',
    referer: 'https://club.example.org/shared/trips/CREDENTIAL24/',
    uriLogged: '/',
    refererLogged: 'https://club.example.org/shared/trips/[redacted]/',
  },
  {
    why: 'an ordinary referer is kept whole — where a reader came from is what the column is for',
    uri: '/',
    referer: 'https://club.example.org/articles/spring-trip',
    uriLogged: '/',
    refererLogged: 'https://club.example.org/articles/spring-trip',
  },
  {
    why: '`combined` writes a dash when there was no referer, and so must this',
    uri: '/',
    referer: '',
    uriLogged: '/',
    refererLogged: '-',
  },
];

describe('the access log is written in a format that scrubs credentials', () => {
  it('both configurations define the format and use it', () => {
    for (const [name, config] of Object.entries(configs)) {
      const text = directives(config);
      assert.match(
        text,
        /log_format silexgis_scrubbed/,
        `${name} must define its own log format; the built-in combined format writes the full URI`,
      );
      assert.match(
        text,
        /access_log \/var\/log\/nginx\/access\.log silexgis_scrubbed;/,
        `${name} defines a scrubbing format but never tells the server block to use it`,
      );
    }
  });

  it('the format writes the scrubbed address and the scrubbed referer, never the raw ones', () => {
    for (const [name, config] of Object.entries(configs)) {
      const format = directives(config).split('log_format silexgis_scrubbed')[1].split(';')[0];

      assert.match(format, /\$silexgis_log_uri/, `${name} must log the scrubbed request URI`);
      // The one that is easy to forget. A follower's page is same-origin with the API it calls, so
      // the browser's default referrer policy sends the whole page URL — token and all — on every
      // poll. Scrubbing the request and not the referer moves the leak one column to the right.
      assert.match(format, /\$silexgis_log_referer/, `${name} must log the scrubbed referer`);

      assert.doesNotMatch(
        format,
        /\$request\b(?!_method)/,
        `${name} logs $request, which is the method and the FULL request URI`,
      );
      assert.doesNotMatch(
        format,
        /\$request_uri/,
        `${name} logs the raw request URI beside the scrubbed one`,
      );
      assert.doesNotMatch(
        format,
        /\$http_referer/,
        `${name} logs the raw referer, which carries the address the follower's page is at`,
      );
    }
  });

  for (const scenario of cases) {
    it(`writes the right line for ${scenario.why}`, () => {
      for (const [name, config] of Object.entries(configs)) {
        const line = logged(config, { uri: scenario.uri, referer: scenario.referer ?? '' });
        assert.equal(line.uri, scenario.uriLogged, `${name}: the request column is wrong`);
        if (scenario.refererLogged !== undefined) {
          assert.equal(line.referer, scenario.refererLogged, `${name}: the referer column is wrong`);
        }
      }
    });
  }

  it('no credential from any of those addresses survives into either column', () => {
    // The sweep the per-case expectations cannot state: whatever a later entry does to an address,
    // what it must never do is leave the credential in it. Asserted as a substring, so a partly
    // scrubbed value — which is the shape the anchored entries produced — fails here too.
    for (const [name, config] of Object.entries(configs)) {
      for (const scenario of cases) {
        const line = logged(config, { uri: scenario.uri, referer: scenario.referer ?? '' });
        for (const column of [line.uri, line.referer]) {
          assert.doesNotMatch(
            column,
            /CREDENTIAL/,
            `${name}: "${scenario.uri}" (referer "${scenario.referer ?? ''}") logged "${column}"`,
          );
        }
      }
    }
  });

  it('the two configurations scrub identically, so an installation cannot be safe by accident', () => {
    // Stated as an equality of ANSWERS rather than of text: these files are edited separately, by
    // different people, months apart, and a rule added to one of them is a rule the other
    // installation silently does not have. Comparing what they do also survives the two stating
    // the same rule in different words, which the path entries legitimately do — a referer is an
    // absolute URL and a request URI is not.
    const packaged = configs['client/nginx.conf'];
    const standalone = configs['deploy/nginx/silexgis.conf'];
    for (const scenario of cases) {
      const request = { uri: scenario.uri, referer: scenario.referer ?? '' };
      assert.deepEqual(
        logged(packaged, request),
        logged(standalone, request),
        `the packaged and hand-edited configurations disagree about "${scenario.uri}"`,
      );
    }
  });

  it('every credential-bearing address shape is named by both configurations', () => {
    // The table above can only ask about shapes somebody thought of. This is the cheap check
    // beside it: every surface that mints a URL-borne capability is named in each file, so adding
    // a sixth to one of them and not to the other is caught before a case exists for it.
    for (const [name, config] of Object.entries(configs)) {
      const keys = mapsOf(config).flatMap((map) =>
        map.target.startsWith('silexgis_') ? map.entries.map((entry) => entry.key) : [],
      );
      for (const [prefix, why] of credentialPrefixes) {
        assert.ok(
          keys.some((key) => key.includes(prefix)),
          `${name}: no map entry scrubs ${prefix} — ${why}`,
        );
      }
      assert.ok(
        keys.some((key) => key.includes('token=')),
        `${name}: no map entry scrubs the signed delivery token in the query`,
      );
    }
  });

  it('the installation guide tells an operator to scrub the same addresses', () => {
    // The third place this rule is written, and the only one nobody here can run. An operator who
    // puts their own proxy in front of the web service gets no map from this repository — they get
    // these instructions, and whatever they build from them is what their log carries.
    //
    // What a check like this can see is the list going out of step: a sixth share surface added to
    // the two configurations and not to the guide leaves every hand-rolled proxy writing that one
    // in full. What it cannot see is whether the recipe beside the list actually works — the defect
    // that made this section worth revisiting was a Caddy example that filtered the query and left
    // the path, which named every right thing while doing half of it. That half is checked by
    // running the recipe, which is how the one below was arrived at.
    const guide = read('docs', 'INSTALL.md');
    const section = (guide.split('And turn off — or scrub — its access log')[1] ?? '').split(
      '\n## ',
    )[0];
    assert.notEqual(
      section,
      '',
      'the installation guide no longer tells an operator to scrub the access log',
    );
    for (const [prefix, why] of credentialPrefixes) {
      assert.ok(
        section.includes(prefix),
        `docs/INSTALL.md does not tell an operator to scrub ${prefix} — ${why}`,
      );
    }
    assert.match(
      section,
      /token=/,
      'docs/INSTALL.md does not tell an operator to scrub the signed delivery token',
    );
  });
});

describe('the rule is worth having', () => {
  // The positive twin of everything above: prove that the format being replaced really does write
  // the credential, so the assertions are about a leak that exists rather than about a string.
  it('the built-in combined format, which is the fallback, writes the whole request URI', () => {
    // nginx's own definition, quoted from its documentation. `$request` is the request line:
    // method, full URI, protocol. This is what every server block gets when it names no format.
    const combined =
      '$remote_addr - $remote_user [$time_local] "$request" ' +
      '$status $body_bytes_sent "$http_referer" "$http_user_agent"';

    assert.match(combined, /\$request"/);
    assert.match(combined, /\$http_referer/);

    // And neither config may fall back to it, which is what the `access_log` assertion above
    // guarantees — restated here so the two halves sit next to each other.
    for (const [name, config] of Object.entries(configs)) {
      assert.doesNotMatch(
        directives(config),
        /access_log\s+\S+\s+combined;/,
        `${name} explicitly selects the combined format`,
      );
    }
  });

  it('the addresses in the table are addresses that carry credentials', () => {
    // The other positive twin, and the one that keeps the sweep honest: a case whose address held
    // nothing to take out would pass "no credential survived" while proving nothing at all.
    const carrying = cases.filter(
      (scenario) => /CREDENTIAL/.test(scenario.uri) || /CREDENTIAL/.test(scenario.referer ?? ''),
    );
    assert.ok(
      carrying.length >= 15,
      'the table has stopped covering the addresses that have credentials in them',
    );
    // And the ones that do not are there on purpose: an address with no credential must come back
    // unchanged, which is what keeps the rule from being "redact everything".
    const untouched = cases.filter(
      (scenario) => !/CREDENTIAL/.test(`${scenario.uri} ${scenario.referer ?? ''}`),
    );
    assert.ok(
      untouched.length >= 4,
      'nothing in the table proves an ordinary address is left alone',
    );
    for (const scenario of untouched) {
      assert.equal(
        scenario.uriLogged,
        scenario.uri,
        `${scenario.uri} carries no credential and must be logged as it was asked for`,
      );
    }
  });
});
