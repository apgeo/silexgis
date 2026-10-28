// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests for the throwaway SpeleoLoc development server.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
//
// Importing the script does not run it: it only acts when it is the entry point, so nothing
// here starts a container or a server. What these tests guard is the half that is silently
// wrong rather than loudly broken — the ports and the connection string. A script that seeded
// the shared development database instead of its own would print exactly the same success
// message, so the pins below are on the values, not on the behaviour they produce.
//
// The tests at the foot of this file stand the stack up for real — they run `up`, wait for the
// API and sign in — and are opt-in, because the continuous-integration runner that executes this
// file has neither the .NET SDK nor a database image. Run them with
// SILEXGIS_SPELEOLOC_DEV_LIVE=1, which is what a developer does before trusting a change here.

import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  ADMIN_EMAIL,
  ADMIN_PASSWORD,
  CLIENT_ID,
  COMPOSE_PROJECT,
  DEFAULT_API_PORT,
  DEFAULT_DB_PORT,
  MEMBER_EMAIL,
  MEMBER_PASSWORD,
  OPEN_PHOTO_TITLE,
  RASTER_TITLE,
  REDIRECT_URI,
  SHARED_PHOTO_TITLE,
  apiExitProblem,
  authorizeUrl,
  baseUrlFor,
  capabilitiesProblem,
  composeEnvironment,
  connectionStringFor,
  detachApi,
  dotnetRun,
  foreignContainerProblem,
  parseArgs,
  photoReachProblem,
  pkcePair,
  proveSyncContent,
  readCapabilities,
  signIn,
  signInAndReadCapabilities,
  stopApiTree,
  tokenDanceCurl,
  withheldPinProblem,
  openPlaceIdIn,
  OPEN_PLACE_NAME,
  REVIEW_EMAIL,
  ReviewRefusal,
  decidedLine,
  positionals,
  reviewDecide,
  reviewWaiting,
  waitingLine,
} from './speleoloc-dev.mjs';

const deployDir = dirname(fileURLToPath(import.meta.url));
const read = (...parts) => readFileSync(join(deployDir, ...parts), 'utf8');

const compose = read('docker-compose.speleoloc-dev.yml');
const script = read('speleoloc-dev.mjs');

/**
 * Every entry under a `ports:` key, whatever form compose allows it to be written in: quoted or
 * bare, `host:container`, `addr:host:container`, or a lone container port.
 */
function publishedPorts(text) {
  const entries = [];
  let indent = null;
  for (const line of text.split('\n')) {
    if (line.trim() === '' || /^\s*#/.test(line)) {
      continue;
    }

    const item = /^(\s*)-\s*(.+?)\s*$/.exec(line);
    if (indent !== null && item !== null && item[1].length > indent) {
      entries.push(item[2].replace(/^["']|["']$/g, ''));
      continue;
    }

    const key = /^(\s*)([\w.-]+):\s*$/.exec(line);
    indent = key !== null && key[2] === 'ports' ? key[1].length : null;
  }
  return entries;
}

/** The published ports that are not bound to the loopback address. */
const notLoopback = (text) =>
  publishedPorts(text).filter((entry) => !entry.startsWith('127.0.0.1:'));

describe('the stack is its own, not the shared development one', () => {
  it('runs under its own compose project name', () => {
    assert.match(compose, /^name: silexgis-speleoloc-dev$/m);
  });

  it('keeps its data on a named volume of its own', () => {
    assert.match(compose, /- silexgis-speleoloc-db:\/var\/lib\/postgresql\/data$/m);
    assert.match(compose, /^volumes:\n(?:.*\n)*?  silexgis-speleoloc-db:$/m);
  });

  it('hands docker compose one file, and it is this stack\'s own', () => {
    // The shared file is another session's database, and migrating it would rewrite their
    // data while looking exactly like a successful run of this one. The script names it in
    // prose, to say why; what matters is which file it actually passes to `docker compose`.
    const named = [...script.matchAll(/'-f',\s*([A-Za-z_]+|'[^']+')/g)].map((m) => m[1]);
    assert.deepEqual(named, ['COMPOSE_FILE']);
    assert.match(script, /^const COMPOSE_FILE = 'docker-compose\.speleoloc-dev\.yml';$/m);
  });

  it('never connects to the shared database port', () => {
    // 5432 is the shared development database; 5433 is where the production stack's own port
    // overlay publishes; 5080 is where `dotnet run` puts the ordinary development API.
    assert.doesNotMatch(connectionStringFor(DEFAULT_DB_PORT), /Port=543[23];/);
    assert.notEqual(DEFAULT_DB_PORT, 5432);
    assert.notEqual(DEFAULT_DB_PORT, 5433);
    assert.notEqual(DEFAULT_API_PORT, 5080);
  });
});

describe('both published ports are loopback only', () => {
  it('publishes the database on 127.0.0.1', () => {
    assert.match(compose, /- "127\.0\.0\.1:\$\{SILEXGIS_SPELEOLOC_DB_PORT:-5434\}:5432"$/m);
  });

  it('publishes the API on 127.0.0.1', () => {
    assert.match(compose, /- "127\.0\.0\.1:\$\{SILEXGIS_SPELEOLOC_API_PORT:-5205\}:8080"$/m);
  });

  it('publishes nothing on every interface', () => {
    // The host firewall drops non-tailnet traffic, but Docker's published ports bypass that
    // chain, so a binding without an address really is reachable from the network — and this
    // stack's database password is `silexgis`, printed in the handoff documentation.
    assert.deepEqual(notLoopback(compose), []);
    // Not vacuous: it did read the two mappings this file has.
    assert.equal(publishedPorts(compose).length, 2);
  });

  it('would catch the forms an edit is most likely to introduce', () => {
    // Every one of these publishes on 0.0.0.0. The short form carries no colon at all and the
    // unquoted form is not the shape the file uses today, so a guard written around today's
    // literals passes them all.
    assert.deepEqual(notLoopback('services:\n  db:\n    ports:\n      - "5434"\n'), ['5434']);
    assert.deepEqual(notLoopback('services:\n  db:\n    ports:\n      - 5434:5432\n'),
      ['5434:5432']);
    assert.deepEqual(notLoopback('services:\n  db:\n    ports:\n      - "0.0.0.0:5434:5432"\n'),
      ['0.0.0.0:5434:5432']);
    assert.deepEqual(notLoopback('services:\n  db:\n    ports:\n      - "127.0.0.1:5434:5432"\n'),
      []);
  });

  it('agrees with the ports the script defaults to', () => {
    assert.match(compose, new RegExp(`:-${DEFAULT_DB_PORT}\\}:5432`));
    assert.match(compose, new RegExp(`:-${DEFAULT_API_PORT}\\}:8080`));
  });
});

describe('the connection string', () => {
  it('names the port it was given, on loopback', () => {
    assert.equal(
      connectionStringFor(5434),
      'Host=127.0.0.1;Port=5434;Database=silexgis;Username=silexgis;Password=silexgis');
    assert.match(connectionStringFor(6001), /Port=6001;/);
  });

  it('is set explicitly, because the development settings file names another database', () => {
    assert.match(script, /SILEXGIS__Db__ConnectionString: connectionStringFor\(dbPort\)/);
  });
});

describe('the server is started on the port this script published', () => {
  it('refuses the project launch profile', () => {
    // A launch profile's applicationUrl beats ASPNETCORE_URLS from the environment, and the
    // one in this project names another port. Without this flag the server comes up healthy
    // on an address nothing here is watching, and the only symptom is a probe that never
    // succeeds against a server that is plainly running.
    assert.ok(dotnetRun('X').includes('--no-launch-profile'));
    assert.deepEqual(
      dotnetRun('X'),
      ['run', '--project', 'X', '--no-build', '--no-restore', '--no-launch-profile']);
  });

  it('uses those arguments for every invocation, seeding included', () => {
    assert.equal((script.match(/spawnSync\(|spawn\(/g) ?? []).length >= 2, true);
    assert.equal((script.match(/dotnetRun\(\)/g) ?? []).length, 3);
    assert.doesNotMatch(script, /'run', '--project', apiProject/);
  });
});

describe('stopping the server it started', () => {
  // The teardown is the half of this script that runs on somebody else's machine and is never
  // watched. On Windows the POSIX form is not merely ineffective — a negative pid is rejected —
  // and the throw is swallowed, so a Ctrl-C there leaves `dotnet run` holding the port while the
  // script prints nothing at all.
  const calls = () => {
    const seen = [];
    return {
      seen,
      signalGroup: (pid) => seen.push(['group', pid]),
      killTree: (pid) => seen.push(['tree', pid]),
    };
  };

  it('signals the whole process group on POSIX', () => {
    const spy = calls();
    stopApiTree({ pid: 4321 }, { platform: 'linux', ...spy });
    assert.deepEqual(spy.seen, [['group', 4321]]);
  });

  it('stops the process tree on Windows, where there is no group to signal', () => {
    const spy = calls();
    stopApiTree({ pid: 4321 }, { platform: 'win32', ...spy });
    assert.deepEqual(spy.seen, [['tree', 4321]]);
  });

  it('asks for a process group only where one exists', () => {
    assert.equal(detachApi('linux'), true);
    assert.equal(detachApi('darwin'), true);
    assert.equal(detachApi('win32'), false);
  });

  it('is untroubled by a child that has already gone', () => {
    stopApiTree({ pid: 4321 }, {
      platform: 'linux',
      signalGroup: () => { throw new Error('ESRCH'); },
    });
  });
});

describe('the wait for the API refuses a server this script did not start', () => {
  // The stale-server case: an API left over from an earlier run still holds the port, the one
  // just started dies on the bind, and the leftover answers the readiness probe with exactly the
  // 401 the probe is looking for. Everything downstream then passes against a server whose
  // database may since have been thrown away.
  it('says nothing while the child is alive', () => {
    assert.equal(apiExitProblem({ exitCode: null, signalCode: null }, baseUrlFor(5205)), null);
  });

  it('names the port and the likely cause when the child has exited', () => {
    const problem = apiExitProblem({ exitCode: 134, signalCode: null }, baseUrlFor(5205));
    assert.match(problem, /134/);
    assert.match(problem, /127\.0\.0\.1:5205/);
    assert.match(problem, /already listening/);
  });

  it('notices a child killed by a signal, which reports no exit code', () => {
    assert.match(apiExitProblem({ exitCode: null, signalCode: 'SIGKILL' }, baseUrlFor(5205)),
      /SIGKILL/);
  });

  it('is what the wait actually consults', () => {
    // Pinned on the call as well as on the helper: a guard the wait never asks would keep its
    // own test green while the stale server went on answering.
    assert.match(script, /apiExitProblem\(api, baseUrl\)/);
    assert.match(script, /await waitForApi\(baseUrl, \{ api \}\)/);
  });
});

describe('the sign-in the script performs', () => {
  it('mints a verifier whose challenge is its own SHA-256, base64url and unpadded', () => {
    const { verifier, challenge } = pkcePair();
    assert.ok(verifier.length >= 43 && verifier.length <= 128, `verifier length ${verifier.length}`);
    assert.doesNotMatch(verifier, /[^A-Za-z0-9\-_]/);
    assert.equal(challenge, createHash('sha256').update(verifier, 'ascii').digest('base64url'));
    assert.doesNotMatch(challenge, /=/);
  });

  it('mints a different verifier every time', () => {
    assert.notEqual(pkcePair().verifier, pkcePair().verifier);
  });

  it('asks for a refresh token and proves possession of the verifier', () => {
    const url = new URL(authorizeUrl(baseUrlFor(5205), 'abc', 'state-1'));
    assert.equal(url.searchParams.get('client_id'), CLIENT_ID);
    assert.equal(url.searchParams.get('redirect_uri'), REDIRECT_URI);
    assert.equal(url.searchParams.get('code_challenge_method'), 'S256');
    assert.equal(url.searchParams.get('response_type'), 'code');
    assert.match(url.searchParams.get('scope'), /\boffline_access\b/);
    assert.equal(url.origin, 'http://127.0.0.1:5205');
  });
});

describe('the capabilities answer is judged, not merely received', () => {
  const good = { contractVersion: 1, pageSizeMax: 500, uploadRowsMax: 500, features: ['download', 'upload'] };

  it('accepts the answer this server gives', () => {
    assert.equal(capabilitiesProblem(good), null);
  });

  // The positive case above is worthless without these: a check that accepted anything would
  // pass against any HTTP server that answered at all, which is how a green run comes to mean
  // nothing.
  it('refuses an answer from a server speaking another contract version', () => {
    assert.match(capabilitiesProblem({ ...good, contractVersion: 2 }), /contractVersion/);
  });

  it('refuses an answer that does not serve both directions', () => {
    assert.match(capabilitiesProblem({ ...good, features: ['download'] }), /download and upload/);
  });

  it('refuses limits a device cannot size itself to', () => {
    assert.match(capabilitiesProblem({ ...good, pageSizeMax: 0 }), /positive/);
  });

  it('refuses something that is not an object at all', () => {
    assert.match(capabilitiesProblem('OK'), /not a JSON object/);
    assert.match(capabilitiesProblem(null), /not a JSON object/);
  });
});

describe('the seeded phone-side data is judged as the protection rules decide it', () => {
  const photo = (title, reach) => ({ document: { title, reach } });
  const documents = (links) => ({ links });
  // The open-cave photo can be linked from more than one place; each link carries the same reach.
  const good = documents([
    photo(OPEN_PHOTO_TITLE, 'original'),
    photo(OPEN_PHOTO_TITLE, 'original'),
    photo(SHARED_PHOTO_TITLE, 'locationFree'),
  ]);

  it('accepts the photo on the open cave as stored and the shared one without its location', () => {
    assert.equal(photoReachProblem(good), null);
  });

  it('refuses the shared photo handed over as stored, which is the leak this proves absent', () => {
    assert.match(photoReachProblem(documents([
      photo(OPEN_PHOTO_TITLE, 'original'),
      photo(SHARED_PHOTO_TITLE, 'original'),
    ])), /expected "locationFree"/);
  });

  it('refuses an answer that lacks either photo, so a seeder that wrote one cannot pass', () => {
    assert.match(photoReachProblem(documents([photo(SHARED_PHOTO_TITLE, 'locationFree')])), /was not in/);
    assert.match(photoReachProblem(documents([photo(OPEN_PHOTO_TITLE, 'original')])), /was not in/);
    assert.match(photoReachProblem({}), /no links/);
  });

  const openId = '00000000-0000-4000-8000-00000000000a';
  const protectedId = '00000000-0000-4000-8000-00000000000b';
  const map = (...placeIds) => ({ maps: [{ title: RASTER_TITLE, pins: placeIds.map((placeId) => ({ placeId })) }] });

  it('accepts the map with the one pin on the open place', () => {
    assert.equal(withheldPinProblem(map(openId), openId), null);
  });

  it('refuses both pins, which would mean the protected place leaked, and none', () => {
    assert.match(withheldPinProblem(map(openId, protectedId), openId), /arrived with 2 pins/);
    assert.match(withheldPinProblem(map(), openId), /arrived with 0 pins/);
  });

  it('refuses a single pin on the protected place, which a count of pins would have accepted', () => {
    assert.match(withheldPinProblem(map(protectedId), openId), /other than "Demo marker A"/);
  });

  it('refuses rather than accepts when the open place could not be named', () => {
    assert.match(withheldPinProblem(map(openId), null), /cannot be named/);
    assert.match(withheldPinProblem(map(openId), undefined), /cannot be named/);
  });

  it('refuses an answer without the seeded map', () => {
    assert.match(withheldPinProblem({ maps: [] }, openId), /was not in/);
    assert.match(withheldPinProblem(null, openId), /no maps/);
  });

  it('reads the open place out of the download only when it is there exactly once', () => {
    const feature = (id, name) => ({ id, name });
    assert.equal(openPlaceIdIn({ features: [feature(openId, OPEN_PLACE_NAME), feature(protectedId, 'Other')] }), openId);
    assert.equal(openPlaceIdIn({ features: [feature(protectedId, 'Other')] }), null);
    assert.equal(openPlaceIdIn({ features: [feature(openId, OPEN_PLACE_NAME), feature(protectedId, OPEN_PLACE_NAME)] }), null);
    assert.equal(openPlaceIdIn(null), null);
  });
});

describe('what the script prints', () => {
  it('names the port it actually served on, everywhere in the transcript', () => {
    const printed = tokenDanceCurl(baseUrlFor(6543));
    assert.doesNotMatch(printed, /127\.0\.0\.1:5205/);
    assert.match(printed, /127\.0\.0\.1:6543\/api\/v1\/auth\/login/);
    assert.match(printed, /127\.0\.0\.1:6543\/connect\/token/);
    assert.match(printed, /127\.0\.0\.1:6543\/api\/v1\/sync\/capabilities/);
  });

  it('signs in as the account that owns nothing', () => {
    const printed = tokenDanceCurl(baseUrlFor(5205));
    assert.match(printed, new RegExp(MEMBER_EMAIL));
    assert.match(printed, new RegExp(MEMBER_PASSWORD));
    // The administrator sees every coordinate exactly, so a transcript that signed in as it
    // would demonstrate none of the rules this API exists to respect.
    assert.doesNotMatch(printed, new RegExp(ADMIN_EMAIL));
  });

  it('tells the reader not to follow the redirect that carries the code', () => {
    const printed = tokenDanceCurl(baseUrlFor(5205));
    assert.match(printed, /Do NOT follow the redirect/);
    assert.doesNotMatch(printed, /curl [^\n]*-L\b/);
  });
});

describe('the command line', () => {
  it('reads --name value and --flag', () => {
    assert.deepEqual(parseArgs(['--db-port', '6000', '--quiet']), { 'db-port': '6000', quiet: true });
    assert.deepEqual(parseArgs([]), {});
  });
});

describe('the review command', () => {
  // A stand-in for the review routes: two pages of queue, a batch with a waiting line and a
  // decided one, and decisions that either answer or refuse with a problem document. What is
  // pinned is what the command asks for and what it makes of the answer — the routes themselves
  // are the server's, and its own suite tests them.
  let server;
  let baseUrl;
  const seen = [];
  const batches = [
    { id: 'b1', lines: [
      { reviewId: 'r1', section: 'rows', rowId: 'row-1', decision: 'pending' },
      { reviewId: 'r0', section: 'rows', rowId: 'row-0', decision: 'rejected' },
    ] },
    { id: 'b2', lines: [{ reviewId: 'r2', section: 'documentLinks', rowId: null, decision: 'pending' }] },
  ];

  before(async () => {
    server = createServer((req, res) => {
      let body = '';
      req.on('data', (chunk) => { body += chunk; });
      req.on('end', () => {
        seen.push({ method: req.method, url: req.url, auth: req.headers.authorization, body });
        const send = (status, payload) => {
          res.writeHead(status, { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' });
          res.end(JSON.stringify(payload));
        };
        const url = new URL(req.url, 'http://x');
        if (req.method === 'GET' && url.pathname === '/api/v1/review/batches') {
          const page = Number(url.searchParams.get('page'));
          return send(200, { items: page <= 2 ? [{ id: batches[page - 1].id }] : [], page, pageSize: 1, totalItems: 2 });
        }
        const detail = /^\/api\/v1\/review\/batches\/(\w+)$/.exec(url.pathname);
        if (req.method === 'GET' && detail) {
          const batch = batches.find((b) => b.id === detail[1]);
          return send(200, { batch: { id: batch.id }, lines: batch.lines });
        }
        const decide = /^\/api\/v1\/review\/items\/([\w-]+)\/(accept|reject)$/.exec(url.pathname);
        if (req.method === 'POST' && decide) {
          if (decide[1] === 'gone') {
            return send(409, { status: 409, title: 'Not waiting', detail: 'That row was decided.', code: 'review.not_pending' });
          }
          return send(200, {
            decided: [
              { reviewId: decide[1], decision: decide[2] === 'accept' ? 'accepted' : 'rejected', failureCode: null },
              { reviewId: 'dependent', decision: 'failed', failureCode: 'review.dependency_failed' },
            ],
            batches: [],
          });
        }
        return send(404, { status: 404, code: 'route.not_found' });
      });
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    baseUrl = `http://127.0.0.1:${server.address().port}`;
  });

  after(() => server?.close());

  it('lists every waiting row of every page of the queue, and nothing already decided', async () => {
    const waiting = await reviewWaiting(baseUrl, 'tok');
    assert.deepEqual(waiting, [
      { reviewId: 'r1', entity: 'rows', rowId: 'row-1' },
      { reviewId: 'r2', entity: 'documentLinks', rowId: null },
    ]);
    assert.deepEqual(waiting.map(waitingLine), ['r1  rows  row-1', 'r2  documentLinks  -']);
    assert.ok(seen.every((r) => r.auth === 'Bearer tok'), 'every call carries the administrator\'s token');
  });

  it('accepts and rejects through the line routes, the note riding along, one line per row decided', async () => {
    seen.length = 0;
    const accepted = await reviewDecide(baseUrl, 'tok', 'r1', true);
    assert.deepEqual(accepted.map(decidedLine), ['r1  accepted', 'dependent  failed  review.dependency_failed']);
    assert.equal(seen[0].method, 'POST');
    assert.equal(seen[0].url, '/api/v1/review/items/r1/accept');
    assert.deepEqual(JSON.parse(seen[0].body), {});

    const rejected = await reviewDecide(baseUrl, 'tok', 'r2', false, 'Wrong cave');
    assert.equal(rejected[0].outcome, 'rejected');
    assert.equal(seen[1].url, '/api/v1/review/items/r2/reject');
    assert.deepEqual(JSON.parse(seen[1].body), { note: 'Wrong cave' });
  });

  it('turns a refusal into the problem code, which the command prints before exiting 1', async () => {
    await assert.rejects(reviewDecide(baseUrl, 'tok', 'gone', true), (error) => {
      assert.ok(error instanceof ReviewRefusal);
      assert.equal(error.status, 409);
      assert.equal(error.code, 'review.not_pending');
      assert.match(error.message, /^refused: 409 review\.not_pending/);
      return true;
    });
  });

  it('reads the review id and the note off the command line, wherever the note sits', () => {
    assert.deepEqual(positionals(['reject', '--note', 'Wrong cave', 'r2']), ['reject', 'r2']);
    assert.deepEqual(positionals(['reject', 'r2', '--note', 'Wrong cave', '--api-port', '6001']), ['reject', 'r2']);
    assert.deepEqual(parseArgs(['reject', 'r2', '--note', 'Wrong cave']), { note: 'Wrong cave' });
  });

  it('is dispatched, signs in as the administrator, and is documented', () => {
    assert.match(script, /case 'review': await reviewCommand\(options, positionals\(rest\)\)/);
    assert.match(script, /signIn\(baseUrl, ADMIN_EMAIL, ADMIN_PASSWORD\)/);
    const doc = readFileSync(join(deployDir, '..', 'docs', 'speleoloc-sync', '07-dev-server.md'), 'utf8');
    assert.match(doc, /speleoloc-dev\.mjs review list/);
    assert.match(doc, /\/api\/v1\/review\/items\/\$REVIEW_ID\/accept/);
  });
});

describe('the port a flag asked for reaches docker compose', () => {
  // The compose file reads both ports from the environment. A flag that stopped at the script
  // would leave the database on the default port while the connection string, the readiness
  // probe and the printed transcript all named another — a healthy database with a probe timing
  // out against it, which reads as Docker being broken rather than as a dropped flag.
  it('names the ports it was given', () => {
    const environment = composeEnvironment(6000, 6001, {});
    assert.equal(environment.SILEXGIS_SPELEOLOC_DB_PORT, '6000');
    assert.equal(environment.SILEXGIS_SPELEOLOC_API_PORT, '6001');
  });

  it('keeps the rest of the environment', () => {
    assert.equal(composeEnvironment(1, 2, { PATH: '/x' }).PATH, '/x');
  });

  it('is what the compose invocation is actually given', () => {
    // Pinned on the call as well as on the helper: a helper nothing passes to `spawnSync` is
    // exactly the shape of this defect, and it would keep its own test green.
    assert.match(script, /env: composeEnvironment\(ports\.dbPort, ports\.apiPort\)/);
    assert.doesNotMatch(script, /compose\('up'/);
  });

  it('reads the same defaults the compose file falls back to', () => {
    assert.equal(composeEnvironment(DEFAULT_DB_PORT, DEFAULT_API_PORT, {}).SILEXGIS_SPELEOLOC_DB_PORT,
      String(DEFAULT_DB_PORT));
    assert.match(compose, new RegExp(`SILEXGIS_SPELEOLOC_DB_PORT:-${DEFAULT_DB_PORT}`));
    assert.match(compose, new RegExp(`SILEXGIS_SPELEOLOC_API_PORT:-${DEFAULT_API_PORT}`));
  });
});

describe('a container of this name that is somebody else\'s', () => {
  // The guard is an allow-list, and these are the cases it must not widen into. The one that
  // used to slip through is the third: a real compose project with another name matched no
  // branch at all, so the stack proceeded to migrate and seed a stranger's database.
  it('accepts no container at all, and its own', () => {
    assert.equal(foreignContainerProblem(''), null);
    assert.equal(foreignContainerProblem('   \n'), null);
    assert.equal(foreignContainerProblem(COMPOSE_PROJECT), null);
    assert.equal(foreignContainerProblem(`${COMPOSE_PROJECT}\n`), null);
  });

  it('accepts an inspect that could not be answered, rather than blocking on it', () => {
    assert.equal(foreignContainerProblem('Error: No such object: silexgis-speleoloc-db'), null);
    // Docker 29's spelling of the same answer; read as a foreign project, it blocked every `up`.
    assert.equal(foreignContainerProblem('error: no such object: silexgis-speleoloc-db'), null);
  });

  it('refuses a container another compose project owns', () => {
    assert.match(foreignContainerProblem('silexgis-dev'), /silexgis-dev/);
    assert.match(foreignContainerProblem('silexgis-dev'), new RegExp(COMPOSE_PROJECT));
  });

  it('refuses one made by a plain docker run', () => {
    assert.match(foreignContainerProblem('<no value>'), /docker run/);
  });
});

// Opt-in: stands the whole stack up itself and signs in for real. It needs Docker and the .NET
// SDK, neither of which the continuous-integration runner for this file has, so it is skipped
// there rather than failing — and the skip says why, because a silently absent test is the same
// as no test.
//
// It runs `up` rather than assuming something is already listening. A test that only fetched a
// port would prove nothing about the orchestration this file exists to guard: the guard against a
// foreign container, the seeding, and the port the API is actually served on are all in the part
// that a fetch against an already-running server never executes. If a stack is already up — a
// developer's own session — it is used as it stands and left running afterwards.
const LIVE = process.env.SILEXGIS_SPELEOLOC_DEV_LIVE === '1';
const SCRIPT = join(deployDir, 'speleoloc-dev.mjs');
// Overridable so a machine where the default ports are taken can still run it; both are passed
// to `up` and `down` as well, so the stack and this file agree on where it listens.
const LIVE_API_PORT = Number(process.env.SILEXGIS_SPELEOLOC_DEV_API_PORT ?? DEFAULT_API_PORT);
const LIVE_DB_PORT = Number(process.env.SILEXGIS_SPELEOLOC_DEV_DB_PORT ?? DEFAULT_DB_PORT);
const PORT_ARGS = ['--api-port', String(LIVE_API_PORT), '--db-port', String(LIVE_DB_PORT)];
const BASE_URL = baseUrlFor(LIVE_API_PORT);

/** The status the sync route answers, or null when nothing is listening yet. */
async function capabilitiesStatus() {
  try {
    const res = await fetch(`${BASE_URL}/api/v1/sync/capabilities`, { redirect: 'manual' });
    return res.status;
  } catch {
    return null;
  }
}

describe('the server this script starts', () => {
  const skip = LIVE ? false : 'needs Docker and the .NET SDK; set SILEXGIS_SPELEOLOC_DEV_LIVE=1 to run it';
  let child = null;
  const log = [];

  // Long, because the first run of this on a machine builds the server and pulls a PostGIS image.
  before(async () => {
    if (!LIVE) {
      return;
    }

    // 401 is the only answer that means "this stack is already up and healthy". Anything else
    // on this port is either a server whose database has gone — a stale `up` outliving its
    // container is the ordinary way that happens — or somebody else's service, and reusing
    // either would report a broken stack as a broken change.
    const standing = await capabilitiesStatus();
    if (standing === 401) {
      return;
    }

    assert.equal(standing, null, `something is already listening on ${BASE_URL} and answers `
      + `${standing} rather than 401. Stop it (or run \`down\`) before running these.`);

    // Its own process group, so the teardown below can signal the whole tree. A `dotnet run`
    // left behind keeps this file's stdio open and the test runner never exits.
    //
    // Its output is captured rather than inherited. Under `node --test` this process's stdout is
    // the runner's own message channel, and a child writing megabytes of server log straight
    // into it corrupts that channel — the file is then reported as failed with a deserialisation
    // error while every test in it passed. So the log is kept here and shown only when something
    // goes wrong, which is the only time anybody wants to read it.
    child = spawn(process.execPath, [SCRIPT, 'up', ...PORT_ARGS], {
      stdio: ['ignore', 'pipe', 'pipe'],
      detached: detachApi(),
    });
    let exited = false;
    child.on('exit', () => { exited = true; });
    for (const stream of [child.stdout, child.stderr]) {
      stream.setEncoding('utf8');
      stream.on('data', (chunk) => {
        log.push(chunk);
        // The reason a stack failed to come up is near the end, and the whole log is large.
        if (log.length > 400) {
          log.splice(0, log.length - 400);
        }
      });
    }

    const tail = () => `\n--- the last of what \`up\` printed ---\n${log.join('')}`;
    const deadline = Date.now() + 900_000;
    for (;;) {
      if ((await capabilitiesStatus()) !== null) {
        return;
      }
      assert.equal(exited, false, '`speleoloc-dev.mjs up` exited before the API answered. Both '
        + 'seeders exit zero when they refuse, so the reason is in its output.' + tail());
      assert.ok(Date.now() < deadline, 'the stack did not come up within fifteen minutes.' + tail());
      await new Promise((done) => setTimeout(done, 2000));
    }
  }, { timeout: 960_000 });

  after(async () => {
    if (child === null) {
      return;
    }

    // Ctrl-C is what a developer sends it, and it is the path the script handles: the API stops
    // and the database is left running. `down` then stops the container, keeping its volume.
    // Stopped through the script's own teardown, so this hook cannot work on a platform the
    // script itself does not.
    stopApiTree(child);

    await Promise.race([once(child, 'exit'), new Promise((done) => setTimeout(done, 30_000))]);
    spawnSync(process.execPath, [SCRIPT, 'down', ...PORT_ARGS], { stdio: ['ignore', 'ignore', 'inherit'] });
  }, { timeout: 120_000 });

  it('refuses the sync capabilities to a caller with no token', { skip, timeout: 60_000 }, async () => {
    assert.equal(await capabilitiesStatus(), 401);
  });

  it('answers them to the seeded member account, through the whole sign-in dance',
    { skip, timeout: 120_000 },
    async () => {
      const body = await signInAndReadCapabilities(BASE_URL, MEMBER_EMAIL, MEMBER_PASSWORD);
      assert.equal(capabilitiesProblem(body), null);
      assert.equal(body.contractVersion, 1);
      // Everything this build serves, and `people` because the development installation has the
      // directory on and the member may read it.
      assert.deepEqual([...body.features].sort(), [
        'documentUpload', 'documents', 'download', 'people', 'rasters', 'reviews', 'trips', 'upload',
      ]);
    });

  it('hands the member one photo without its location and one raster pin fewer than were seeded',
    { skip, timeout: 120_000 },
    async () => {
      const tokens = await signIn(BASE_URL, MEMBER_EMAIL, MEMBER_PASSWORD);
      assert.equal(capabilitiesProblem(await readCapabilities(BASE_URL, tokens.access_token)), null);
      await proveSyncContent(BASE_URL, tokens.access_token);
    });

  it('lets the review command decide what the account under review sent, and its phone reads the decision',
    { skip, timeout: 180_000 },
    async () => {
      const phone = await signIn(BASE_URL, REVIEW_EMAIL, MEMBER_PASSWORD);
      const headers = { Authorization: `Bearer ${phone.access_token}`, 'Content-Type': 'application/json' };
      const created = await fetch(`${BASE_URL}/api/v1/sync/sets`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ name: `review check ${Date.now()}`, uploadVisibility: 'private', rootFeatureIds: [], settings: {} }),
      });
      const set = await created.json();
      assert.equal(created.status, 201, JSON.stringify(set));
      try {
        const upload = await fetch(`${BASE_URL}/api/v1/sync/sets/${set.id}/upload`, {
          method: 'POST',
          headers,
          body: JSON.stringify({
            batchId: crypto.randomUUID(),
            contractVersion: 1,
            clientFeatures: ['held'],
            // A phone user is what an account that owns nothing and may create no cave can send:
            // anything it may not write is refused outright rather than held.
            people: [{ deviceUserId: crypto.randomUUID(), username: 'invented-phone-user' }],
          }),
        });
        const answer = await upload.json();
        assert.equal(upload.status, 200, JSON.stringify(answer));
        const { reviewId, status } = answer.people[0];
        assert.equal(status, 'held', JSON.stringify(answer.people[0]));

        const admin = await signIn(BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD);
        assert.ok((await reviewWaiting(BASE_URL, admin.access_token)).some((line) => line.reviewId === reviewId));
        const decided = await reviewDecide(BASE_URL, admin.access_token, reviewId, false, 'Not this one');
        assert.deepEqual(decided.map((line) => [line.reviewId, line.outcome]), [[reviewId, 'rejected']]);
        await assert.rejects(reviewDecide(BASE_URL, admin.access_token, reviewId, false), ReviewRefusal);

        const reviews = await (await fetch(`${BASE_URL}/api/v1/sync/sets/${set.id}/reviews`, { headers })).json();
        const told = reviews.reviews.find((review) => review.reviewId === reviewId);
        assert.equal(told?.outcome, 'rejected');
        assert.equal(told?.note, 'Not this one');
      } finally {
        await fetch(`${BASE_URL}/api/v1/sync/sets/${set.id}`, { method: 'DELETE', headers }).catch(() => {});
      }
    });
});
