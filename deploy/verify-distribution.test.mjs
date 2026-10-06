// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests for the distribution check.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.
//
// Importing the script does not run it: it only acts when it is the entry point, so nothing
// here builds an image or starts a container. What is tested is everything about the check
// that can be wrong without a container to show it — where it looks for the stack, how it
// judges a document, and what it does to an installation when something fails half way.
//
// The report check is driven against a small server written out below, which answers the way
// the API does. That is not a substitute for the packaged image, and is not meant as one: the
// continuous-integration job that builds the images runs the real thing. It is what makes the
// order of the requests, the judgement of each answer and the clearing up afterwards something
// a change to this script cannot break silently on a machine with no Docker.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join } from 'node:path';
import { after, before, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { checkReports, stackSettings, writeUpProblem, zipEntryNames } from './verify-distribution.mjs';

const deployDir = dirname(fileURLToPath(import.meta.url));
const read = (...parts) => readFileSync(join(deployDir, ...parts), 'utf8');

const script = read('verify-distribution.mjs');
const composeFile = read('docker-compose.yml');

const DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

// ---- a zip, written out by hand ---------------------------------------------------------------
//
// Stored rather than compressed, with real checksums, so what the tests read is an archive any
// unzip tool would open — the reader under test is given the real format, not a shape that
// happens to satisfy it.

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k += 1) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }
  return c >>> 0;
});

function crc32(bytes) {
  let c = 0xffffffff;
  for (const byte of bytes) {
    c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}

function zip(entries, comment = '') {
  const files = [];
  const directory = [];
  let offset = 0;
  for (const [name, text] of entries) {
    const nameBytes = Buffer.from(name, 'utf8');
    const data = Buffer.from(text, 'utf8');
    const checksum = crc32(data);

    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt32LE(checksum, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    files.push(local, nameBytes, data);

    const entry = Buffer.alloc(46);
    entry.writeUInt32LE(0x02014b50, 0);
    entry.writeUInt16LE(20, 4);
    entry.writeUInt16LE(20, 6);
    entry.writeUInt32LE(checksum, 16);
    entry.writeUInt32LE(data.length, 20);
    entry.writeUInt32LE(data.length, 24);
    entry.writeUInt16LE(nameBytes.length, 28);
    entry.writeUInt32LE(offset, 42);
    directory.push(entry, nameBytes);

    offset += local.length + nameBytes.length + data.length;
  }

  const directoryBytes = Buffer.concat(directory);
  const commentBytes = Buffer.from(comment, 'utf8');
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directoryBytes.length, 12);
  end.writeUInt32LE(offset, 16);
  end.writeUInt16LE(commentBytes.length, 20);
  return Buffer.concat([...files, directoryBytes, end, commentBytes]);
}

/** The parts a word-processor document is made of, as an archive. */
const writeUp = (extra = []) => zip([
  ['[Content_Types].xml', '<Types/>'],
  ['_rels/.rels', '<Relationships/>'],
  ['word/document.xml', '<w:document/>'],
  ...extra,
]);

describe('the parts of a document are read from the archive itself', () => {
  it('names every file in it', () => {
    assert.deepEqual(zipEntryNames(writeUp()), ['[Content_Types].xml', '_rels/.rels', 'word/document.xml']);
  });

  it('finds the directory behind a comment at the end of the archive', () => {
    const commented = zip([['word/document.xml', '<w:document/>']], 'written by a word processor');
    assert.deepEqual(zipEntryNames(commented), ['word/document.xml']);
  });

  it('reads an archive with nothing in it as holding nothing', () => {
    assert.deepEqual(zipEntryNames(zip([])), []);
  });

  // The positive cases above are worthless without these: a reader that answered a list for
  // anything it was handed would let an error page through as a document.
  it('refuses a body that is not an archive at all', () => {
    assert.throws(() => zipEntryNames(Buffer.from('<html><body>500 Internal Server Error</body></html>')),
      /no end-of-directory record/);
    assert.throws(() => zipEntryNames(Buffer.alloc(0)), /no end-of-directory record/);
    assert.throws(() => zipEntryNames(Buffer.from('PK')), /no end-of-directory record/);
  });

  it('refuses a document that was cut short', () => {
    // The record that says where the directory is sits at the very end, so a body that stopped
    // arriving has lost it whatever else it still holds.
    const whole = writeUp();
    assert.throws(() => zipEntryNames(whole.subarray(0, whole.length - 10)), /no end-of-directory record/);
  });

  it('refuses a directory that does not hold what its closing record promises', () => {
    const damaged = Buffer.from(writeUp());
    // The closing record is the last 22 bytes; the directory's position is 16 bytes into it.
    damaged.writeUInt32LE(4, damaged.length - 22 + 16);
    assert.throws(() => zipEntryNames(damaged), /directory is damaged/);
  });
});

describe('a write-up is judged, not merely received', () => {
  it('accepts a word-processor document that holds a document', () => {
    assert.equal(writeUpProblem(200, DOCX, writeUp()), null);
    assert.equal(writeUpProblem(200, `${DOCX}; charset=binary`, writeUp()), null);
  });

  it('refuses a failure, and says how the API worded it', () => {
    const refusal = Buffer.from(JSON.stringify({
      status: 500, code: 'server.error', detail: 'The writer failed.',
    }));
    const problem = writeUpProblem(500, 'application/problem+json', refusal);
    assert.match(problem, /500/);
    assert.match(problem, /server\.error/);
    assert.match(problem, /The writer failed\./);
  });

  it('refuses a failure that says nothing readable, by its status alone', () => {
    const gateway = writeUpProblem(502, 'text/html', Buffer.from('<html>Bad Gateway</html>'));
    assert.match(gateway, /it answered 502$/);
  });

  it('refuses a 200 that is not served as a document', () => {
    // A front that answered its own page for a route it did not recognise is a 200 as well.
    assert.match(writeUpProblem(200, 'text/html', Buffer.from('<!doctype html>')), /served as "text\/html"/);
    assert.match(writeUpProblem(200, null, writeUp()), /served as "nothing"/);
  });

  it('refuses the right type around the wrong bytes', () => {
    assert.match(writeUpProblem(200, DOCX, Buffer.from('not an archive')), /not a zip archive/);
  });

  it('refuses an archive that holds no document', () => {
    const spreadsheet = zip([['[Content_Types].xml', '<Types/>'], ['xl/workbook.xml', '<workbook/>']]);
    const problem = writeUpProblem(200, DOCX, spreadsheet);
    assert.match(problem, /no word\/document\.xml/);
    assert.match(problem, /xl\/workbook\.xml/);
  });
});

describe('where the stack is, read from what compose resolved', () => {
  /** The resolved configuration, as `docker compose config --format json` prints the parts read here. */
  const resolved = ({
    port = { mode: 'ingress', target: 80, published: '8080', protocol: 'tcp' },
    environment = {},
  } = {}) => ({
    name: 'silexgis',
    services: {
      api: {
        environment: {
          SILEXGIS__Admin__Email: 'admin@example.com',
          SILEXGIS__Admin__Password: 'change-me-please',
          SILEXGIS__Files__MaxUploadBytes: '536870912',
          SILEXGIS__PublicUrl: 'http://localhost:8080',
          ...environment,
        },
      },
      web: { ports: port ? [port] : [] },
    },
  });

  /** The settings read from a stack whose web port was published this way. */
  const published = (port) => stackSettings(resolved({ port: { target: 80, ...port } }));

  /** The settings read from a stack whose API was given these values. */
  const given = (environment) => stackSettings(resolved({ environment }));

  it('looks on localhost when the port is published on every interface', () => {
    const stack = stackSettings(resolved());
    assert.equal(stack.origin, 'http://localhost:8080');
    assert.equal(stack.port, 8080);
    // A socket is opened to an address and never to the name, which may resolve somewhere
    // nothing is listening.
    assert.equal(stack.socketHost, '127.0.0.1');
  });

  it('looks at the address the port was bound to, when it was bound to one', () => {
    const stack = published({ published: '8097', host_ip: '127.0.0.1' });
    assert.equal(stack.origin, 'http://127.0.0.1:8097');
    assert.equal(stack.socketHost, '127.0.0.1');
    assert.equal(stack.port, 8097);
  });

  it('treats an explicit every-interface address as every interface', () => {
    assert.equal(published({ published: '8080', host_ip: '0.0.0.0' }).origin, 'http://localhost:8080');
    assert.equal(published({ published: '8080', host_ip: '::' }).origin, 'http://localhost:8080');
  });

  it('brackets an IPv6 address in the URL and not in the socket', () => {
    const stack = published({ published: '8080', host_ip: '::1' });
    assert.equal(stack.origin, 'http://[::1]:8080');
    assert.equal(stack.socketHost, '::1');
  });

  it('takes the port that reaches the web server, whatever else is published', () => {
    const config = resolved();
    config.services.web.ports = [{ target: 9113, published: '9113' }, { target: 80, published: 8081 }];
    assert.equal(stackSettings(config).port, 8081);
  });

  it('says so when the web front publishes no port, rather than probing a guess', () => {
    assert.throws(() => stackSettings(resolved({ port: null })), /publishes no host port/);
    assert.throws(() => stackSettings({}), /publishes no host port/);
  });

  it('reads the upload cap the stack was given, and the API\'s own when it was given none', () => {
    assert.equal(given({ SILEXGIS__Files__MaxUploadBytes: '1048576' }).maxUploadBytes, 1048576);
    assert.equal(given({ SILEXGIS__Files__MaxUploadBytes: undefined }).maxUploadBytes, 512 * 1024 * 1024);
  });

  it('names the administrator only when the stack was given a whole one', () => {
    assert.deepEqual(given({}).admin, { email: 'admin@example.com', password: 'change-me-please' });
    assert.equal(given({ SILEXGIS__Admin__Password: '' }).admin, null);
    assert.equal(given({ SILEXGIS__Admin__Email: '  ' }).admin, null);
    assert.equal(given({ SILEXGIS__Admin__Email: null }).admin, null);
  });

  it('trims the public address the way the API does before it registers the web client', () => {
    assert.equal(given({ SILEXGIS__PublicUrl: 'https://caves.example.org/' }).publicUrl,
      'https://caves.example.org');
    assert.equal(given({ SILEXGIS__PublicUrl: '' }).publicUrl, 'http://localhost:8080');
  });

  it('reads names the compose file actually gives the stack', () => {
    // Every one of these is read by name out of the resolved configuration. A name that was
    // renamed in the compose file would not fail here on its own — the administrator would
    // simply be missing and the upload cap would quietly become the default — so the names are
    // pinned against the file they come from.
    for (const name of ['SILEXGIS__Admin__Email', 'SILEXGIS__Admin__Password',
      'SILEXGIS__Files__MaxUploadBytes', 'SILEXGIS__PublicUrl']) {
      assert.match(composeFile, new RegExp(`^\\s+${name}: `, 'm'), `${name} is not set on the api service`);
      assert.match(script, new RegExp(`environment\\.${name}\\b`), `${name} is not read by the script`);
    }
    assert.match(composeFile, /^\s+- "\$\{SILEXGIS_HTTP_PORT:-8080\}:80"$/m);
  });

  it('is asked of compose, and the answer is not echoed', () => {
    // The resolved configuration holds two passwords. `run` prints what it runs and lets the
    // output through; this one command must do neither.
    assert.match(script, /execSync\(`\$\{compose\} config --format json`/);
    assert.doesNotMatch(script, /run\(`\$\{compose\} config/);
  });
});

// ---- a server that answers the way the API does ----------------------------------------------

const ADMIN = { email: 'admin@caves.example.org', password: 'the-right-password' };
const PUBLIC_URL = 'https://caves.example.org';
const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const CAMP_ID = '22222222-2222-4222-8222-222222222222';
const DOCUMENT_ID = '33333333-3333-4333-8333-333333333333';
const FILE_ID = '44444444-4444-4444-8444-444444444444';

function problem(res, status, code, detail) {
  res.writeHead(status, { 'Content-Type': 'application/problem+json' });
  res.end(JSON.stringify({ status, code, detail }));
}

function json(res, status, body, headers = {}) {
  res.writeHead(status, { 'Content-Type': 'application/json', ...headers });
  res.end(JSON.stringify(body));
}

function document(res) {
  res.writeHead(200, { 'Content-Type': DOCX, 'Content-Disposition': 'attachment; filename=write-up.docx' });
  res.end(writeUp());
}

/**
 * What the stand-in holds and has been asked. `intercept` lets one test answer one route
 * differently; it returns true when it has answered.
 */
function standIn() {
  const state = {
    trips: new Map(),
    camps: new Map(),
    documents: new Set(),
    asked: [],
    challenge: null,
    scope: null,
    intercept: () => false,
  };

  const server = createServer(async (req, res) => {
    const url = new URL(req.url, 'http://stand-in');
    const chunks = [];
    for await (const chunk of req) {
      chunks.push(chunk);
    }
    const body = Buffer.concat(chunks).toString('utf8');
    const route = `${req.method} ${url.pathname}`;
    state.asked.push(route);

    if (state.intercept(route, res, state)) {
      return;
    }

    if (route === 'POST /api/v1/auth/login') {
      const { email, password } = JSON.parse(body);
      if (email !== ADMIN.email || password !== ADMIN.password) {
        problem(res, 401, 'auth.invalid_credentials', 'Invalid email or password.');
        return;
      }
      json(res, 200, { userId: 'u', email, displayName: 'Administrator' },
        { 'Set-Cookie': 'silexgis.session=the-session; path=/; samesite=lax; httponly' });
      return;
    }

    if (route === 'GET /connect/authorize') {
      const asked = url.searchParams;
      if (!(req.headers.cookie ?? '').includes('silexgis.session=the-session')) {
        res.writeHead(302, { Location: `/login?returnUrl=${encodeURIComponent(req.url)}` });
        res.end();
        return;
      }
      if (asked.get('client_id') !== 'silexgis-spa'
        || asked.get('redirect_uri') !== `${PUBLIC_URL}/auth/callback`
        || asked.get('response_type') !== 'code'
        || asked.get('code_challenge_method') !== 'S256') {
        res.writeHead(400, { 'Content-Type': 'text/html' });
        res.end('<html>invalid_request</html>');
        return;
      }
      state.challenge = asked.get('code_challenge');
      state.scope = asked.get('scope');
      const answer = new URLSearchParams({ code: 'the-code', state: asked.get('state'), iss: PUBLIC_URL });
      res.writeHead(302, { Location: `${PUBLIC_URL}/auth/callback?${answer}` });
      res.end();
      return;
    }

    if (route === 'POST /connect/token') {
      const form = new URLSearchParams(body);
      const proof = createHash('sha256').update(form.get('code_verifier') ?? '', 'ascii').digest('base64url');
      if (form.get('grant_type') !== 'authorization_code'
        || form.get('code') !== 'the-code'
        || form.get('client_id') !== 'silexgis-spa'
        || form.get('redirect_uri') !== `${PUBLIC_URL}/auth/callback`
        || proof !== state.challenge) {
        json(res, 400, { error: 'invalid_grant', error_description: 'The specified token is invalid.' });
        return;
      }
      json(res, 200, { access_token: 'the-token', token_type: 'Bearer', expires_in: 900 });
      return;
    }

    if (req.headers.authorization !== 'Bearer the-token') {
      res.writeHead(401);
      res.end();
      return;
    }

    if (route === 'POST /api/v1/trip-logs') {
      state.trips.set(TRIP_ID, JSON.parse(body));
      json(res, 201, { id: TRIP_ID, ...state.trips.get(TRIP_ID) });
    } else if (route === `GET /api/v1/trip-logs/${TRIP_ID}`) {
      if (state.trips.has(TRIP_ID)) {
        json(res, 200, { id: TRIP_ID });
      } else {
        problem(res, 404, 'trip_log.not_found');
      }
    } else if (route === `GET /api/v1/trip-logs/${TRIP_ID}/report` && state.trips.has(TRIP_ID)) {
      document(res);
    } else if (route === `POST /api/v1/trip-logs/${TRIP_ID}/report` && state.trips.has(TRIP_ID)) {
      state.documents.add(DOCUMENT_ID);
      json(res, 200, { documentId: DOCUMENT_ID, fileId: FILE_ID, fileName: 'trip-report-20261006.docx' });
    } else if (route === `DELETE /api/v1/trip-logs/${TRIP_ID}` && state.trips.has(TRIP_ID)) {
      state.trips.delete(TRIP_ID);
      state.documents.delete(DOCUMENT_ID);
      res.writeHead(204);
      res.end();
    } else if (route === `GET /api/v1/documents/${DOCUMENT_ID}`) {
      if (state.documents.has(DOCUMENT_ID)) {
        json(res, 200, { id: DOCUMENT_ID });
      } else {
        problem(res, 404, 'document.not_found');
      }
    } else if (route === 'POST /api/v1/expeditions') {
      state.camps.set(CAMP_ID, JSON.parse(body));
      json(res, 201, { id: CAMP_ID, ...state.camps.get(CAMP_ID) });
    } else if (route === `GET /api/v1/expeditions/${CAMP_ID}`) {
      if (state.camps.has(CAMP_ID)) {
        json(res, 200, { id: CAMP_ID });
      } else {
        problem(res, 404, 'expedition.not_found');
      }
    } else if (route === `GET /api/v1/expeditions/${CAMP_ID}/report` && state.camps.has(CAMP_ID)) {
      document(res);
    } else if (route === `DELETE /api/v1/expeditions/${CAMP_ID}` && state.camps.has(CAMP_ID)) {
      state.camps.delete(CAMP_ID);
      res.writeHead(204);
      res.end();
    } else {
      problem(res, 404, 'not_found');
    }
  });

  return { server, state };
}

describe('the report check, against a server that answers the way the API does', () => {
  const { server, state } = standIn();
  let stack;
  let said;
  const out = { write: (text) => { said += text; } };

  before(async () => {
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    stack = {
      origin: `http://127.0.0.1:${server.address().port}`,
      publicUrl: PUBLIC_URL,
      admin: ADMIN,
    };
  });

  after(() => {
    server.closeAllConnections();
    server.close();
  });

  beforeEach(() => {
    state.trips.clear();
    state.camps.clear();
    state.documents.clear();
    state.asked.length = 0;
    state.intercept = () => false;
    said = '';
  });

  /** Answers one route with a failure and every other route as usual. */
  const failing = (route, respond) => {
    state.intercept = (asked, res) => {
      if (asked !== route) {
        return false;
      }
      respond(res);
      return true;
    };
  };

  it('has both documents written, files the trip\'s, and leaves nothing behind', async () => {
    await checkReports(stack, out);

    assert.equal(state.trips.size, 0, 'the trip is still there');
    assert.equal(state.camps.size, 0, 'the camp is still there');
    assert.equal(state.documents.size, 0, 'the filed write-up is still there');

    assert.deepEqual(state.asked, [
      'POST /api/v1/auth/login',
      'GET /connect/authorize',
      'POST /connect/token',
      'POST /api/v1/trip-logs',
      `GET /api/v1/trip-logs/${TRIP_ID}/report`,
      `POST /api/v1/trip-logs/${TRIP_ID}/report`,
      `GET /api/v1/documents/${DOCUMENT_ID}`,
      `DELETE /api/v1/trip-logs/${TRIP_ID}`,
      `GET /api/v1/trip-logs/${TRIP_ID}`,
      `GET /api/v1/documents/${DOCUMENT_ID}`,
      'POST /api/v1/expeditions',
      `GET /api/v1/expeditions/${CAMP_ID}/report`,
      `DELETE /api/v1/expeditions/${CAMP_ID}`,
      `GET /api/v1/expeditions/${CAMP_ID}`,
    ]);

    const lines = said.trimEnd().split('\n');
    assert.equal(lines.length, 6);
    assert.equal(lines[0], `Signing in as ${ADMIN.email} ok`);
    assert.match(lines[1], /^Writing up a throwaway trip ok \(\d+ bytes in 3 parts\)$/);
    assert.equal(lines[2], 'Filing that write-up against the trip ok (trip-report-20261006.docx)');
    assert.equal(lines[3], 'Deleting the trip ok (its filed write-up went with it)');
    assert.match(lines[4], /^Writing up a throwaway camp ok \(\d+ bytes in 3 parts\)$/);
    assert.equal(lines[5], 'Deleting the camp ok');
  });

  it('makes records only their maker can see, named for what they are', async () => {
    let trip;
    let camp;
    state.intercept = (asked, _res, held) => {
      trip ??= held.trips.get(TRIP_ID);
      camp ??= held.camps.get(CAMP_ID);
      return false;
    };
    await checkReports(stack, out);

    // Private, so nobody else on the installation is shown either during the seconds it
    // exists; and titled so that one a killed run left behind explains itself.
    assert.equal(trip.visibility, 'private');
    assert.equal(camp.visibility, 'private');
    assert.match(trip.title, /^Distribution check \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC/);
    assert.match(trip.title, /safe to delete/);
    assert.equal(camp.name, trip.title);
    assert.match(trip.tripDate, /^\d{4}-\d{2}-\d{2}$/);
    assert.equal(camp.startDate, trip.tripDate);
    assert.deepEqual(trip.participants, []);
  });

  it('never files the camp\'s write-up', async () => {
    // Deleting a camp leaves a document that was filed on it standing, and no route removes a
    // document — so filing one here would leave it on every installation the check ran against.
    await checkReports(stack, out);
    assert.ok(!state.asked.includes(`POST /api/v1/expeditions/${CAMP_ID}/report`));
  });

  it('signs in as the web application, proving its key, and asks for no lasting credential', async () => {
    await checkReports(stack, out);
    // Without this scope no refresh token is issued, so the fifteen-minute access token is
    // all the check ever holds.
    assert.doesNotMatch(state.scope, /offline_access/);
    assert.match(state.scope, /\bopenid\b/);
  });

  it('stops at a refused sign-in, and says whose it was', async () => {
    await assert.rejects(
      checkReports({ ...stack, admin: { ...ADMIN, password: 'changed-since-first-start' } }, out),
      (err) => {
        assert.match(err.message, /signing in as admin@caves\.example\.org was refused/);
        assert.match(err.message, /401 auth\.invalid_credentials/);
        assert.match(err.message, /SILEXGIS_ADMIN_PASSWORD/);
        return true;
      },
    );
    assert.deepEqual(state.asked, ['POST /api/v1/auth/login']);
    assert.equal(said, `Signing in as ${ADMIN.email} failed\n`);
  });

  it('names the public address when the web client\'s return address is not accepted', async () => {
    await assert.rejects(
      checkReports({ ...stack, publicUrl: 'http://localhost:8080' }, out),
      /named http:\/\/localhost:8080\/auth\/callback .* SILEXGIS_PUBLIC_URL/,
    );
    assert.equal(state.trips.size, 0);
  });

  it('fails when the image cannot write the trip up, and takes the trip away again', async () => {
    failing(`GET /api/v1/trip-logs/${TRIP_ID}/report`,
      (res) => problem(res, 500, 'server.error', 'The writer failed.'));

    await assert.rejects(checkReports(stack, out),
      /\/report: it answered 500 server\.error The writer failed\./);
    assert.equal(state.trips.size, 0, 'the failed check left its trip behind');
    assert.ok(!state.asked.includes('POST /api/v1/expeditions'), 'the check went on after failing');
    assert.match(said, /Writing up a throwaway trip failed\n$/);
  });

  it('fails when a write-up comes back as something other than a document', async () => {
    failing(`GET /api/v1/expeditions/${CAMP_ID}/report`, (res) => {
      res.writeHead(200, { 'Content-Type': 'text/html' });
      res.end('<!doctype html><title>SilexGIS</title>');
    });

    await assert.rejects(checkReports(stack, out), /served as "text\/html"/);
    assert.equal(state.camps.size, 0, 'the failed check left its camp behind');
  });

  it('fails when filing answers without having filed anything', async () => {
    failing(`POST /api/v1/trip-logs/${TRIP_ID}/report`, (res) => json(res, 200, {
      documentId: '00000000-0000-0000-0000-000000000000', fileId: FILE_ID, fileName: 'trip-report.docx',
    }));

    await assert.rejects(checkReports(stack, out), /answered 200 and named no document/);
    assert.equal(state.trips.size, 0);
  });

  it('fails when the document it was told about cannot be read back', async () => {
    failing(`GET /api/v1/documents/${DOCUMENT_ID}`, (res) => problem(res, 404, 'document.not_found'));

    await assert.rejects(checkReports(stack, out), /the document the route said it filed is not there/);
    assert.equal(state.trips.size, 0);
  });

  it('fails when deleting the trip leaves its write-up on the installation', async () => {
    // The whole claim that the check can be run against an installation rests on this, so it
    // is checked on every run rather than assumed from how the delete is written today.
    failing(`DELETE /api/v1/trip-logs/${TRIP_ID}`, (res) => {
      state.trips.delete(TRIP_ID);
      res.writeHead(204);
      res.end();
    });

    const stillThere = `GET /api/v1/documents/${DOCUMENT_ID} answered 200 after the delete`;
    await assert.rejects(checkReports(stack, out),
      new RegExp(`${stillThere}, so the check has left it behind`));
    assert.match(said, /Deleting the trip failed\n$/);
  });

  it('says what it could not remove, and still reports the failure that came first', async () => {
    state.intercept = (asked, res) => {
      if (asked === `GET /api/v1/trip-logs/${TRIP_ID}/report`) {
        problem(res, 500, 'server.error');
        return true;
      }
      if (asked === `DELETE /api/v1/trip-logs/${TRIP_ID}`) {
        problem(res, 503, 'server.unavailable');
        return true;
      }
      return false;
    };

    await assert.rejects(checkReports(stack, out), /it answered 500 server\.error/);
    assert.match(said, new RegExp(
      `could not remove what the check made: DELETE /api/v1/trip-logs/${TRIP_ID} answered 503`));
  });
});

describe('the check is asked for where it is meant to run', () => {
  it('runs the report check when it is asked for, and says so when it is not', () => {
    // Pinned on the call as well as on the function: a check nothing calls keeps its own tests
    // green.
    assert.match(script, /if \(args\.has\('--reports'\)\) \{\s+await checkReports\(stack\);\s+\} else \{/);
    assert.match(script, /\} else \{\s+console\.log\(\s+'Report check skipped: /);
  });

  it('refuses to be asked for it without an administrator, before anything is built', () => {
    const refusal = script.indexOf("args.has('--reports') && !stack.admin");
    const build = script.indexOf('run(`${compose} build');
    assert.ok(refusal > 0, 'the refusal is gone');
    assert.ok(refusal < build, 'the images are built before the request is refused');
  });
});
