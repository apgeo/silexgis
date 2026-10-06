// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The photo-library specs, against two libraries that hold nothing but invented pictures.
//
//   node scripts/e2e-photo-libraries.mjs            # stand both up, run the specs, take it all down
//   node scripts/e2e-photo-libraries.mjs --keep     # leave the libraries standing afterwards
//
// Why this exists. The application reads two neighbouring photo libraries, Immich and PhotoPrism,
// and four browser specs check what it does with them. They need both libraries running with a
// known set of pictures indexed, so they were written to be switched on by hand — and a library
// somebody uses holds that person's photographs, which a test has no business opening, printing or
// taking screenshots of. So the specs ran when somebody remembered, against whatever was there.
//
// This gives them libraries of their own: the two compose files this repository ships, started
// under a project name and on ports that belong to nobody else, with fresh volumes, indexing a
// directory that deploy/photo-fixtures/make-fixtures.py has just filled with generated images at
// positions the manifest beside them states. Every step an operator does once by hand in each
// library's own screens is done here through that library's own interface — the administrator,
// the key or password the application reads with, the index — which also makes this the one place
// those steps are written down as something that runs.
//
// The specs run twice: with both libraries answering, and then the one that reports a library's
// health again with PhotoPrism stopped, because "the library holds nothing here" and "the library
// is not answering" look the same on a map and telling them apart is what that spec is for.
//
// Needs Docker with Compose, and Python 3 with Pillow for the generator.

import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import process from 'node:process';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const deployDir = join(repoRoot, 'deploy');
const IMMICH_CLIENT_SOURCE = join(
  repoRoot, 'server', 'src', 'SilexGis.Infrastructure', 'PhotoLibraries', 'ImmichClient.cs');

// Beside the development libraries' ports (2283 and 2342), never on them.
const IMMICH_PORT = process.env.SILEXGIS_E2E_IMMICH_PORT ?? '2293';
const PHOTOPRISM_PORT = process.env.SILEXGIS_E2E_PHOTOPRISM_PORT ?? '2352';
const IMMICH_URL = `http://127.0.0.1:${IMMICH_PORT}`;
const PHOTOPRISM_URL = `http://127.0.0.1:${PHOTOPRISM_PORT}`;
// Named after a port for the reason the browser stack's database is: two runs that differ only by
// port must not find, and remove, each other's containers and volumes.
const PROJECT = `silexgis-e2e-photolib-${IMMICH_PORT}`;
// Beside the browser stack's own scratch, which is ignored by git and survives Playwright emptying
// its results directory at the start of every run.
const FIXTURES = join(
  repoRoot, 'client', 'node_modules', '.cache', `silexgis-e2e-photo-fixtures-${IMMICH_PORT}`);

const args = process.argv.slice(2);
const keep = args.includes('--keep');
const passthrough = args.filter((a) => a !== '--keep');

/**
 * The builds the deployment's own example names, so that what is tested is what an operator is
 * told to install. Read from the file rather than repeated here: the day the example moves to a
 * newer build, this follows it or fails saying which line it could not find.
 */
export function pinnedVersions(example) {
  const pin = (name) => {
    const match = new RegExp(`^#?\\s*${name}=(\\S+)\\s*$`, 'm').exec(example);
    if (!match) throw new Error(`deploy/.env.example no longer names ${name}`);
    return match[1];
  };
  return { immich: pin('SILEXGIS_IMMICH_VERSION'), photoprism: pin('SILEXGIS_PHOTOPRISM_VERSION') };
}

/**
 * The rights the application says an Immich key must carry, read from where it says so.
 *
 * The key made here carries exactly these and nothing more: a key that worked because it carried
 * everything would prove nothing about the list an operator is told to grant. Read from the
 * source rather than repeated, for the reason the builds are — the list has grown before, and the
 * copies of it written elsewhere did not all grow with it.
 */
export function requiredImmichPermissions(clientSource) {
  const list = /RequiredPermissions\s*=\s*\[([^\]]*)\]/.exec(clientSource)?.[1];
  const names = [...(list ?? '').matchAll(/"([a-z.]+)"/gi)].map((match) => match[1]);
  if (names.length === 0) throw new Error('the Immich client no longer lists the permissions it requires');
  return names;
}

/** The password PhotoPrism prints for an application, picked out of what the command said. */
export function appPasswordIn(output) {
  return /\b[A-Za-z0-9]{6}(?:-[A-Za-z0-9]{6}){3}\b/.exec(output ?? '')?.[0] ?? null;
}

function step(message) {
  console.log(`\n=== ${message}`);
}

function run(cmd, argv, opts = {}) {
  const r = spawnSync(cmd, argv, { stdio: 'inherit', ...opts });
  if (r.error) throw r.error;
  return r.status ?? 1;
}

function capture(cmd, argv, opts = {}) {
  const r = spawnSync(cmd, argv, { encoding: 'utf8', ...opts });
  if (r.error) throw r.error;
  if (r.status !== 0) throw new Error(`${cmd} ${argv.join(' ')} failed:\n${r.stderr || r.stdout}`);
  return r.stdout;
}

async function waitFor(label, check, timeoutMs = 180_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      if (await check()) {
        console.log(`${label}: ready after ${Math.round((Date.now() - started) / 1000)}s`);
        return;
      }
    } catch {
      // Not answering yet is what is being waited out.
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
  throw new Error(`${label}: not ready within ${timeoutMs / 1000}s`);
}

async function asJson(url, { method = 'GET', headers = {}, body } = {}) {
  const response = await fetch(url, {
    method,
    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    throw new Error(`${method} ${url} answered ${response.status}: ${(await response.text()).slice(0, 300)}`);
  }
  return response.status === 204 ? null : response.json();
}

function composeEnv(versions) {
  // Passwords for containers that exist for the length of this run, on loopback, holding
  // generated pictures. They are here in the open for the same reason the browser stack's are.
  return {
    ...process.env,
    SILEXGIS_IMMICH_VERSION: versions.immich,
    SILEXGIS_IMMICH_DB_PASSWORD: 'e2e-immich-db',
    SILEXGIS_IMMICH_LIBRARY_DIR: FIXTURES,
    SILEXGIS_IMMICH_PORT: IMMICH_PORT,
    // The administrator is made below, through the same door an operator uses once.
    SILEXGIS_IMMICH_ALLOW_SETUP: 'true',
    SILEXGIS_PHOTOPRISM_VERSION: versions.photoprism,
    SILEXGIS_PHOTOPRISM_ADMIN_PASSWORD: 'e2e-photoprism-admin',
    SILEXGIS_PHOTOPRISM_LIBRARY_DIR: FIXTURES,
    SILEXGIS_PHOTOPRISM_PORT: PHOTOPRISM_PORT,
    SILEXGIS_PHOTOPRISM_SITE_URL: `http://localhost:${PHOTOPRISM_PORT}/`,
  };
}

function compose(env, ...argv) {
  return [
    'docker',
    ['compose', '-p', PROJECT,
      '-f', join(deployDir, 'docker-compose.immich.yml'),
      '-f', join(deployDir, 'docker-compose.photoprism.yml'),
      ...argv],
    { env },
  ];
}

/** Immich: its administrator, a key carrying only what the application reads, and the index. */
async function provisionImmich(manifest, permissions) {
  await waitFor('Immich', async () => (await fetch(`${IMMICH_URL}/api/server/ping`)).ok);
  const admin = { email: 'admin@e2e.invalid', password: 'e2e-immich-admin-1' };
  await asJson(`${IMMICH_URL}/api/auth/admin-sign-up`, { method: 'POST', body: { ...admin, name: 'E2E' } });
  const { accessToken } = await asJson(`${IMMICH_URL}/api/auth/login`, { method: 'POST', body: admin });
  const asAdmin = { Authorization: `Bearer ${accessToken}` };
  const me = await asJson(`${IMMICH_URL}/api/users/me`, { headers: asAdmin });

  const { secret } = await asJson(`${IMMICH_URL}/api/api-keys`, {
    method: 'POST',
    headers: asAdmin,
    body: { name: 'silexgis-e2e', permissions },
  });

  const library = await asJson(`${IMMICH_URL}/api/libraries`, {
    method: 'POST',
    headers: asAdmin,
    body: { ownerId: me.id, name: 'fixtures', importPaths: ['/photo-library'], exclusionPatterns: [] },
  });
  await asJson(`${IMMICH_URL}/api/libraries/${library.id}/scan`, { method: 'POST', headers: asAdmin });
  await waitFor('Immich index', async () => {
    const markers = await asJson(`${IMMICH_URL}/api/map/markers`, { headers: { 'x-api-key': secret } });
    return markers.length === manifest.imageCount;
  });
  return secret;
}

/** PhotoPrism: the index, and a password for the application bound to the administrator. */
async function provisionPhotoPrism(env, manifest) {
  await waitFor('PhotoPrism', async () => (await fetch(`${PHOTOPRISM_URL}/api/v1/status`)).ok);
  // Nothing indexes on a schedule in the shipped configuration, so this is the only way one begins.
  if (run(...compose(env, 'exec', '-T', 'photoprism', 'photoprism', 'index')) !== 0) {
    throw new Error('PhotoPrism did not index the fixtures');
  }
  // The account name comes last and matters: left out, the command mints a token that reads the
  // configuration and is refused on photographs and positions with a 400 that reads like a
  // malformed request. With it, the result is a password bound to that account, used as a bearer
  // credential. The three scopes are the three things the application asks about.
  const [cmd, argv, opts] = compose(
    env, 'exec', '-T', 'photoprism',
    'photoprism', 'auth', 'add', '-n', 'silexgis-e2e', '-s', 'photos albums places', 'admin');
  const password = appPasswordIn(capture(cmd, argv, opts));
  if (!password) throw new Error('PhotoPrism printed no application password');

  const inRectangle = manifest.rectangles.find((r) => r.key === 'A');
  await waitFor('PhotoPrism index', async () => {
    const latlng = [inRectangle.latMax, inRectangle.lonMax, inRectangle.latMin, inRectangle.lonMin].join(',');
    const answer = await asJson(
      `${PHOTOPRISM_URL}/api/v1/geo?latlng=${encodeURIComponent(latlng)}&count=1000&public=true&private=false`,
      { headers: { Authorization: `Bearer ${password}` } });
    return answer.features.length === inRectangle.count;
  });
  return password;
}

function browserLeg(extraEnv, argv) {
  return run(process.execPath, [join(repoRoot, 'scripts', 'e2e.mjs'), ...argv], {
    env: { ...process.env, ...extraEnv },
  });
}

async function main() {
  const versions = pinnedVersions(readFileSync(join(deployDir, '.env.example'), 'utf8'));
  const env = composeEnv(versions);
  let exitCode = 1;
  try {
    step('Generating the invented pictures');
    rmSync(FIXTURES, { recursive: true, force: true });
    mkdirSync(FIXTURES, { recursive: true });
    const python = process.platform === 'win32' ? 'python' : 'python3';
    if (run(python, [join(deployDir, 'photo-fixtures', 'make-fixtures.py'), '--out', FIXTURES, '--force']) !== 0) {
      throw new Error('the fixture generator failed (it needs Python 3 with Pillow)');
    }
    // What the compose files require of a library directory before they will mount it.
    writeFileSync(join(FIXTURES, '.silexgis-photo-library'), 'generated for a test run\n');
    const manifest = JSON.parse(readFileSync(join(FIXTURES, 'manifest.json'), 'utf8'));

    step(`Immich ${versions.immich} on :${IMMICH_PORT}, PhotoPrism ${versions.photoprism} on :${PHOTOPRISM_PORT}`);
    run(...compose(env, 'down', '-v', '--remove-orphans'));
    if (run(...compose(env, 'up', '-d')) !== 0) throw new Error('the libraries did not start');

    step('Setting the libraries up the way an operator would');
    const immichKey = await provisionImmich(
      manifest,
      requiredImmichPermissions(readFileSync(IMMICH_CLIENT_SOURCE, 'utf8')),
    );
    const photoPrismPassword = await provisionPhotoPrism(env, manifest);

    const forTheSpecs = {
      SILEXGIS_E2E_PHOTO_LIBRARY: '1',
      SILEXGIS_E2E_PHOTO_FIXTURES: FIXTURES,
      SILEXGIS_E2E_LIBRARY_ADDRESSES: `${IMMICH_URL},${PHOTOPRISM_URL}`,
      SILEXGIS__PhotoLibraries__Immich__Enabled: 'true',
      SILEXGIS__PhotoLibraries__Immich__BaseUrl: IMMICH_URL,
      SILEXGIS__PhotoLibraries__Immich__ApiKey: immichKey,
      SILEXGIS__PhotoLibraries__PhotoPrism__Enabled: 'true',
      SILEXGIS__PhotoLibraries__PhotoPrism__BaseUrl: PHOTOPRISM_URL,
      SILEXGIS__PhotoLibraries__PhotoPrism__AccessToken: photoPrismPassword,
    };

    step('The specs, with both libraries answering');
    const answering = browserLeg(forTheSpecs, ['photo-library', '--project=desktop', '--workers=1', ...passthrough]);

    step('The health spec again, with PhotoPrism stopped');
    run(...compose(env, 'stop', 'photoprism'));
    // Its results beside the first pass's rather than over them: Playwright empties the directory
    // it is given before it starts, and the traces of a failure in the first pass are still wanted.
    const stopped = browserLeg(
      { ...forTheSpecs, SILEXGIS_E2E_LIBRARY_STATE: 'stopped' },
      ['photo-library-health', '--project=desktop', '--workers=1',
        `--output=${join('test-results', 'library-stopped')}`, ...passthrough]);

    exitCode = answering !== 0 ? answering : stopped;
  } catch (error) {
    console.error(`\n${error.message}`);
  } finally {
    if (keep) {
      console.log(`\nLeft standing (--keep): Immich ${IMMICH_URL}, PhotoPrism ${PHOTOPRISM_URL}.`);
      console.log(`Remove them with: docker compose -p ${PROJECT} down -v`);
    } else {
      step('Taking the libraries down');
      run(...compose(env, 'down', '-v', '--remove-orphans'));
      rmSync(FIXTURES, { recursive: true, force: true });
    }
  }
  process.exit(exitCode);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  await main();
}
