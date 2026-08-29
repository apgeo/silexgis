// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Stand up everything the browser leg needs, run it, and take it down again.
//
//   node scripts/e2e.mjs                     # the whole suite
//   node scripts/e2e.mjs smoke trips         # only these spec files
//   node scripts/e2e.mjs --keep              # leave the stack up afterwards
//   node scripts/e2e.mjs --project=desktop   # one Playwright project
//
// Why this exists. Every piece of the browser leg was already here — the browsers are
// installed, the specs are written, the config knows how to start Vite — and it was still
// skipped batch after batch, always for the same reason: standing up a database, a migrated
// API on the right port with the right non-default settings, and a seeded demo dataset is
// four commands nobody has written down, and getting one of them subtly wrong produces a
// suite that fails for reasons that have nothing to do with the change under test. So the
// four commands are written down here, once.
//
// It runs on its own ports and its own database container by default, so it cannot collide
// with a dev stack somebody is using, and cannot be the run that quietly migrates the shared
// dev database. Set SILEXGIS_E2E_REUSE=1 to point it at an already-running stack instead.
//
// Cross-platform: no shell-isms, paths joined rather than concatenated, and every child
// process spawned with an argument array rather than a command line.
import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import process from 'node:process';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const clientDir = join(repoRoot, 'client');
const apiDir = join(repoRoot, 'server', 'src', 'SilexGis.Api');

// Deliberately not 5432/5080/5173. Those belong to whatever the developer is already running,
// and the failure mode of borrowing them is not an error — it is a suite that passes or fails
// against somebody else's data.
const DB_PORT = process.env.SILEXGIS_E2E_DB_PORT ?? '5447';
const API_PORT = process.env.SILEXGIS_E2E_API_PORT ?? '5081';
const DEV_PORT = process.env.SILEXGIS_E2E_DEV_PORT ?? '5174';
const DB_NAME = 'silexgis-e2e-db';

const ADMIN_EMAIL = 'admin@dev.local';
const ADMIN_PASSWORD = 'dev-admin-pass-1';

// Where the API's process id is left between runs.
//
// This exists because the first thing this script did in practice was leave an API behind: killed
// mid-run, the `finally` never ran, and the *next* run found something already answering on its
// port and reported "API: ready after 0s" — serving the whole suite from the previous run's
// process. That is the failure this project has been bitten by before, and it is worse than a
// crash: the run is green, and green about the wrong build.
const PID_FILE = join(repoRoot, 'client', 'node_modules', '.cache', 'silexgis-e2e-api.pid');

/** Kill an API this script left behind, so a stale one can never quietly serve the run. */
function killPreviousApi() {
  if (!existsSync(PID_FILE)) return;
  const pid = Number(readFileSync(PID_FILE, 'utf8').trim());
  if (Number.isInteger(pid) && pid > 0) {
    try {
      process.kill(pid);
      console.log(`Stopped an API left by an earlier run (pid ${pid}).`);
    } catch {
      // Already gone, which is the normal case.
    }
  }
  rmSync(PID_FILE, { force: true });
}

/** True when something is already answering on the API port. */
async function portAnswering() {
  try {
    await fetch(`http://localhost:${API_PORT}/openapi/v1.json`);
    return true;
  } catch {
    return false;
  }
}

const args = process.argv.slice(2);
const keep = args.includes('--keep');
const reuse = process.env.SILEXGIS_E2E_REUSE === '1';
const projectArg = args.find((a) => a.startsWith('--project='));
const specs = args.filter((a) => !a.startsWith('--'));

const apiEnv = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
  Db__ConnectionString:
    `Host=localhost;Port=${DB_PORT};Database=silexgis;Username=silexgis;Password=silexgis`,
  // The bootstrap administrator the specs sign in as. Demo credentials against a throwaway
  // database on loopback; there is nothing here worth protecting and the specs need to know
  // them, so they are the same well-known pair `client/e2e/helpers.ts` names.
  SILEXGIS__Admin__Email: ADMIN_EMAIL,
  SILEXGIS__Admin__Password: ADMIN_PASSWORD,
  // Off by default, and one flow needs it: nothing but self-registration mints the second
  // account that flow requires, and it fails rather than skipping without it.
  SILEXGIS__Auth__OpenRegistration: 'true',
  // The SPA is served from the e2e dev port, so that is where the authorization server has to
  // be willing to send a browser back to. Without this every sign-in dies at the redirect.
  SILEXGIS__Auth__AdditionalRedirectUris__0: `http://localhost:${DEV_PORT}/auth/callback`,
};

function run(cmd, argv, opts = {}) {
  const r = spawnSync(cmd, argv, { stdio: 'inherit', ...opts });
  if (r.error) throw r.error;
  return r.status ?? 1;
}

function step(message) {
  console.log(`\n=== ${message}`);
}

async function waitFor(label, check, timeoutMs = 180_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (await check()) {
      console.log(`${label}: ready after ${Math.round((Date.now() - started) / 1000)}s`);
      return;
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`${label}: not ready within ${timeoutMs / 1000}s`);
}

function teardown() {
  if (keep) {
    console.log(`\nLeft standing (--keep): API :${API_PORT}, database :${DB_PORT} (${DB_NAME}).`);
    console.log(`Remove it with: docker rm -f ${DB_NAME}`);
    return;
  }
  step('Tearing the stack down');
  if (api?.pid) {
    // By the handle this script owns, never by process name: other checkouts on this machine
    // run their own API and their own test suites, and killing "dotnet" would take them too.
    try { process.kill(api.pid); } catch { /* already gone */ }
  }
  rmSync(PID_FILE, { force: true });
  spawnSync('docker', ['rm', '-f', DB_NAME], { stdio: 'ignore' });
}

let api;
let exitCode = 1;
try {
  if (!existsSync(join(clientDir, 'node_modules'))) {
    console.error('client/node_modules is missing — run `npm ci` in client/ first.');
    process.exit(1);
  }

  if (!reuse) {
    killPreviousApi();
    if (await portAnswering()) {
      // Not reused, and not killed either: this is something the script did not start and has no
      // business ending. Refusing is the only honest option — running anyway would test whatever
      // is there and call the result ours.
      console.error(
        `Something is already answering on :${API_PORT} that this script did not start.\n`
        + `Stop it, or set SILEXGIS_E2E_API_PORT to a free port, or SILEXGIS_E2E_REUSE=1 to `
        + `deliberately run against it.`);
      process.exit(1);
    }

    step(`Database on :${DB_PORT}`);
    spawnSync('docker', ['rm', '-f', DB_NAME], { stdio: 'ignore' });
    if (run('docker', [
      'run', '-d', '--name', DB_NAME,
      // Loopback only: a published port bypasses the host firewall's input chain, so a bare
      // mapping would offer this well-known-password database to the whole tailnet.
      '-p', `127.0.0.1:${DB_PORT}:5432`,
      '-e', 'POSTGRES_DB=silexgis',
      '-e', 'POSTGRES_USER=silexgis',
      '-e', 'POSTGRES_PASSWORD=silexgis',
      'postgis/postgis:17-3.5',
    ]) !== 0) throw new Error('could not start the database');

    await waitFor('database', () =>
      spawnSync('docker', ['exec', DB_NAME, 'pg_isready', '-U', 'silexgis'],
        { stdio: 'ignore' }).status === 0, 60_000);

    step('Building the API');
    if (run('dotnet', ['build', join(repoRoot, 'server', 'SilexGis.slnx')]) !== 0) {
      throw new Error('the API did not build');
    }

    // Migrations run at start-up, so seeding is what forces them: this both creates the
    // schema and loads the demo dataset the specs read, and it exits when it is done.
    step('Migrating and seeding the demo dataset');
    if (run('dotnet', ['run', '--no-build', '--no-launch-profile', '--', 'seed-demo'],
      { cwd: apiDir, env: apiEnv }) !== 0) {
      throw new Error('seeding failed');
    }

    step(`API on :${API_PORT}`);
    // --no-launch-profile, because launchSettings.json pins :5080 and would silently override
    // the port this whole run is addressed at.
    api = spawn('dotnet', ['run', '--no-build', '--no-launch-profile'],
      { cwd: apiDir, env: apiEnv, stdio: 'ignore', detached: false });
    mkdirSync(dirname(PID_FILE), { recursive: true });
    // Written before the wait, not after: a run killed while the API is still starting is exactly
    // the case that strands one, and a pid recorded only on success would miss it.
    writeFileSync(PID_FILE, String(api.pid));
    await waitFor('API', async () => {
      try {
        const r = await fetch(`http://localhost:${API_PORT}/openapi/v1.json`);
        return r.ok;
      } catch { return false; }
    });
  }

  step('Running the browser leg');
  const playwrightArgs = ['playwright', 'test'];
  if (projectArg) playwrightArgs.push(`--project=${projectArg.split('=')[1]}`);
  playwrightArgs.push(...specs);

  exitCode = run('npx', playwrightArgs, {
    cwd: clientDir,
    env: {
      ...process.env,
      SILEXGIS_DEV_PORT: DEV_PORT,
      SILEXGIS_API_TARGET: `http://localhost:${API_PORT}`,
    },
  });
} catch (error) {
  console.error(`\n${error.message}`);
} finally {
  teardown();
}

process.exit(exitCode);
