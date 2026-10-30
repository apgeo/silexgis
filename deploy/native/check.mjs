// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Checks a native installation from the outside, the way its users reach it, and says by name
// what is wrong.
//
//   node deploy/native/check.mjs [--url <address>] [--config-dir <dir>]
//
// Each check prints `ok` or `FAIL <name>: <reason>`; the exit code is 1 when any failed.
//
//   api-health          /health/ready through the web server: the API is up and reaches its database
//   web-app             / is the built web application, and the script it loads is served as JavaScript
//   module-scripts      every .mjs file of the build is served as JavaScript (a browser refuses a
//                       module script of any other type, and nginx's own type map has no entry for it)
//   api-proxy           /api reaches the API, rather than the web application or a gateway error
//   sign-in-discovery   /.well-known/openid-configuration answers, naming the address users reach
//   migrations          every migration the installed build defines is applied to the database
//   files-writable      the service account can write the uploaded-files directory
//   keys-writable       the service account can write the keys directory (sign-in certificates
//                       and the data-protection key ring)
//
// --url checks through another address than the public one — http://127.0.0.1 before DNS or
// TLS are in place, say. The public host name is still sent, so the web server answers as the
// installation's own site and not as the machine's default one.

import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  BUILD_MANIFEST,
  filesWithExtension,
  httpGet,
  isHealthy,
  isWindows,
  layoutOptionSpec,
  loadInstallation,
  machineFacts,
  migrationIdsFromSource,
  missingMigrations,
  psqlRun,
  readBuildManifest,
  run,
  runMain,
} from './lib.mjs';

const JS_TYPES = ['text/javascript', 'application/javascript'];
const mediaType = (headers) => String(headers['content-type'] ?? '').split(';')[0].trim().toLowerCase();

/** Why a readiness answer is not healthy, or undefined. */
export function judgeHealth(status, body) {
  if (isHealthy(status, body)) return undefined;
  if (status === 502 || status === 503 || status === 504) return `the web server answered ${status}: it cannot reach the API`;
  return `answered ${status} "${body.slice(0, 80).trim()}"`;
}

/** The module scripts an index page loads from the build's assets. */
export function moduleScripts(html) {
  return [...html.matchAll(/<script[^>]*type="module"[^>]*src="([^"]+)"/g)].map((m) => m[1]);
}

/** Why `/` is not the built web application, or undefined. */
export function judgeWebApp(status, contentType, body) {
  if (status !== 200) return `answered ${status}`;
  if (contentType !== 'text/html') return `answered ${contentType || 'no type'}, not a page`;
  if (!body.includes('<div id="root">')) return 'the page is not the web application (no application root in it)';
  const scripts = moduleScripts(body);
  if (scripts.length === 0) return 'the page loads no script';
  if (scripts.some((src) => src.startsWith('/src/'))) {
    return 'the page is the unbuilt source page: the web root is the client directory, not the built one';
  }
  return undefined;
}

/** Why a script was served with a type a browser will refuse for a module, or undefined. */
export function judgeScriptType(status, contentType) {
  if (status !== 200) return `answered ${status}`;
  if (!JS_TYPES.includes(contentType)) return `served as "${contentType || 'nothing'}"; a browser refuses a module of that type`;
  return undefined;
}

/**
 * Why an unauthenticated API request did not reach the API, or undefined.
 *
 * Every route under /api/v1 requires a sign-in unless it is on a short allow-list, so a request
 * with no credential that reaches the API is refused with 401. Anything else means it did not
 * get there: the web application itself (a 200 page, because the site falls back to it for
 * every address it does not know), a 404 from the web server, or a gateway error.
 */
export function judgeApiProxy(status, contentType) {
  if (status === 401) return undefined;
  if (status === 200 && contentType === 'text/html') {
    return '/api is answered by the web application itself: the web server does not proxy it to the API';
  }
  if (status === 502 || status === 503 || status === 504) return `the web server answered ${status}: it cannot reach the API`;
  return `expected the API's 401 for a request without a sign-in, got ${status} ${contentType}`;
}

/** Why the sign-in discovery document is wrong, or undefined. */
export function judgeDiscovery(status, contentType, body, expectedOrigin) {
  if (status !== 200) return `answered ${status}`;
  let doc;
  try {
    doc = JSON.parse(body);
  } catch {
    return `answered ${contentType || 'no type'} that is not JSON`;
  }
  for (const key of ['issuer', 'authorization_endpoint', 'token_endpoint']) {
    if (typeof doc[key] !== 'string') return `the document has no ${key}`;
  }
  if (expectedOrigin) {
    const issuer = new URL(doc.issuer).origin;
    if (issuer !== expectedOrigin) {
      return (
        `the issuer is ${issuer} but users reach ${expectedOrigin}: sign-in will be refused. The web server must ` +
        'pass the Host and the scheme through (X-Forwarded-Proto) and SILEXGIS__PublicUrl must be that address'
      );
    }
  }
  return undefined;
}

/** Whether `user` can create a file in `dir`, asked as that account. */
function writableAs(dir, user) {
  if (!fs.existsSync(dir)) return `${dir} does not exist`;
  const probe = path.join(dir, `.silexgis-check-${process.pid}`);
  const facts = machineFacts();
  try {
    if (!isWindows && facts.root && user !== 'root') {
      const result = run('runuser', ['-u', user, '--', 'touch', probe], { capture: true, allowFail: true });
      if (result.status !== 0) return `${user} cannot write ${dir}: ${result.stderr.trim()}`;
    } else {
      fs.writeFileSync(probe, '');
    }
    return undefined;
  } catch (error) {
    return `cannot write ${dir}: ${error.message}`;
  } finally {
    fs.rmSync(probe, { force: true });
  }
}

/** Whether anything accepts a connection at this address (any HTTP answer counts). */
export async function answers(address) {
  try {
    await httpGet(`${address.replace(/\/+$/, '')}/health/ready`, { timeoutMs: 5000 });
    return true;
  } catch {
    return false;
  }
}

/**
 * Runs every check against an installation. Returns `{ ok, failed }`, `failed` being the names
 * of the checks that did not pass, and prints one line per check as it goes.
 */
export async function runChecks({ layout, db }, { url } = {}) {
  if (!layout.publicUrl) return { ok: false, failed: ['configuration'] };
  const publicOrigin = new URL(layout.publicUrl).origin;
  // With no web server installed (--web-server none: the Windows default) the public address may
  // have nothing behind it at all. Then the API is asked directly on its own loopback port, as the
  // installation's own site, and the two checks that exist to prove the web server serves the built
  // web application are reported as skipped rather than failed for a server nobody installed. An
  // operator who runs a web server of their own is not affected: whatever answers on the public
  // address is checked as before.
  let through = url;
  let apiOnly = false;
  if (!url && layout.webServer === 'none' && !(await answers(layout.publicUrl))) {
    through = layout.apiUrl;
    apiOnly = true;
  }
  const base = (through ?? layout.publicUrl).replace(/\/+$/, '');
  const hostHeader = through ? new URL(layout.publicUrl).host : undefined;
  const get = (route) => httpGet(`${base}${route}`, { hostHeader });
  const failed = [];

  const check = async (name, work) => {
    let problem;
    let detail;
    try {
      const outcome = await work();
      if (typeof outcome === 'string') problem = outcome;
      else detail = outcome?.detail;
    } catch (error) {
      problem = error.message;
    }
    if (problem) {
      failed.push(name);
      console.log(`  FAIL  ${name}: ${problem}`);
    } else {
      console.log(`  ok    ${name}${detail ? ` (${detail})` : ''}`);
    }
  };

  const skipped = (name, reason) => console.log(`  skip  ${name}: ${reason}`);
  console.log(`Checking ${base}${hostHeader ? ` as ${hostHeader}` : ''}`);

  await check('api-health', async () => {
    const res = await get('/health/ready');
    return judgeHealth(res.status, res.body.toString('utf8'));
  });

  if (apiOnly) skipped('web-app', 'no web server is installed; the API was asked on its own port');
  else await check('web-app', async () => {
    const res = await get('/');
    const html = res.body.toString('utf8');
    const problem = judgeWebApp(res.status, mediaType(res.headers), html);
    if (problem) return problem;
    for (const src of moduleScripts(html)) {
      const script = await get(src);
      const scriptProblem = judgeScriptType(script.status, mediaType(script.headers));
      if (scriptProblem) return `${src}: ${scriptProblem}`;
    }
    return undefined;
  });

  if (apiOnly) skipped('module-scripts', 'no web server is installed; the API was asked on its own port');
  else await check('module-scripts', async () => {
    const files = filesWithExtension(layout.webRoot, '.mjs');
    for (const file of files) {
      const route = `/${file.split(path.sep).join('/')}`;
      const res = await get(route);
      const problem = judgeScriptType(res.status, mediaType(res.headers));
      if (problem) return `${route}: ${problem}`;
    }
    return { detail: `${files.length}` };
  });

  await check('api-proxy', async () => {
    const res = await get('/api/v1/me');
    return judgeApiProxy(res.status, mediaType(res.headers));
  });

  await check('sign-in-discovery', async () => {
    const res = await get('/.well-known/openid-configuration');
    return judgeDiscovery(res.status, mediaType(res.headers), res.body.toString('utf8'), through ? undefined : publicOrigin);
  });

  await check('migrations', async () => {
    const manifest = readBuildManifest(layout.apiDir);
    const defined = manifest?.migrations ?? migrationIdsFromSource(layout.sourceDir);
    if (defined.length === 0) return `no list of migrations: neither ${BUILD_MANIFEST} in the build nor a source tree`;
    const applied = psqlRun(
      layout,
      { kind: 'app', conn: db },
      // Every column, the identifier first: its name follows the database's naming convention
      // (migration_id here, MigrationId under EF Core's default), and the check need not know which.
      { database: db.database, sql: 'SELECT * FROM "__EFMigrationsHistory"', allowFail: true },
    );
    if (applied.status !== 0) return `cannot read the migration history: ${applied.stderr.trim()}`;
    const ids = applied.stdout
      .split(/\r?\n/)
      .filter(Boolean)
      .map((row) => row.split('|')[0]);
    const missing = missingMigrations(defined, ids);
    if (missing.length > 0) {
      return `${missing.length} of ${defined.length} not applied (first: ${missing[0]}); the API applies them as it starts unless SILEXGIS__Db__AutoMigrate is false`;
    }
    return { detail: `${defined.length} applied` };
  });

  await check('files-writable', async () => writableAs(layout.filesDir, layout.serviceUser));
  await check('keys-writable', async () => writableAs(layout.keysDir, layout.serviceUser));

  console.log(failed.length === 0 ? 'All checks passed.' : `${failed.length} check(s) failed: ${failed.join(', ')}`);
  return { ok: failed.length === 0, failed };
}

export async function main(argv) {
  const { values } = parseArgs({
    args: argv,
    options: { ...layoutOptionSpec(), url: { type: 'string' }, help: { type: 'boolean' } },
    strict: true,
  });
  if (values.help) {
    console.log('usage: node deploy/native/check.mjs [--url <address>] [--config-dir <dir>]');
    return 0;
  }
  const installation = loadInstallation(values);
  const verdict = await runChecks(installation, { url: values.url });
  return verdict.ok ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await runMain(main);
}

export const checkScript = fileURLToPath(import.meta.url);
