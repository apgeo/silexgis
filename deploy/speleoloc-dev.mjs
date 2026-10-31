// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A throwaway SilexGIS installation for writing a mobile client against the sync API.
//
// Cross-platform (Node 24, tested on 24 only, no shell-isms). Four commands:
//
//   node deploy/speleoloc-dev.mjs up
//       Starts a dedicated PostGIS container on a loopback port with its own named volume,
//       runs the schema migration and the always-on seeding, loads the demonstration dataset,
//       adds the sync-specific development data, serves the API from source on a loopback
//       port, proves it answers by signing in and reading the sync capabilities, proves the
//       phone-side data is there and protected (one photo handed over without its location,
//       one raster pin withheld), then prints
//       the base URL, both accounts, the caving group, the client id and a paste-ready
//       sequence of curl calls for the whole sign-in dance. Stays in the foreground; Ctrl-C
//       stops the API and leaves the database running.
//
//   node deploy/speleoloc-dev.mjs down
//       Stops the database container. The volume survives, so `up` comes back with the same
//       data. It does not reach an API: `up` stops the one it started when it exits, and an
//       API from an earlier run that somehow outlived it has to be stopped by hand — `up`
//       refuses to report success against one, rather than mistaking it for its own.
//
//   node deploy/speleoloc-dev.mjs reset
//       Removes the container and its volume, and the stored files and signing keys beside
//       them, so the next `up` starts from an empty installation.
//
//   node deploy/speleoloc-dev.mjs review list
//   node deploy/speleoloc-dev.mjs review accept <reviewId> [--note "…"]
//   node deploy/speleoloc-dev.mjs review reject <reviewId> [--note "…"]
//       Decides what `review@dev.local` sent while it waits for a reviewer, from the command
//       line rather than the browser, so a phone's live test can drive the whole review cycle.
//       Signs in as the development administrator and calls the review routes the browser uses
//       against the server `up` is serving: `list` prints one line per waiting row (review id,
//       section, row id), a decision one line per row it decided (review id, outcome, and the
//       code when the row could not be written). A refusal exits 1 with its status and code.
//
// WHY THIS DOES NOT USE THE SHARED DEVELOPMENT DATABASE
// docker-compose.dev.yml is the database the web application's developers use, published on
// every interface. Migrating and seeding it would rewrite their working data, and it would do
// so silently — a run against it looks identical to a run against a database of our own right
// up to the moment somebody else's session fails. So this stack has its own project name, its
// own container, its own volume and its own ports, and this script never reads that file.
//
// WHY IT SIGNS IN BEFORE DECLARING SUCCESS
// Both seeding commands exit zero when they refuse to do anything: they log the reason and
// return. A check that trusted the exit code would pass against a database with no accounts
// in it. The only signal that means what it says is completing the sign-in dance as the
// seeded non-administrator account and reading a real answer out of the sync API.

import { spawn, spawnSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { rmSync } from 'node:fs';
import { connect } from 'node:net';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(deployDir, '..');
const serverDir = join(repoRoot, 'server');
const apiProject = join(serverDir, 'src', 'SilexGis.Api');

/**
 * Where the API keeps the files it stores (the seeded photos and the raster map) and its signing
 * keys. Named explicitly because both default to a path relative to the working directory, which
 * for this script is the server folder of the checkout — stored bytes would land among the
 * sources as untracked files. Ignored by git; `reset` removes it with the database volume, so
 * the two never disagree about which files exist.
 */
export const STATE_DIR = join(repoRoot, '.speleoloc-dev');

/** The compose file that describes this stack. Standalone, not an overlay of the shipped one. */
const COMPOSE_FILE = 'docker-compose.speleoloc-dev.yml';

/** The compose project and the database container it owns, as the compose file names them. */
export const COMPOSE_PROJECT = 'silexgis-speleoloc-dev';
export const DB_CONTAINER = 'silexgis-speleoloc-db';

/**
 * Host port for the database. Not 5432 (the shared development database), not 5433 (what
 * docker-compose.dbport.yml publishes the production stack's database on).
 */
export const DEFAULT_DB_PORT = 5434;

/** Host port for the API. Not 5080, which `dotnet run` uses for the ordinary development API. */
export const DEFAULT_API_PORT = 5205;

/**
 * The bootstrap administrator. A development credential, printed in the documentation, which is
 * why nothing in this stack is reachable from outside the loopback interface.
 */
export const ADMIN_EMAIL = 'admin@dev.local';
export const ADMIN_PASSWORD = 'dev-admin-pass-1';

/**
 * The second account: a plain member of the caving group who owns nothing. It is the one worth
 * signing in as, because the administrator sees every coordinate exactly and therefore
 * demonstrates none of the protection rules the sync API exists to respect.
 */
export const MEMBER_EMAIL = 'member@dev.local';
export const MEMBER_PASSWORD = 'dev-member-pass-1';

/** The caving group both accounts belong to, and the demo cave that is visible only through it. */
export const GROUP_NAME = 'Demo Caving Club';
export const GROUP_VISIBLE_CAVE_CODE = 'DEMO-0006';

/** The demo cave whose exact position is protected; a non-administrator is refused it. */
export const PROTECTED_CAVE_CODE = 'DEMO-0002';

/**
 * The open demo cave the seeded photos, trip recording and raster map hang on. The titles below
 * are the seeder's, and are what `up` looks for when it proves the data reached the member.
 */
export const OPEN_CAVE_CODE = 'DEMO-0001';
/** Hung on the open cave alone: the member is handed the file as stored (`original`). */
export const OPEN_PHOTO_TITLE = 'Demo photo — open cave';
/**
 * Hung on the open cave and on the protected one: the member is handed a copy without the GPS
 * position (`locationFree`), because the protected cave is part of where the camera may have been.
 */
export const SHARED_PHOTO_TITLE = 'Demo photo — open and protected caves';
/** A raster map of the open cave with two pins, one of them on a place that is protected itself. */
export const RASTER_TITLE = 'Demo plan';
/**
 * The two places the map's pins stand on, inside the open cave. The first is ordinary and reaches
 * the member; the second is its own protection root and does not, so neither does its pin.
 */
export const OPEN_PLACE_NAME = 'Demo marker A';
export const PROTECTED_PLACE_NAME = 'Demo marker B (protected)';
export const RECORDING_TITLE = 'Demo trip recording';
/** A person on the club roster with no account, listed by the people directory. */
export const ROSTER_ONLY_CAVER = 'Demo Caver';

/**
 * A second plain member whose uploads wait for a reviewer. It signs in with the member's
 * password; an upload from it without `clientFeatures: ["held"]` is refused with
 * `sync.client_update_required`, and with it every row comes back `held`.
 */
export const REVIEW_EMAIL = 'review@dev.local';

/** The registered public client. No secret; PKCE is required. */
export const CLIENT_ID = 'silexgis-speleoloc';

/**
 * A loopback redirect the seeded client accepts. The registered entry carries no port and the
 * port is excluded from matching, so any loopback port works — but the value must be
 * byte-identical between the authorization request and the code exchange.
 */
export const REDIRECT_URI = 'http://127.0.0.1:54321/callback';

/** Everything a device asks for. `offline_access` is what produces a refresh token. */
export const SCOPE = 'openid profile email roles offline_access';

function fail(message, hint) {
  console.error(`\n${message}`);
  if (hint) {
    console.error(hint);
  }
  process.exit(1);
}

/**
 * The words of a command line that are neither a flag nor a flag's value, in order — what
 * `parseArgs` reads past.
 *
 * Exported for its own test; the commands are what a developer runs.
 */
export function positionals(argv) {
  const words = [];
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg.startsWith('--')) {
      const next = argv[i + 1];
      if (next !== undefined && !next.startsWith('--')) {
        i += 1;
      }
      continue;
    }
    words.push(arg);
  }
  return words;
}

/** `--name value` and `--flag` out of the command line, with no dependency to do it. */
export function parseArgs(argv) {
  const options = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith('--')) {
      continue;
    }
    const name = arg.slice(2);
    const next = argv[i + 1];
    if (next === undefined || next.startsWith('--')) {
      options[name] = true;
    } else {
      options[name] = next;
      i += 1;
    }
  }
  return options;
}

/**
 * The connection string the API is started with. Exported for its own test, because getting
 * this wrong is the failure the whole script is arranged to prevent: the development settings
 * file names port 5432, which is the database somebody else is using.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function connectionStringFor(port) {
  return `Host=127.0.0.1;Port=${port};Database=silexgis;Username=silexgis;Password=silexgis`;
}

/** Exported for its own test; `up` is what a developer runs. */
export function baseUrlFor(port) {
  return `http://127.0.0.1:${port}`;
}

/**
 * A PKCE verifier and its S256 challenge. base64url throughout: the challenge is the base64url
 * encoding of the SHA-256 of the verifier's ASCII bytes, unpadded.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function pkcePair() {
  const verifier = randomBytes(48).toString('base64url');
  const challenge = createHash('sha256').update(verifier, 'ascii').digest('base64url');
  return { verifier, challenge };
}

/**
 * The authorization URL for one sign-in attempt.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function authorizeUrl(baseUrl, challenge, state) {
  const query = new URLSearchParams({
    client_id: CLIENT_ID,
    redirect_uri: REDIRECT_URI,
    response_type: 'code',
    scope: SCOPE,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    state,
  });
  return `${baseUrl}/connect/authorize?${query}`;
}

/**
 * What `up` prints at the end: the whole sign-in dance as commands somebody can paste. Written
 * as a function of the base URL so the printed transcript can never name a port the script did
 * not actually serve on.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function tokenDanceCurl(baseUrl) {
  return [
    '# 1. Password login. The authorization endpoint consumes a session cookie and this is the',
    '#    only thing that produces one.',
    `curl -sS -c cookies.txt -X POST ${baseUrl}/api/v1/auth/login \\`,
    "  -H 'Content-Type: application/json' \\",
    `  -d '{"email":"${MEMBER_EMAIL}","password":"${MEMBER_PASSWORD}"}'`,
    '',
    '# 2. PKCE. Keep the verifier; step 3 needs it.',
    "VERIFIER=$(openssl rand -base64 48 | tr '+/' '-_' | tr -d '=')",
    "CHALLENGE=$(printf %s \"$VERIFIER\" | openssl dgst -binary -sha256 | base64 | tr '+/' '-_' | tr -d '=')",
    '',
    '# 3. The authorization request. Do NOT follow the redirect: the code is in the Location',
    '#    header and the response has no body.',
    `curl -sS -i -b cookies.txt "${baseUrl}/connect/authorize\\`,
    `?client_id=${CLIENT_ID}\\`,
    `&redirect_uri=${encodeURIComponent(REDIRECT_URI)}\\`,
    '&response_type=code\\',
    `&scope=${encodeURIComponent(SCOPE)}\\`,
    '&code_challenge=$CHALLENGE\\',
    '&code_challenge_method=S256\\',
    '&state=dev" | grep -i ^location:',
    '',
    '# 4. The code exchange. A form body, not JSON, and no client secret. redirect_uri must be',
    '#    byte-identical to the one in step 3, port included.',
    `curl -sS -X POST ${baseUrl}/connect/token \\`,
    "  -H 'Content-Type: application/x-www-form-urlencoded' \\",
    '  -d grant_type=authorization_code \\',
    '  -d code=PASTE_THE_CODE_FROM_STEP_3 \\',
    `  -d 'redirect_uri=${REDIRECT_URI}' \\`,
    `  -d client_id=${CLIENT_ID} \\`,
    '  -d code_verifier="$VERIFIER"',
    '',
    '# 5. Any sync route, with the access token.',
    `curl -sS ${baseUrl}/api/v1/sync/capabilities -H "Authorization: Bearer $ACCESS_TOKEN"`,
  ].join('\n');
}

/**
 * True when the answer is the one this server is supposed to give. Kept apart from the request
 * so a test can exercise the judgement without a server: a check that only asked whether
 * something answered would pass against any HTTP server at all.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function capabilitiesProblem(body) {
  if (body === null || typeof body !== 'object') {
    return 'the capabilities answer was not a JSON object';
  }
  if (body.contractVersion !== 1) {
    return `contractVersion was ${JSON.stringify(body.contractVersion)}, expected 1`;
  }
  if (!(body.pageSizeMax > 0) || !(body.uploadRowsMax > 0)) {
    return 'pageSizeMax and uploadRowsMax must both be positive';
  }
  if (!Array.isArray(body.features)
    || !body.features.includes('download')
    || !body.features.includes('upload')) {
    return `features must list download and upload, got ${JSON.stringify(body.features)}`;
  }
  return null;
}

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

/** Runs a command to completion, echoing it first, and fails the script if it does not. */
function run(command, args, options = {}) {
  console.log(`\n$ ${command} ${args.join(' ')}`);
  const result = spawnSync(command, args, { stdio: 'inherit', ...options });
  if (result.error) {
    fail(`Could not run ${command}: ${result.error.message}`);
  }
  if (result.status !== 0) {
    fail(`${command} ${args[0]} exited with ${result.status}.`);
  }
}

/** Runs a command and returns what it wrote, without echoing it. */
function capture(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: 'utf8', ...options });
  return `${result.stdout ?? ''}${result.stderr ?? ''}`;
}

/** Waits for a TCP listener. PostgreSQL is not HTTP, so this is the only portable probe. */
async function waitForPort(port, { timeoutMs = 120_000, label } = {}) {
  const deadline = Date.now() + timeoutMs;
  process.stdout.write(`Waiting for ${label ?? `127.0.0.1:${port}`} `);
  for (;;) {
    const open = await new Promise((done) => {
      const socket = connect(port, '127.0.0.1');
      socket.setTimeout(2000);
      socket.on('connect', () => { socket.destroy(); done(true); });
      socket.on('timeout', () => { socket.destroy(); done(false); });
      socket.on('error', () => { socket.destroy(); done(false); });
    });
    if (open) {
      console.log(' ok');
      return;
    }
    if (Date.now() > deadline) {
      console.log(' timeout');
      fail(`${label ?? `127.0.0.1:${port}`} did not accept connections within ${timeoutMs / 1000}s.`);
    }
    process.stdout.write('.');
    await sleep(1000);
  }
}

/**
 * Whether a child started for the API should be given a process group of its own.
 *
 * POSIX only. Windows has no process groups, so there `detached` buys nothing the teardown can
 * use and only lets the server outlive the script that started it.
 */
export const detachApi = (platform = process.platform) => platform !== 'win32';

/**
 * Why the wait below must not accept an answer, or null while the API is still alive.
 *
 * The failure it names is the quiet one: a previous run whose API outlived it still holds the
 * port, the one just started dies on the bind, and the old server answers every probe here
 * exactly as a healthy new one would — including from a database that has since been reset.
 */
export function apiExitProblem(api, baseUrl) {
  if (api.exitCode === null && api.signalCode === null) {
    return null;
  }

  const how = api.exitCode === null ? `on ${api.signalCode}` : `with code ${api.exitCode}`;
  return `The API exited ${how} before answering. If it could not bind its port, something `
    + `else is already listening on ${baseUrl} — stop it, or run \`down\`, and try again.`;
}

/**
 * Stops the server and everything it launched.
 *
 * The two arms are the same intent on platforms that express it differently, and the callers of
 * the injected pair are what its test drives: a Windows box has no process groups, so signalling
 * a negative pid there is rejected rather than merely ineffective.
 */
export function stopApiTree(api, {
  platform = process.platform,
  signalGroup = (pid) => process.kill(-pid, 'SIGINT'),
  killTree = (pid) => spawnSync('taskkill', ['/pid', String(pid), '/T', '/F'], { stdio: 'ignore' }),
} = {}) {
  try {
    if (detachApi(platform)) {
      signalGroup(api.pid);
    } else {
      killTree(api.pid);
    }
  } catch {
    // Already gone, or never started. Either way there is nothing left to stop.
  }
}

/**
 * Waits for the API. It waits for a *sync* route rather than for the health endpoint, and it
 * expects 401: an unauthenticated sync route answering 401 proves the routing, the
 * authentication middleware and the database connection are all up, whereas a health endpoint
 * can answer while the sync slice is broken.
 */
async function waitForApi(baseUrl, { timeoutMs = 180_000, api = null } = {}) {
  const deadline = Date.now() + timeoutMs;
  process.stdout.write(`Waiting for ${baseUrl}/api/v1/sync/capabilities `);
  for (;;) {
    // Asked before the fetch, so a server this script did not start can never be mistaken for
    // the one it did. The ordinary way that happens is a previous run whose API outlived it and
    // still holds the port: the new one dies on the bind, the old one answers 401, and every
    // check below passes against a server whose database may since have been thrown away.
    const exited = api === null ? null : apiExitProblem(api, baseUrl);
    if (exited !== null) {
      console.log(' failed');
      fail(exited,
        'Look at the output above: a migration or a seeding step usually fails loudly first.');
    }
    try {
      const res = await fetch(`${baseUrl}/api/v1/sync/capabilities`, { redirect: 'manual' });
      if (res.status === 401) {
        console.log(' ok (401 unauthenticated, as it should be)');
        return;
      }
      if (res.status === 200) {
        fail('The sync capabilities route answered 200 without a token.',
          'That route must be authenticated; something has put it on the anonymous allow-list.');
      }
    } catch {
      // Not listening yet.
    }
    if (Date.now() > deadline) {
      console.log(' timeout');
      fail(`The API did not answer within ${timeoutMs / 1000}s.`,
        'Look at the output above: a migration or a seeding step usually fails loudly first.');
    }
    process.stdout.write('.');
    await sleep(2000);
  }
}

/**
 * The whole sign-in dance, done for real, ending in a sync answer. This is what makes `up`'s
 * success message mean something: it exercises the seeded account, the seeded OAuth client,
 * PKCE, the token exchange and the sync slice in one pass.
 *
 * Throws rather than exiting, so its own test can run it against a server this process did not
 * start. `up` turns the throw into a message and a non-zero exit.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export async function signInAndReadCapabilities(baseUrl, email, password) {
  const tokens = await signIn(baseUrl, email, password);
  return readCapabilities(baseUrl, tokens.access_token);
}

/**
 * The sign-in dance alone: password login for the session cookie, the authorization request with
 * PKCE, and the code exchange. Returns the token answer (`access_token`, `refresh_token`, …).
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export async function signIn(baseUrl, email, password) {
  const login = await fetch(`${baseUrl}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  if (!login.ok) {
    throw new Error(`Signing in as ${email} answered ${login.status}. `
      + 'The development seeding did not create that account.');
  }
  const cookie = (login.headers.getSetCookie?.() ?? [])
    .map((entry) => entry.split(';')[0])
    .join('; ');
  if (!cookie) {
    throw new Error('The login answered 200 but set no session cookie.');
  }

  const { verifier, challenge } = pkcePair();
  const state = randomBytes(8).toString('hex');
  const authorize = await fetch(authorizeUrl(baseUrl, challenge, state), {
    headers: { Cookie: cookie },
    redirect: 'manual',
  });
  const location = authorize.headers.get('location');
  if (authorize.status !== 302 || !location) {
    throw new Error(`The authorization request answered ${authorize.status} with no Location header.`);
  }
  const returned = new URL(location);
  if (returned.searchParams.get('state') !== state) {
    throw new Error('The authorization response carried a different state than was sent.');
  }
  const code = returned.searchParams.get('code');
  if (!code) {
    throw new Error(`The authorization redirect carried no code: ${location}`);
  }

  const token = await fetch(`${baseUrl}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      code,
      redirect_uri: REDIRECT_URI,
      client_id: CLIENT_ID,
      code_verifier: verifier,
    }),
  });
  const tokens = await token.json().catch(() => null);
  if (!token.ok || !tokens?.access_token) {
    throw new Error(`The code exchange answered ${token.status}: ${JSON.stringify(tokens)}`);
  }
  if (!tokens.refresh_token) {
    throw new Error('The code exchange returned no refresh token. offline_access must be in '
      + 'the requested scope, or a device re-enters a password every fifteen minutes.');
  }
  return tokens;
}

/** Reads and judges the capabilities answer with a token `signIn` returned. */
export async function readCapabilities(baseUrl, accessToken) {
  const capabilities = await fetch(`${baseUrl}/api/v1/sync/capabilities`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  if (capabilities.status !== 200) {
    throw new Error(`The sync capabilities route answered ${capabilities.status} to a signed-in caller.`);
  }
  const body = await capabilities.json();
  const problem = capabilitiesProblem(body);
  if (problem) {
    throw new Error(`The sync capabilities answer is not the expected one: ${problem}`);
  }
  return body;
}

/**
 * Null when the documents stream hands the member both seeded photos the way the protection rule
 * says it must: the one on the open cave alone as stored, the one that also hangs on the protected
 * cave without its location. A photo missing altogether is a problem too — a check that only
 * looked for `locationFree` would pass against a seeder that wrote one photo and not the other.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function photoReachProblem(documentsPage) {
  if (documentsPage === null || typeof documentsPage !== 'object' || !Array.isArray(documentsPage.links)) {
    return 'the documents stream answer carried no links list';
  }
  const reachOf = (title) => [...new Set(documentsPage.links
    .map((link) => link?.document)
    .filter((document) => document?.title === title)
    .map((document) => document.reach))];
  const expected = [[OPEN_PHOTO_TITLE, 'original'], [SHARED_PHOTO_TITLE, 'locationFree']];
  for (const [title, reach] of expected) {
    const seen = reachOf(title);
    if (seen.length === 0) {
      return `the photo "${title}" was not in the documents stream; the seeding did not write it`;
    }
    if (seen.length !== 1 || seen[0] !== reach) {
      return `the photo "${title}" was announced with reach ${JSON.stringify(seen)}, expected "${reach}"`;
    }
  }
  return null;
}

/**
 * The identifier of the open place as the member's download hands it, or null when the download
 * does not hand it — which is itself a problem, because the pin check below then has nothing to
 * name the pin it expects with.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function openPlaceIdIn(downloadPage) {
  const features = Array.isArray(downloadPage?.features) ? downloadPage.features : [];
  const matches = features.filter((feature) => feature?.name === OPEN_PLACE_NAME);
  return matches.length === 1 && typeof matches[0].id === 'string' ? matches[0].id : null;
}

/**
 * Null when the rasters stream hands the member the seeded map with exactly one of its two pins,
 * and that one is the pin on the open place. The other is on a place that is its own protection
 * root, so it is withheld from a reader who may not see that place exactly.
 *
 * The pin is named, not counted: a filter keyed on the wrong place would hand the member the
 * protected pin instead of the open one, and a count of one would read as a working refusal. So
 * without the open place's identifier there is nothing to compare against, and that is refused
 * rather than accepted.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function withheldPinProblem(rastersPage, openPlaceId) {
  if (typeof openPlaceId !== 'string' || openPlaceId === '') {
    return `the member's download did not hand over the place "${OPEN_PLACE_NAME}", `
      + 'so the pin the member should be given cannot be named';
  }
  if (rastersPage === null || typeof rastersPage !== 'object' || !Array.isArray(rastersPage.maps)) {
    return 'the rasters stream answer carried no maps list';
  }
  const map = rastersPage.maps.find((candidate) => candidate?.title === RASTER_TITLE);
  if (!map) {
    return `the raster map "${RASTER_TITLE}" was not in the rasters stream; the seeding did not write it`;
  }
  const pins = Array.isArray(map.pins) ? map.pins : [];
  if (pins.length !== 1) {
    return `the raster map "${RASTER_TITLE}" arrived with ${pins.length} pins, expected 1 `
      + '(two are seeded; the one on the protected place must be withheld from this account)';
  }
  if (pins[0]?.placeId !== openPlaceId) {
    return `the raster map "${RASTER_TITLE}" handed the member a pin on a place other than `
      + `"${OPEN_PLACE_NAME}" — the pin on the protected place reached it and the open one did not`;
  }
  return null;
}

/**
 * Proves, as the member, that the phone-side data is there and protected: a sync set rooted at
 * the open cave, its documents stream judged by `photoReachProblem`, its rasters stream by
 * `withheldPinProblem` against the open place's identifier read from its own download. The set is deleted afterwards, so running `up` again leaves no pile of
 * sets behind in the member's list. Throws with the reason, like the sign-in does.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export async function proveSyncContent(baseUrl, accessToken) {
  const headers = { Authorization: `Bearer ${accessToken}` };
  const getJson = async (path) => {
    const response = await fetch(`${baseUrl}${path}`, { headers });
    if (response.status !== 200) {
      throw new Error(`GET ${path} answered ${response.status} to the member.`);
    }
    return response.json();
  };

  const caves = await getJson('/api/v1/caves?pageSize=200');
  const openCave = (caves.items ?? []).find((cave) => cave.identificationCode === OPEN_CAVE_CODE);
  if (!openCave) {
    throw new Error(`The member could not find the demo cave ${OPEN_CAVE_CODE}; seed-demo did not run.`);
  }

  const created = await fetch(`${baseUrl}/api/v1/sync/sets`, {
    method: 'POST',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: `speleoloc-dev check ${randomBytes(3).toString('hex')}`,
      uploadVisibility: 'private',
      rootFeatureIds: [openCave.id],
      settings: {},
    }),
  });
  const set = await created.json().catch(() => null);
  if (created.status !== 201 || !set?.id) {
    throw new Error(`Creating a sync set as the member answered ${created.status}: ${JSON.stringify(set)}`);
  }
  try {
    const photoProblem = photoReachProblem(await getJson(`/api/v1/sync/sets/${set.id}/documents`));
    if (photoProblem) {
      throw new Error(`The seeded photos are not protected as expected: ${photoProblem}`);
    }
    // The whole download, page by page: the open place may sit on any page of it.
    const features = [];
    let cursor = null;
    for (let page = 0; page < 100; page += 1) {
      const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`;
      const body = await getJson(`/api/v1/sync/sets/${set.id}/download${query}`);
      features.push(...(Array.isArray(body.features) ? body.features : []));
      cursor = body.nextCursor ?? null;
      if (!body.hasMore || cursor === null) {
        break;
      }
    }
    const openPlaceId = openPlaceIdIn({ features });
    const pinProblem = withheldPinProblem(await getJson(`/api/v1/sync/sets/${set.id}/rasters`), openPlaceId);
    if (pinProblem) {
      throw new Error(`The seeded raster map is not protected as expected: ${pinProblem}`);
    }
  } finally {
    await fetch(`${baseUrl}/api/v1/sync/sets/${set.id}`, { method: 'DELETE', headers }).catch(() => {});
  }
}

/**
 * A review route that refused, with the status and the problem's stable `code` — what the
 * `review` command prints and exits on. A refusal is an answer, not a fault: an id that is not
 * waiting, a row changed since it was sent, or an account deciding its own uploads.
 */
export class ReviewRefusal extends Error {
  constructor(status, code, detail) {
    super(`refused: ${status} ${code ?? '(no code)'}${detail ? ` — ${detail}` : ''}`);
    this.status = status;
    this.code = code ?? null;
  }
}

/** Reads a review route's answer, or throws the refusal it is. */
async function reviewAnswer(response) {
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    throw new ReviewRefusal(response.status, body?.code, body?.detail ?? body?.title);
  }
  return body;
}

/**
 * The rows waiting for a reviewer, oldest batch first: every page of the queue, and each
 * batch's lines that are still waiting. Asked as the administrator, through the routes the
 * browser's review queue reads.
 *
 * Exported for its own test; `review list` is what a developer runs.
 */
export async function reviewWaiting(baseUrl, accessToken) {
  const headers = { Authorization: `Bearer ${accessToken}` };
  const waiting = [];
  for (let page = 1; page <= 100; page += 1) {
    const queue = await reviewAnswer(await fetch(
      `${baseUrl}/api/v1/review/batches?page=${page}&pageSize=100`, { headers }));
    const batches = Array.isArray(queue?.items) ? queue.items : [];
    for (const batch of batches) {
      const detail = await reviewAnswer(await fetch(`${baseUrl}/api/v1/review/batches/${batch.id}`, { headers }));
      for (const line of Array.isArray(detail?.lines) ? detail.lines : []) {
        if (line?.decision === 'pending') {
          waiting.push({ reviewId: line.reviewId, entity: line.section ?? null, rowId: line.rowId ?? null });
        }
      }
    }
    if (batches.length === 0 || page * (queue.pageSize ?? 100) >= (queue.totalItems ?? 0)) {
      break;
    }
  }
  return waiting;
}

/**
 * Accepts or rejects one waiting row, with what it depends on (accepting) or what depends on it
 * (rejecting), and returns every row the decision decided.
 *
 * Exported for its own test; `review accept` and `review reject` are what a developer runs.
 */
export async function reviewDecide(baseUrl, accessToken, reviewId, accept, note) {
  const verb = accept ? 'accept' : 'reject';
  const body = typeof note === 'string' && note !== '' ? { note } : {};
  const answer = await reviewAnswer(await fetch(
    `${baseUrl}/api/v1/review/items/${encodeURIComponent(reviewId)}/${verb}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }));
  return (Array.isArray(answer?.decided) ? answer.decided : []).map((line) => ({
    reviewId: line.reviewId,
    outcome: line.decision,
    code: line.failureCode ?? null,
  }));
}

/** One printed line of `review list`. Exported for its own test. */
export function waitingLine({ reviewId, entity, rowId }) {
  return `${reviewId}  ${entity ?? '-'}  ${rowId ?? '-'}`;
}

/** One printed line of a decision. Exported for its own test. */
export function decidedLine({ reviewId, outcome, code }) {
  return code ? `${reviewId}  ${outcome}  ${code}` : `${reviewId}  ${outcome}`;
}

const REVIEW_USAGE = 'usage: node deploy/speleoloc-dev.mjs review list | accept <reviewId> | reject <reviewId> [--note "…"]';

async function reviewCommand(options, words) {
  const [action, reviewId] = words;
  if (!['list', 'accept', 'reject'].includes(action) || (action !== 'list' && !reviewId)) {
    fail(REVIEW_USAGE);
  }
  const baseUrl = baseUrlFor(portsFrom(options).apiPort);
  try {
    const tokens = await signIn(baseUrl, ADMIN_EMAIL, ADMIN_PASSWORD);
    if (action === 'list') {
      for (const line of await reviewWaiting(baseUrl, tokens.access_token)) {
        console.log(waitingLine(line));
      }
      return;
    }
    const note = typeof options.note === 'string' ? options.note : undefined;
    for (const line of await reviewDecide(baseUrl, tokens.access_token, reviewId, action === 'accept', note)) {
      console.log(decidedLine(line));
    }
  } catch (error) {
    if (error instanceof ReviewRefusal) {
      console.error(error.message);
      process.exit(1);
    }
    fail(error.message, `Is the server up on ${baseUrl}? \`node deploy/speleoloc-dev.mjs up\` serves it.`);
  }
}

/**
 * True when a container of this stack's name is one this stack may use.
 *
 * The judgement, not the docker call, because the judgement is the part that was wrong: it is
 * stated as an allow-list rather than as a list of the shapes to refuse. `docker inspect` prints
 * the empty string when no such container exists, the project name when compose made it,
 * `<no value>` when a plain `docker run` made it, and an `Error…` (or, from Docker 29, `error…`)
 * line when there is no such container or the daemon could not be asked. Only the first three of those are known-good; every other label is somebody else's
 * project and is exactly the case this guard exists for, so it must fall on the refusing side.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function foreignContainerProblem(label) {
  const value = label.trim();
  // Docker before 29 wrote `Error: No such object: …`; Docker 29 writes `error: no such object: …`.
  if (value === '' || value === COMPOSE_PROJECT || /^error\b/i.test(value)) {
    return null;
  }
  return value === '<no value>'
    ? 'it was started by a plain `docker run`, not by any compose project'
    : `it belongs to the compose project '${value}', not to ${COMPOSE_PROJECT}`;
}

/** Fails with the removal command when a container of this name was not started by this stack. */
function refuseForeignContainer() {
  const problem = foreignContainerProblem(capture('docker', [
    'inspect', DB_CONTAINER,
    '--format', '{{index .Config.Labels "com.docker.compose.project"}}',
  ]));
  if (problem) {
    fail(`A container named ${DB_CONTAINER} exists but was not started by this stack: ${problem}. `
      + 'Its data is not on the volume this script manages.',
      `Remove it first:  docker rm -f ${DB_CONTAINER}`);
  }
}

/**
 * The environment `docker compose` is given. The ports are the load-bearing entries and they are
 * not decoration: the compose file reads both from the environment with the defaults this script
 * also holds, so a `--db-port` that never reached compose would publish the database on the
 * default port while everything downstream — the connection string, the readiness probe, the
 * printed transcript — named the port that was asked for. The symptom is a healthy database and a
 * probe that times out against it, which reads as a Docker fault rather than as a dropped flag.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function composeEnvironment(dbPort, apiPort, base = process.env) {
  return {
    ...base,
    SILEXGIS_SPELEOLOC_DB_PORT: String(dbPort),
    SILEXGIS_SPELEOLOC_API_PORT: String(apiPort),
  };
}

function compose(ports, ...args) {
  run('docker', ['compose', '-f', COMPOSE_FILE, ...args], {
    cwd: deployDir,
    env: composeEnvironment(ports.dbPort, ports.apiPort),
  });
}

/**
 * The arguments every `dotnet run` here gets.
 *
 * `--no-launch-profile` is load-bearing and not tidiness. The project's launch settings name a
 * fixed address, and a launch profile's `applicationUrl` overrides the ASPNETCORE_URLS given in
 * the environment — so without this the server binds the ordinary development port instead of
 * the one this script published, prints a healthy startup banner, and nothing on the port this
 * script then probes ever answers.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function dotnetRun(project = apiProject) {
  return ['run', '--project', project, '--no-build', '--no-restore', '--no-launch-profile'];
}

/** Environment every dotnet invocation gets. The connection string is the load-bearing entry. */
function apiEnvironment(dbPort, apiPort) {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: baseUrlFor(apiPort),
    SILEXGIS__Db__ConnectionString: connectionStringFor(dbPort),
    SILEXGIS__PublicUrl: baseUrlFor(apiPort),
    SILEXGIS__Admin__Email: ADMIN_EMAIL,
    SILEXGIS__Admin__Password: ADMIN_PASSWORD,
    SILEXGIS__SpeleoLocDev__MemberPassword: MEMBER_PASSWORD,
    SILEXGIS__Files__Root: join(STATE_DIR, 'files'),
    SILEXGIS__Keys__Path: join(STATE_DIR, 'keys'),
  };
}

function printSummary(baseUrl, capabilities) {
  console.log('\n--- a SilexGIS server for the SpeleoLoc sync API ----------------------------');
  console.log(`Base URL          ${baseUrl}`);
  console.log(`Contract version  ${capabilities.contractVersion}`
    + `   (page ${capabilities.pageSizeMax}, upload ${capabilities.uploadRowsMax},`
    + ` features: ${capabilities.features.join(', ')})`);
  console.log(`OpenAPI           ${baseUrl}/openapi/v1.json`);
  console.log('');
  console.log(`Member account    ${MEMBER_EMAIL} / ${MEMBER_PASSWORD}`);
  console.log('                  Sign in as this one. It owns nothing, so what it can see is');
  console.log('                  decided by the permission rules rather than by ownership.');
  console.log(`Admin account     ${ADMIN_EMAIL} / ${ADMIN_PASSWORD}`);
  console.log('                  A full administrator: it sees every coordinate exactly, so it');
  console.log('                  demonstrates none of the protection behaviour.');
  console.log(`Caving group      ${GROUP_NAME}   (both accounts are members)`);
  console.log(`Group-only cave   ${GROUP_VISIBLE_CAVE_CODE}  — visible only through that group`);
  console.log(`Protected cave    ${PROTECTED_CAVE_CODE}  — its exact position is withheld from`);
  console.log('                  the member account on the sync channel');
  console.log(`Review account    ${REVIEW_EMAIL} / ${MEMBER_PASSWORD}`);
  console.log('                  A second plain member whose uploads wait for a reviewer: send');
  console.log('                  clientFeatures ["held"] or the upload is refused whole.');
  console.log('');
  console.log(`On ${OPEN_CAVE_CODE}         two photos with an invented GPS tag — "${OPEN_PHOTO_TITLE}"`);
  console.log('                  reaches the member as stored, the one that also hangs on');
  console.log(`                  ${PROTECTED_CAVE_CODE} without its location; a raster map "${RASTER_TITLE}" with`);
  console.log('                  two pins, one withheld from the member; a trip recording');
  console.log(`                  "${RECORDING_TITLE}"`);
  console.log(`People directory  on — lists "${ROSTER_ONLY_CAVER}" (no account) and links one phone`);
  console.log('                  user the member\'s phone reported to that person');
  console.log('');
  console.log(`Client id         ${CLIENT_ID}   (public, no secret, PKCE S256 required)`);
  console.log(`Redirect URI      ${REDIRECT_URI}   (or speleoloc://auth)`);
  console.log('');
  console.log('--- the sign-in dance, as commands ------------------------------------------');
  console.log(tokenDanceCurl(baseUrl));
  console.log('\n-----------------------------------------------------------------------------');
  console.log('Ctrl-C stops the API. The database keeps running; `down` stops it and `reset`');
  console.log('throws its volume away.');
}

async function upCommand(options) {
  const { dbPort, apiPort } = portsFrom(options);
  const baseUrl = baseUrlFor(apiPort);
  const environment = apiEnvironment(dbPort, apiPort);

  refuseForeignContainer();
  compose({ dbPort, apiPort }, 'up', '-d', 'db');
  await waitForPort(dbPort, { label: `the database on 127.0.0.1:${dbPort}` });

  // The API project, not the whole solution. This script only ever runs the server, so building
  // the test projects too costs time and — the reason it is written this way — rewrites test
  // assemblies that another suite on the same machine may be executing from.
  run('dotnet', ['build', apiProject, '-v', 'quiet', '--nologo'], { cwd: serverDir });

  // Three sequential runs, not one: both seeding commands migrate, seed and then exit, so the
  // serving instance is a third invocation. Their exit code says nothing — each returns zero
  // after logging its refusal — which is why nothing here is trusted until the sign-in below.
  run('dotnet', [...dotnetRun(), '--', 'seed-demo'], { cwd: serverDir, env: environment });
  run('dotnet', [...dotnetRun(), '--', 'seed-speleoloc-dev'], { cwd: serverDir, env: environment });

  console.log(`\n$ dotnet run --project ${apiProject} (serving on ${baseUrl})`);

  // The whole tree has to be stopped, not just the launcher: `dotnet run` is a launcher, the
  // server is a separate child of it, and a signal delivered only to the launcher leaves that
  // child running. The symptom is an API still holding this port after Ctrl-C, with its
  // database container already stopped — a server that answers every request with a failure and
  // looks, to the next person who runs this script, like a port conflict.
  //
  // How the tree is reached differs by platform, and the difference is not cosmetic. On POSIX
  // the child gets a process group of its own and the group is signalled. Windows has no
  // process groups: a negative pid is rejected there, and `detached` would only let the child
  // outlive this process — so the tree is stopped with `taskkill /T`, and `detached` is not
  // asked for.
  const api = spawn('dotnet', dotnetRun(), {
    cwd: serverDir,
    env: environment,
    stdio: 'inherit',
    detached: detachApi(),
  });
  const stop = () => stopApiTree(api);
  process.on('SIGINT', () => { stop(); process.exit(0); });
  process.on('exit', stop);

  await waitForApi(baseUrl, { api });
  const seedingHint = 'Both seeding commands exit zero when they refuse, so the reason is a line '
    + 'in the output above rather than a failed command.';
  const tokens = await signIn(baseUrl, MEMBER_EMAIL, MEMBER_PASSWORD)
    .catch((error) => fail(error.message, seedingHint));
  const capabilities = await readCapabilities(baseUrl, tokens.access_token)
    .catch((error) => fail(error.message, seedingHint));
  console.log(`\n✅ Signed in as ${MEMBER_EMAIL} and read the sync capabilities.`);
  await proveSyncContent(baseUrl, tokens.access_token)
    .catch((error) => fail(error.message, seedingHint));
  console.log(`✅ As ${MEMBER_EMAIL}: one photo handed over without its location, one raster pin withheld.`);
  printSummary(baseUrl, capabilities);

  await new Promise((done) => api.on('exit', done));
}

/** The ports a command was given, defaulted. Shared so `down` and `reset` read them the same way. */
function portsFrom(options) {
  return {
    dbPort: Number(options['db-port'] ?? DEFAULT_DB_PORT),
    apiPort: Number(options['api-port'] ?? DEFAULT_API_PORT),
  };
}

function downCommand(options) {
  compose(portsFrom(options), 'stop', 'db');
  console.log('\nThe database is stopped and its volume is intact. `up` brings it back as it was.');
}

function resetCommand(options) {
  compose(portsFrom(options), 'down', '-v');
  rmSync(STATE_DIR, { recursive: true, force: true });
  console.log('\nContainer, volume, stored files and keys removed. The next `up` starts from an '
    + 'empty installation.');
}

// Only when run, not when imported, so the test file can exercise the helpers above.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [command, ...rest] = process.argv.slice(2);
  const options = parseArgs(rest);
  switch (command) {
    case 'up': await upCommand(options); break;
    case 'down': downCommand(options); break;
    case 'reset': resetCommand(options); break;
    case 'review': await reviewCommand(options, positionals(rest)); break;
    default:
      console.log('A throwaway SilexGIS server for the SpeleoLoc sync API.\n');
      console.log('  node deploy/speleoloc-dev.mjs up      start it and print how to sign in');
      console.log('  node deploy/speleoloc-dev.mjs down    stop it, keeping the data');
      console.log('  node deploy/speleoloc-dev.mjs reset   throw the database away');
      console.log('  node deploy/speleoloc-dev.mjs review list | accept <reviewId> | reject <reviewId> [--note "…"]');
      console.log('                                        decide review@dev.local\'s waiting rows as the administrator\n');
      console.log(`  --db-port N    default ${DEFAULT_DB_PORT}`);
      console.log(`  --api-port N   default ${DEFAULT_API_PORT}`);
      process.exit(command ? 1 : 0);
  }
}
