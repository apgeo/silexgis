// SPDX-License-Identifier: AGPL-3.0-or-later
//
// A throwaway SilexGIS installation for writing a mobile client against the sync API.
//
// Cross-platform (Node 18+, no shell-isms). Three commands:
//
//   node deploy/speleoloc-dev.mjs up
//       Starts a dedicated PostGIS container on a loopback port with its own named volume,
//       runs the schema migration and the always-on seeding, loads the demonstration dataset,
//       adds the sync-specific development data, serves the API from source on a loopback
//       port, proves it answers by signing in and reading the sync capabilities, then prints
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
//       Removes the container and its volume, so the next `up` starts from an empty database.
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
import { connect } from 'node:net';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const deployDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(deployDir, '..');
const serverDir = join(repoRoot, 'server');
const apiProject = join(serverDir, 'src', 'SilexGis.Api');

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

  const capabilities = await fetch(`${baseUrl}/api/v1/sync/capabilities`, {
    headers: { Authorization: `Bearer ${tokens.access_token}` },
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
 * True when a container of this stack's name is one this stack may use.
 *
 * The judgement, not the docker call, because the judgement is the part that was wrong: it is
 * stated as an allow-list rather than as a list of the shapes to refuse. `docker inspect` prints
 * the empty string when no such container exists, the project name when compose made it,
 * `<no value>` when a plain `docker run` made it, and an `Error…` line when the daemon could not
 * be asked. Only the first three of those are known-good; every other label is somebody else's
 * project and is exactly the case this guard exists for, so it must fall on the refusing side.
 *
 * Exported for its own test; `up` is what a developer runs.
 */
export function foreignContainerProblem(label) {
  const value = label.trim();
  if (value === '' || value === COMPOSE_PROJECT || value.startsWith('Error')) {
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
  const capabilities = await signInAndReadCapabilities(baseUrl, MEMBER_EMAIL, MEMBER_PASSWORD)
    .catch((error) => fail(error.message,
      'Both seeding commands exit zero when they refuse, so the reason is a line in the output '
      + 'above rather than a failed command.'));
  console.log(`\n✅ Signed in as ${MEMBER_EMAIL} and read the sync capabilities.`);
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
  console.log('\nContainer and volume removed. The next `up` starts from an empty database.');
}

// Only when run, not when imported, so the test file can exercise the helpers above.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [command, ...rest] = process.argv.slice(2);
  const options = parseArgs(rest);
  switch (command) {
    case 'up': await upCommand(options); break;
    case 'down': downCommand(options); break;
    case 'reset': resetCommand(options); break;
    default:
      console.log('A throwaway SilexGIS server for the SpeleoLoc sync API.\n');
      console.log('  node deploy/speleoloc-dev.mjs up      start it and print how to sign in');
      console.log('  node deploy/speleoloc-dev.mjs down    stop it, keeping the data');
      console.log('  node deploy/speleoloc-dev.mjs reset   throw the database away\n');
      console.log(`  --db-port N    default ${DEFAULT_DB_PORT}`);
      console.log(`  --api-port N   default ${DEFAULT_API_PORT}`);
      process.exit(command ? 1 : 0);
  }
}
