// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests for the outside-in check of one published-trip link.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
//
// Everything here is driven against a small server written out below, on this machine's loopback
// address, which answers the way the web front and the API do for the handful of addresses the
// check reads. No installation is ever asked: the token and the signed address in these tests
// are invented, and the point of most of them is that neither appears in a single printed line.

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join } from 'node:path';
import { after, before, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  TOKEN_VARIABLE,
  frameAncestorsOf,
  handleOf,
  parseArguments,
  readSecret,
  scrubber,
  verify,
} from './verify-published-trip.mjs';

const deployDir = dirname(fileURLToPath(import.meta.url));
const scriptPath = join(deployDir, 'verify-published-trip.mjs');
const script = readFileSync(scriptPath, 'utf8');

/** An invented follow token, shaped like a real one, and the signed half of an invented file address. */
const TOKEN = 'Zm9sbG93LW1lLWludG8tdGhlLWNhdmU_x-7Q';
const SIGNED = 'c2lnbmVkLWRlbGl2ZXJ5LXRva2Vu.9f3a';
const HANDLE = createHash('sha256').update(TOKEN, 'utf8').digest('base64url').slice(0, 8);

const CLUB = 'https://club.example.org';

/**
 * What the stand-in server answers, changed by each test.
 *
 * `link` is what the server makes of the token: 'followed', 'closed', 'archive' (its own trip is
 * history), 'unknown' (every published-trip address answers the one 404), or 'archiveOff'.
 */
const DEFAULTS = {
  link: 'followed',
  limited: false,
  retryAfter: '37',
  robots: 'noindex, nofollow',
  policy: `frame-ancestors 'self' ${CLUB}`,
  apiStatus: null,
  model: true,
  fileStatus: 206,
  redirectPage: false,
};
let answers = { ...DEFAULTS };
let requests = [];

let server;
let address;

function json(response, status, body, headers = {}) {
  response.writeHead(status, { 'content-type': status === 200 ? 'application/json' : 'application/problem+json', ...headers });
  response.end(JSON.stringify(body));
}

const refusal = (response) =>
  json(response, 404, { status: 404, code: 'tracking.share_not_found', title: 'Not Found' });

before(async () => {
  server = createServer((request, response) => {
    const url = new URL(request.url, 'http://stub');
    requests.push({ method: request.method, path: url.pathname, range: request.headers.range ?? null });

    if (answers.limited) {
      const headers = answers.retryAfter === null ? {} : { 'retry-after': answers.retryAfter };
      return json(response, 429, { status: 429, title: 'Too Many Requests' }, headers);
    }

    if (url.pathname === `/shared/trips/${TOKEN}`) {
      if (answers.redirectPage) {
        response.writeHead(301, { location: `https://elsewhere.example.org/shared/trips/${TOKEN}` });
        return response.end();
      }
      const headers = { 'content-type': 'text/html' };
      if (answers.robots !== null) headers['x-robots-tag'] = answers.robots;
      if (answers.policy !== null) headers['content-security-policy'] = answers.policy;
      response.writeHead(200, headers);
      return response.end('<!doctype html><title>SilexGIS</title>');
    }

    if (url.pathname === '/api/v1/files/7d0c1a52-0000-4000-8000-000000000001/content') {
      response.writeHead(url.searchParams.get('token') === SIGNED ? answers.fileStatus : 401);
      return response.end('x');
    }

    const api = `/api/v1/public/trips/${TOKEN}`;
    if (!url.pathname.startsWith(api)) return refusal(response);
    if (answers.apiStatus !== null) return json(response, answers.apiStatus, { status: answers.apiStatus });

    const route = url.pathname.slice(api.length);
    const { link } = answers;
    if (link === 'unknown') return refusal(response);

    if (route === '') {
      if (link === 'archive' || link === 'liveLapsed') return refusal(response);
      return json(response, 200, {
        tripLogId: '7d0c1a52-0000-4000-8000-0000000000aa',
        title: 'An invented trip',
        state: link === 'closed' ? 'closed' : 'armed',
        model: answers.model
          ? { modelUrl: `/api/v1/files/7d0c1a52-0000-4000-8000-000000000001/content?token=${SIGNED}` }
          : null,
        participants: [],
      });
    }
    if (route === '/live') {
      if (link === 'liveLapsed') return refusal(response);
      return json(response, 200, { trips: link === 'archive' ? [] : [{}], more: false });
    }
    if (route === '/past') {
      return link === 'archiveOff' ? refusal(response) : json(response, 200, { trips: [{}, {}], more: false });
    }
    return refusal(response);
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  address = `http://127.0.0.1:${server.address().port}`;
});

after(() => server.close());

beforeEach(() => {
  answers = { ...DEFAULTS };
  requests = [];
});

/**
 * Runs the check against the stand-in, answering as the defaults say except where `overrides`
 * says otherwise, and returns what it printed and whether it failed.
 */
async function run(overrides = {}, input = {}) {
  answers = { ...DEFAULTS, ...overrides };
  const lines = [];
  const failed = await verify({ address, token: TOKEN, ...input }, (line) => lines.push(line));
  return { lines, failed, text: lines.join('\n') };
}

const verdicts = (lines) => lines.map((line) => line.split(/\s+/)[0]);

describe('the link is never an argument', () => {
  it('takes an installation address and nothing that could be a link', () => {
    assert.deepEqual(parseArguments(['https://gis.example.org']),
      { address: 'https://gis.example.org', origins: [], allowHttp: false });
    assert.deepEqual(parseArguments([]), { address: null, origins: [], allowHttp: false });
  });

  it('refuses a follow link, and a bare token, without repeating either', () => {
    for (const arg of [`https://gis.example.org/shared/trips/${TOKEN}`, TOKEN, `https://gis.example.org/?t=${TOKEN}`]) {
      const { problem, address: parsed } = parseArguments([arg]);
      assert.equal(parsed, undefined);
      assert.match(problem, /never an argument/);
      assert.ok(!problem.includes(TOKEN), 'the refusal repeats what it refused');
    }
  });

  it('reads no option that could carry the token', () => {
    assert.match(parseArguments(['--token', TOKEN]).problem, /Unknown option --token/);
    // The source text itself: the token reaches `verify` from the environment or standard input
    // and from nowhere else, so nothing in `main` may take it out of the argument list.
    assert.doesNotMatch(script, /token:\s*(asked|process\.argv)/);
    assert.match(script, /process\.env\[TOKEN_VARIABLE\]/);
    assert.equal(TOKEN_VARIABLE, 'SILEXGIS_VERIFY_TOKEN');
  });

  it('takes sites that frame the page as origins only', () => {
    assert.deepEqual(parseArguments(['--origin', CLUB, '--origin', 'https://WWW.club.example.org:443']).origins,
      [CLUB, 'https://www.club.example.org']);
    assert.match(parseArguments(['--origin']).problem, /--origin takes/);
    assert.match(parseArguments(['--origin', `${CLUB}/articles/1`]).problem, /--origin takes/);
  });

  it('takes the token from a pasted link, with the installation it names', () => {
    assert.deepEqual(readSecret(`  https://gis.example.org/shared/trips/${TOKEN}?past=1#x\n`),
      { token: TOKEN, address: 'https://gis.example.org' });
    assert.deepEqual(readSecret(`${TOKEN}\r\n`), { token: TOKEN, address: null });
    assert.match(readSecret('\n\n').problem, /No follow link/);
    assert.match(readSecret('https://gis.example.org/shared/trips/').problem, /no token/);
    assert.match(readSecret('https://gis.example.org/trips/12').problem, /neither a follow link/);
  });
});

describe('the handle', () => {
  it('is the first eight characters of the base64url SHA-256 of the token', () => {
    assert.equal(handleOf(TOKEN), HANDLE);
    assert.equal(handleOf(TOKEN).length, 8);
    assert.notEqual(handleOf(`${TOKEN}x`), HANDLE);
  });

  it('replaces the token however an address spells it, and any signed file address', () => {
    const awkward = 'a b/c+d';
    const scrub = scrubber(awkward);
    const handle = `[token:${handleOf(awkward)}]`;
    assert.equal(scrub(`GET /shared/trips/${encodeURIComponent(awkward)} and ${awkward}`),
      `GET /shared/trips/${handle} and ${handle}`);
    assert.equal(scrubber(TOKEN)(`/api/v1/files/1/content?size=2&token=${SIGNED}&x=1`),
      '/api/v1/files/1/content?size=2&token=[redacted]&x=1');
  });
});

describe('what a working link prints', () => {
  it('passes every check, reading each address once', async () => {
    const { lines, failed } = await run({}, { origins: [CLUB] });

    assert.equal(failed, false);
    assert.deepEqual(verdicts(lines), ['note', 'ok', 'ok', 'ok', 'ok', 'ok', 'ok', 'ok']);
    assert.match(lines[0], new RegExp(`link handle ${HANDLE} `));
    assert.match(lines.join('\n'), /party: the link follows its trip \(tracking running\)/);
    assert.match(lines.join('\n'), /followed now: answered, trips listed: 1/);
    assert.match(lines.join('\n'), /past trips: answered, trips listed: 2/);
    assert.match(lines.join('\n'), /survey: the drawing is served/);

    assert.deepEqual(requests.map((each) => each.path), [
      `/shared/trips/${TOKEN}`,
      `/api/v1/public/trips/${TOKEN}`,
      `/api/v1/public/trips/${TOKEN}/live`,
      `/api/v1/public/trips/${TOKEN}/past`,
      '/api/v1/files/7d0c1a52-0000-4000-8000-000000000001/content',
    ]);
    assert.ok(requests.every((each) => each.method === 'GET'));
    assert.equal(requests.at(-1).range, 'bytes=0-0', 'the survey is asked for by its first byte, not downloaded');
  });

  it('says a closed trip is over, and a trip with no survey has no drawing', async () => {
    const { text, failed } = await run({ link: 'closed', model: false });
    assert.equal(failed, false);
    assert.match(text, /party: the link opens its trip, which is over/);
    assert.match(text, /note\s+survey: the trip publishes none/);
  });

  it('lists who may frame the page when no site was named', async () => {
    const { text } = await run();
    assert.match(text, /note\s+page: may be framed by 'self' https:\/\/club\.example\.org/);
  });
});

describe('a 429 is never a pass', () => {
  it('prints every limited read as rate-limited from this address, with the wait', async () => {
    const { lines, failed, text } = await run({ limited: true }, { origins: [CLUB] });

    assert.equal(failed, true);
    assert.deepEqual(verdicts(lines), ['note', 'LIMITED', 'LIMITED', 'LIMITED', 'LIMITED']);
    for (const line of lines.slice(1)) {
      assert.match(line, /rate-limited from this address; the server asks for 37 s/);
    }
    assert.doesNotMatch(text, /^ok/m);
    assert.doesNotMatch(text, /Published trip read refused/, 'a limited read is not evidence about the link');
  });

  it('says so without a wait when the answer carries none', async () => {
    const { lines } = await run({ limited: true, retryAfter: null });
    assert.match(lines[1], /^LIMITED\s+page: rate-limited from this address — /);
  });
});

describe('what a reader who sees nothing is seeing', () => {
  it('a link the server does not know: the handle, and where the reason is', async () => {
    const { lines, failed, text } = await run({ link: 'unknown' });
    assert.equal(failed, true);
    assert.equal(lines.at(-1).startsWith('FAIL'), true);
    assert.match(text, new RegExp(`search it for "link ${HANDLE}"`));
    assert.match(text, /Published trip read refused/);
    assert.doesNotMatch(text, /survey:/, 'nothing was handed over to ask for');
  });

  it('a link whose own trip is history: a note, not a failure', async () => {
    const { failed, text } = await run({ link: 'archive' });
    assert.equal(failed, false);
    assert.match(text, /note\s+party: the link no longer follows its own trip/);
    assert.match(text, /ok\s+past trips: answered, trips listed: 2/);
    assert.match(text, /it opens its cave's lists, above/);
    assert.doesNotMatch(text, /followed now: not offered/, 'both lists answered');
  });

  it('an old link past the period for listing current parties: past trips only, and why', async () => {
    const { failed, text } = await run({ link: 'liveLapsed' });
    assert.equal(failed, false);
    assert.match(text, /ok\s+past trips: answered, trips listed: 2/);
    assert.match(text, /note\s+followed now: not offered through this link/);
    assert.match(text, /SILEXGIS__TripTracking__SiblingWindowAfterLapse/);
    assert.match(text, /past_sibling_window/);
    assert.match(text, /it opens its cave's past trips, above, and nothing else/);
    assert.doesNotMatch(text, /its cave's lists/, 'one of the two lists was refused');
    assert.doesNotMatch(text, /ok\s+followed now/);
  });

  it('past trips switched off: a note beside a followed party', async () => {
    const { failed, text } = await run({ link: 'archiveOff' });
    assert.equal(failed, false);
    assert.match(text, /note\s+past trips: not offered through this link/);
  });

  it('a proxy that drops the two headers', async () => {
    const { failed, text } = await run({ robots: null, policy: null });
    assert.equal(failed, true);
    assert.match(text, /FAIL\s+page: no "X-Robots-Tag: noindex"/);
    assert.match(text, /FAIL\s+page: no "frame-ancestors"/);
  });

  it('a site that is not allowed to frame the page, beside one that is', async () => {
    const { failed, text } = await run({}, { origins: [CLUB, 'https://www.club.example.org'] });
    assert.equal(failed, true);
    assert.match(text, /ok\s+page: https:\/\/club\.example\.org may frame it/);
    assert.match(text, /FAIL\s+page: https:\/\/www\.club\.example\.org may not frame it/);
  });

  it('a server error, a redirect and a survey that is not served are each named', async () => {
    assert.match((await run({ apiStatus: 502 })).text, /FAIL\s+party: the server answered 502/);

    const redirected = await run({ redirectPage: true });
    assert.equal(redirected.failed, true);
    assert.match(redirected.text, /FAIL\s+page: answered a redirect \(301\)/);

    const noFile = await run({ fileStatus: 404 });
    assert.equal(noFile.failed, true);
    assert.match(noFile.text, /FAIL\s+survey: the drawing's address answered 404/);
  });

  it('an address nothing answers at', async () => {
    const lines = [];
    const failed = await verify(
      { address: 'http://127.0.0.1:9', token: TOKEN },
      (line) => lines.push(line),
      async (url) => { throw new Error(`connect refused for ${url}`); });
    assert.equal(failed, true);
    assert.equal(lines.filter((line) => line.startsWith('FAIL')).length, 4);
    assert.ok(!lines.join('\n').includes(TOKEN), 'an error that quotes the address quotes the token');
  });

  it('reads frame-ancestors out of a longer policy', () => {
    assert.deepEqual(frameAncestorsOf(`default-src 'self'; Frame-Ancestors 'self' ${CLUB}; img-src *`), ["'self'", CLUB]);
    assert.equal(frameAncestorsOf("default-src 'self'"), null);
    assert.equal(frameAncestorsOf(null), null);
  });
});

describe('nothing printed carries the credential', () => {
  it('in any outcome the stand-in can produce', async () => {
    const outcomes = [
      {}, { link: 'closed' }, { link: 'archive' }, { link: 'unknown' }, { link: 'archiveOff' },
      { limited: true }, { robots: null, policy: null }, { apiStatus: 500 }, { redirectPage: true },
      { fileStatus: 403 }, { model: false },
    ];
    let printed = 0;
    for (const outcome of outcomes) {
      const { lines } = await run(outcome, { origins: [CLUB, 'https://other.example.org'], linkAddress: 'https://gis.example.org' });
      for (const line of lines) {
        printed += 1;
        assert.ok(!line.includes(TOKEN), `the token is printed: ${outcome.link ?? JSON.stringify(outcome)}`);
        assert.ok(!line.includes(SIGNED), 'the signed address of the survey is printed');
        assert.ok(!line.includes('\n'), 'one verdict per line');
        assert.match(line, /^(ok|note|FAIL|LIMITED)\s/);
      }
    }
    // The redirect's Location carried the token: prove the scrubbing was exercised, not idle.
    assert.ok(printed > 50);
    assert.match((await run({ redirectPage: true })).text, new RegExp(`/shared/trips/\\[token:${HANDLE}\\]`));
  });
});

describe('run as a person runs it', () => {
  /** Runs the script itself, with the link on standard input or in the environment. */
  async function launch(args, { stdin = null, env = {} } = {}) {
    const child = spawn(process.execPath, [scriptPath, ...args], {
      env: { ...process.env, [TOKEN_VARIABLE]: '', ...env },
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.stdin.end(stdin ?? '');
    const [code] = await once(child, 'close');
    return { code, stdout, stderr };
  }

  it('takes the whole link on standard input and exits 0 on a working link', async () => {
    const { code, stdout, stderr } = await launch([], { stdin: `${address}/shared/trips/${TOKEN}\n` });
    assert.equal(code, 0, stderr);
    assert.match(stdout, /ok\s+survey: the drawing is served/);
    assert.ok(!stdout.includes(TOKEN) && !stderr.includes(TOKEN));
  });

  it('takes the token from the environment and exits 1 when limited', async () => {
    answers.limited = true;
    const { code, stdout } = await launch([address], { env: { [TOKEN_VARIABLE]: TOKEN } });
    assert.equal(code, 1);
    assert.match(stdout, /rate-limited from this address/);
    assert.ok(!stdout.includes(TOKEN));
  });

  it('exits 2, asking nothing, when the link is an argument or no installation is named', async () => {
    const asArgument = await launch([`${address}/shared/trips/${TOKEN}`]);
    assert.equal(asArgument.code, 2);
    assert.match(asArgument.stderr, /never an argument/);
    assert.ok(!asArgument.stderr.includes(TOKEN) && !asArgument.stdout.includes(TOKEN));

    const nowhere = await launch([], { stdin: `${TOKEN}\n` });
    assert.equal(nowhere.code, 2);
    assert.match(nowhere.stderr, /Which installation\?/);

    const plain = await launch(['http://gis.example.org'], { stdin: `${TOKEN}\n` });
    assert.equal(plain.code, 2);
    assert.match(plain.stderr, /plain HTTP/);

    assert.equal(requests.length, 0, 'a refused run reached the server');
  });
});
